using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 公司 Logo：上傳沿用 file-server（API 簽發上傳 token、file-server 驗證檔頭後回呼），不屬於任何專案，所以不建立附件。
/// 產生報價 PDF 時，API 簽一個短效下載 token 向 file-server 取回圖檔（<see cref="QuoteOptions.FileServerInternalUrl"/>），
/// 依檔案 Id 快取在記憶體；取不到時記錄警告，PDF 照常產生、只是沒有 Logo。
/// </summary>
public sealed class CompanyLogoService(
    QuoteDbContext db,
    FileTokenService tokens,
    IHttpClientFactory httpClients,
    IMemoryCache cache,
    IOptions<QuoteOptions> options,
    ILogger<CompanyLogoService> logger)
{
    public const string HttpClientName = "file-server";
    public const long MaxBytes = 2 * 1024 * 1024;
    /// <summary>QuestPDF 可直接繪製的格式。</summary>
    public static readonly string[] ContentTypes = ["image/png", "image/jpeg", "image/webp"];
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(15);

    public async Task<ServiceResult<object>> CreateUploadAsync(RequestScope scope, string? fileName, long size, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        var contentType = FileRules.ContentTypeOf(name);
        if (name.Length is 0 or > 200 || contentType is null || !ContentTypes.Contains(contentType)) return ServiceResult<object>.BadRequest("Logo 只接受 PNG、JPG 或 WebP 圖片。");
        if (size <= 0 || size > MaxBytes) return ServiceResult<object>.BadRequest("Logo 圖片不可超過 2 MB。");
        var now = DateTimeOffset.UtcNow;
        var file = new FileObject { OriginalName = name, ContentType = contentType, Size = size, UploadedBy = scope.UserId };
        file.StorageKey = $"{scope.CompanyId}/logo/{file.Id}";
        db.Files.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new
        {
            fileId = file.Id,
            uploadUrl = $"{options.Value.FilePublicBasePath}/uploads/{file.Id}",
            token = tokens.Create("upload", file, UploadLifetime),
            expiresAt = now.Add(UploadLifetime),
        });
    }

    /// <summary>檢查檔案可以當 Logo：本公司、已上傳完成、圖片、大小合格。</summary>
    public async Task<string?> ValidateAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.Files.AsNoTracking().SingleOrDefaultAsync(value => value.Id == fileId, cancellationToken);
        if (file is null) return "找不到上傳的 Logo。";
        if (file.Status != FileStatuses.Ready) return "Logo 還沒上傳完成，請稍後再試。";
        if (!ContentTypes.Contains(file.ContentType) || file.Size > MaxBytes) return "Logo 只接受 2 MB 以內的 PNG、JPG 或 WebP 圖片。";
        return null;
    }

    /// <summary>設定頁預覽用的短效網址（瀏覽器直接向 file-server 取圖）。</summary>
    public async Task<string?> PreviewUrlAsync(Guid? fileId, CancellationToken cancellationToken)
    {
        if (fileId is not Guid id) return null;
        var file = await db.Files.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.Status == FileStatuses.Ready, cancellationToken);
        return file is null ? null : $"{options.Value.FilePublicBasePath}/objects/{file.Id}?token={Uri.EscapeDataString(tokens.Create("download", file, TimeSpan.FromMinutes(5), inline: true))}";
    }

    /// <summary>產生 PDF 用：取回 Logo 圖檔，失敗時回傳 null（PDF 不印 Logo，不讓整份報價失敗）。</summary>
    public async Task<byte[]?> GetBytesAsync(Guid? fileId, CancellationToken cancellationToken)
    {
        if (fileId is not Guid id) return null;
        if (cache.TryGetValue(CacheKey(id), out byte[]? cached)) return cached;
        var file = await db.Files.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.Status == FileStatuses.Ready, cancellationToken);
        if (file is null) return null;
        try
        {
            var token = tokens.Create("download", file, TimeSpan.FromMinutes(2));
            var url = $"{options.Value.FileServerInternalUrl.TrimEnd('/')}{options.Value.FilePublicBasePath}/objects/{file.Id}?token={Uri.EscapeDataString(token)}";
            var bytes = await httpClients.CreateClient(HttpClientName).GetByteArrayAsync(url, cancellationToken);
            cache.Set(CacheKey(id), bytes, TimeSpan.FromHours(1));
            return bytes;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "取不到公司 Logo（檔案 {FileId}），報價 PDF 不印 Logo", id);
            return null;
        }
    }

    private static string CacheKey(Guid fileId) => $"company-logo:{fileId}";
}
