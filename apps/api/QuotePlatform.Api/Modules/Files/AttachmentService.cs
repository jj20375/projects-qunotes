using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Files;

public sealed record CreateUploadRequest(string? OwnerType, Guid OwnerId, string? FileName, long Size, string? Category, string? Note);
public sealed record UploadCompletedRequest(long Size, string? Sha256);

/// <summary>附件所屬對象的存取判斷。</summary>
public sealed record AttachmentOwnerAccess(ProjectAccess Project, string PermissionCode, bool CanRead, bool CanWrite, bool CanDownload);

/// <summary>
/// 附件與上傳流程：
/// 1. 前端申請上傳 → API 檢查所屬對象的權限與資料範圍，建立 files（uploading）與 attachments，簽發上傳 token。
/// 2. 前端把檔案 PUT 到 file-server → file-server 驗證大小、檔頭後存檔並回呼 API。
/// 3. API 收到回呼才把檔案標示為 ready；列表只顯示 ready 的附件。
/// </summary>
/// <remarks>
/// 附件的可見性一律回推到所屬專案（ProjectAccessService）；訂單、保固、發票的附件下載另需該資源的 download 子功能。
/// 刪除附件只標示檔案為 deleted，實體檔案保留在儲存區供稽核，日後以清理工作處理。
/// </remarks>
public sealed class AttachmentService(QuoteDbContext db, ProjectAccessService projects, FileTokenService tokens, AuditService audit, IOptions<QuoteOptions> options)
{
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(5);
    private static readonly string[] DownloadOptionResources = ["orders", "warranties", "invoices"];

    public async Task<AttachmentOwnerAccess?> ResolveOwnerAsync(RequestScope scope, string ownerType, Guid ownerId, CancellationToken cancellationToken)
    {
        if (!AttachmentOwners.PermissionCodes.TryGetValue(ownerType, out var code)) return null;
        Guid? projectId = ownerType switch
        {
            "project" => ownerId,
            "quote" => await db.Quotes.Where(value => value.Id == ownerId).Select(value => (Guid?)value.ProjectId).SingleOrDefaultAsync(cancellationToken),
            "order" => await db.ProjectOrders.Where(value => value.Id == ownerId).Select(value => (Guid?)value.ProjectId).SingleOrDefaultAsync(cancellationToken),
            "shipment" => await db.ProjectShipments.Where(value => value.Id == ownerId).Select(value => (Guid?)value.ProjectId).SingleOrDefaultAsync(cancellationToken),
            "warranty" => await db.ProjectWarranties.Where(value => value.Id == ownerId).Select(value => (Guid?)value.ProjectId).SingleOrDefaultAsync(cancellationToken),
            "invoice" => await db.ProjectInvoices.Where(value => value.Id == ownerId).Select(value => (Guid?)value.ProjectId).SingleOrDefaultAsync(cancellationToken),
            _ => null,
        };
        if (projectId is not Guid id) return null;
        var project = await projects.GetAsync(scope, id, track: false, cancellationToken);
        if (project is null) return null;
        var canRead = scope.Has(code, "read");
        var canDownload = canRead && (!DownloadOptionResources.Contains(code) || scope.HasOption(code, "download"));
        return new AttachmentOwnerAccess(project, code, canRead, project.CanWriteChild(scope, code), canDownload);
    }

    public async Task<ServiceResult<object>> ListAsync(RequestScope scope, string ownerType, Guid ownerId, CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(scope, ownerType, ownerId, cancellationToken);
        if (owner is null || !owner.CanRead) return ServiceResult<object>.NotFound("找不到附件所屬的資料。");
        var rows = await db.Attachments.AsNoTracking()
            .Where(value => value.OwnerType == ownerType && value.OwnerId == ownerId && value.File.Status == FileStatuses.Ready)
            .OrderBy(value => value.CreatedAt)
            .Select(value => new
            {
                value.Id,
                value.Category,
                value.Note,
                value.CreatedAt,
                createdBy = db.Users.Where(user => user.Id == value.CreatedBy).Select(user => user.DisplayName).FirstOrDefault(),
                fileName = value.File.OriginalName,
                contentType = value.File.ContentType,
                size = value.File.Size,
            })
            .ToListAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { canUpload = owner.CanWrite, canDownload = owner.CanDownload, canDelete = owner.CanWrite, items = rows });
    }

    public async Task<ServiceResult<object>> CreateUploadAsync(RequestScope scope, CreateUploadRequest request, CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(scope, request.OwnerType ?? string.Empty, request.OwnerId, cancellationToken);
        if (owner is null || !owner.CanRead) return ServiceResult<object>.NotFound("找不到附件所屬的資料。");
        if (!owner.CanWrite) return ServiceResult<object>.Forbidden("沒有上傳附件的權限。");
        var fileName = Path.GetFileName(request.FileName ?? string.Empty).Trim();
        if (fileName.Length is 0 or > 200) return ServiceResult<object>.BadRequest("檔名不正確。");
        var contentType = FileRules.ContentTypeOf(fileName);
        if (contentType is null) return ServiceResult<object>.BadRequest($"不支援的檔案類型，可上傳：{string.Join("、", FileRules.ContentTypes.Keys)}。");
        if (request.Size <= 0 || request.Size > FileRules.MaxBytes) return ServiceResult<object>.BadRequest($"檔案大小必須介於 1 byte 到 {FileRules.MaxBytes / 1024 / 1024} MB。");

        var now = DateTimeOffset.UtcNow;
        var file = new FileObject { OriginalName = fileName, ContentType = contentType, Size = request.Size, UploadedBy = scope.UserId };
        file.StorageKey = $"{scope.CompanyId}/{now:yyyy}/{now:MM}/{file.Id}";
        db.Files.Add(file);
        db.Attachments.Add(new Attachment
        {
            OwnerType = request.OwnerType!,
            OwnerId = request.OwnerId,
            FileId = file.Id,
            Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedBy = scope.UserId,
        });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new
        {
            fileId = file.Id,
            uploadUrl = $"{options.Value.FilePublicBasePath}/uploads/{file.Id}",
            token = tokens.Create("upload", file, UploadLifetime),
            expiresAt = now.Add(UploadLifetime),
        });
    }

    /// <summary>file-server 回呼：大小相符才標示完成。不經 JWT，因此以 IgnoreQueryFilters 依 fileId 查詢。</summary>
    public async Task<ServiceResult<Empty>> CompleteAsync(Guid fileId, UploadCompletedRequest request, CancellationToken cancellationToken)
    {
        var file = await db.Files.IgnoreQueryFilters().SingleOrDefaultAsync(value => value.Id == fileId, cancellationToken);
        if (file is null) return ServiceResult<Empty>.NotFound("找不到檔案。");
        if (file.Status != FileStatuses.Uploading) return ServiceResult<Empty>.Conflict("檔案狀態不是上傳中。");
        if (request.Size != file.Size || request.Sha256 is not { Length: 64 }) return ServiceResult<Empty>.BadRequest("檔案大小或雜湊不符。");
        file.Status = FileStatuses.Ready;
        file.Sha256 = request.Sha256.ToLowerInvariant();
        file.CompletedAt = DateTimeOffset.UtcNow;
        audit.Add(file.CompanyId, file.UploadedBy, "file", file.Id, "uploaded", after: new { file.OriginalName, file.Size, file.Sha256 });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    /// <summary>簽發短效下載連結；PDF 與圖片可 inline 預覽。</summary>
    public async Task<ServiceResult<object>> DownloadAsync(RequestScope scope, Guid attachmentId, bool inline, CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments.AsNoTracking().Include(value => value.File).SingleOrDefaultAsync(value => value.Id == attachmentId && value.File.Status == FileStatuses.Ready, cancellationToken);
        if (attachment is null) return ServiceResult<object>.NotFound("找不到附件。");
        var owner = await ResolveOwnerAsync(scope, attachment.OwnerType, attachment.OwnerId, cancellationToken);
        if (owner is null || !owner.CanRead) return ServiceResult<object>.NotFound("找不到附件。");
        if (!owner.CanDownload) return ServiceResult<object>.Forbidden("沒有下載附件的權限。");
        audit.Add(scope.CompanyId, scope.UserId, "file", attachment.FileId, "download", after: new { attachment.OwnerType, attachment.OwnerId });
        await db.SaveChangesAsync(cancellationToken);
        var token = tokens.Create("download", attachment.File, DownloadLifetime, inline);
        return ServiceResult<object>.Ok(new { url = $"{options.Value.FilePublicBasePath}/objects/{attachment.FileId}?token={Uri.EscapeDataString(token)}", expiresAt = DateTimeOffset.UtcNow.Add(DownloadLifetime) });
    }

    public async Task<ServiceResult<Empty>> DeleteAsync(RequestScope scope, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments.Include(value => value.File).SingleOrDefaultAsync(value => value.Id == attachmentId, cancellationToken);
        if (attachment is null) return ServiceResult<Empty>.NotFound("找不到附件。");
        var owner = await ResolveOwnerAsync(scope, attachment.OwnerType, attachment.OwnerId, cancellationToken);
        if (owner is null || !owner.CanRead) return ServiceResult<Empty>.NotFound("找不到附件。");
        if (!owner.CanWrite) return ServiceResult<Empty>.Forbidden("沒有刪除附件的權限。");
        await RemoveAsync([attachment], scope, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    /// <summary>刪除所屬資料（例如發票）時一併移除附件；呼叫端負責 SaveChanges。</summary>
    public async Task RemoveForOwnerAsync(RequestScope scope, string ownerType, Guid ownerId, CancellationToken cancellationToken)
    {
        var attachments = await db.Attachments.Include(value => value.File).Where(value => value.OwnerType == ownerType && value.OwnerId == ownerId).ToListAsync(cancellationToken);
        await RemoveAsync(attachments, scope, cancellationToken);
    }

    private Task RemoveAsync(IEnumerable<Attachment> attachments, RequestScope scope, CancellationToken cancellationToken)
    {
        foreach (var attachment in attachments)
        {
            attachment.File.Status = FileStatuses.Deleted;
            attachment.File.DeletedAt = DateTimeOffset.UtcNow;
            db.Attachments.Remove(attachment);
            audit.Add(scope.CompanyId, scope.UserId, "file", attachment.FileId, "delete", new { attachment.OwnerType, attachment.OwnerId, attachment.File.OriginalName });
        }
        return Task.CompletedTask;
    }
}
