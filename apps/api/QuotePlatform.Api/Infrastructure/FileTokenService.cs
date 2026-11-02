using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>file-server token 的內容；欄位名稱與 file-server/src/server.mjs 讀取的一致。</summary>
public sealed record FileTokenPayload(string Kind, Guid FileId, string StorageKey, long FileSize, string ContentType, string FileName, bool Inline, long ExpiresAtUnix);

/// <summary>
/// 簽發 file-server 的上傳／下載 token：<c>base64(JSON).hex(HMAC-SHA256)</c>（與美達特 media-server 相同格式）。
/// </summary>
/// <remarks>
/// file-server 不連資料庫，能存取哪個檔案完全由這裡簽入的 fileId 與 storageKey 決定；
/// 因此只有在 API 完成權限與資料範圍檢查後才簽發，且有效時間很短（上傳 15 分鐘、下載 5 分鐘）。
/// </remarks>
public sealed class FileTokenService(IOptions<QuoteOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Create(string kind, FileObject file, TimeSpan lifetime, bool inline = false)
    {
        var payload = new FileTokenPayload(kind, file.Id, file.StorageKey, file.Size, file.ContentType, file.OriginalName, inline, DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds());
        return Sign(JsonSerializer.Serialize(payload, JsonOptions), options.Value.FileTokenKey);
    }

    public static string Sign(string json, string key)
    {
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body)));
        return $"{body}.{signature}";
    }

    /// <summary>以固定時間比較 file-server 回呼帶的共享密鑰。</summary>
    public bool IsValidCallbackSecret(string? presented)
    {
        var expected = options.Value.FileCallbackSecret;
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(expected)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>供 <c>--self-test</c>：簽章格式與 file-server 的 verifyToken 相容（HMAC 是對 base64 字串本身計算）。</summary>
    public static void AssertExamples()
    {
        var token = Sign("""{"kind":"download"}""", new string('k', 32));
        var parts = token.Split('.');
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(new string('k', 32)), Encoding.UTF8.GetBytes(parts[0])));
        if (parts.Length != 2 || parts[1] != expected || Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])) != """{"kind":"download"}""")
        {
            throw new InvalidOperationException("檔案 token 簽章格式驗證失敗。");
        }
    }
}
