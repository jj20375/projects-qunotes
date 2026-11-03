using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Files;

/// <summary>
/// 業務附件與檔案上傳授權控制器（公司租戶範圍）。
/// 權限嚴格繼承所屬對象：專案、報價、訂單、出貨、保固、發票。
/// </summary>
/// <param name="service">附件核心業務服務實體。</param>
[Route("api")]
public sealed class AttachmentsController(AttachmentService service) : ScopedControllerBase
{
    /// <summary>
    /// 查詢指定業務單據所屬之全部已就緒（ready）附件清單與操作權限。
    /// </summary>
    /// <param name="ownerType">擁有者實體類型（project、quote、order、shipment、warranty、invoice）。</param>
    /// <param name="ownerId">擁有者實體識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回附件資料列清單與權限旗標。</returns>
    [HttpGet("attachments")]
    public async Task<ActionResult> List([FromQuery] string? ownerType, [FromQuery] Guid ownerId, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? ToResult(await service.ListAsync(scope, ownerType ?? string.Empty, ownerId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 申請附件上傳工作階段，簽發短效 Token 與 file-server 直接上傳位址。
    /// </summary>
    /// <param name="request">上傳請求參數。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回上傳 Token 與目標 URL。</returns>
    [HttpPost("files/upload-sessions")]
    public async Task<ActionResult> CreateUpload(CreateUploadRequest request, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? ToResult(await service.CreateUploadAsync(scope, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 簽發指定附件之安全短效下載或線上預覽簽名網址。
    /// </summary>
    /// <param name="attachmentId">附件唯一識別碼。</param>
    /// <param name="inline">是否於瀏覽器分頁直接預覽開檔（true）或強制下載（false）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回帶簽名權杖之下載 URL。</returns>
    [HttpGet("attachments/{attachmentId:guid}/download")]
    public async Task<ActionResult> Download(Guid attachmentId, [FromQuery] bool inline, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? ToResult(await service.DownloadAsync(scope, attachmentId, inline, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除指定附件（標記軟刪除並保留檔案本體供稽核）。
    /// </summary>
    /// <param name="attachmentId">附件唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("attachments/{attachmentId:guid}")]
    public async Task<ActionResult> Delete(Guid attachmentId, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? ToResult(await service.DeleteAsync(scope, attachmentId, cancellationToken)) : Forbidden();
}

/// <summary>
/// file-server 專用之內部回呼控制器。
/// 安全防禦機制：以共用密鑰驗證；正式環境 Nginx 對外阻擋 /api/internal/ 路徑，僅限 Docker Compose 內網服務通訊。
/// </summary>
/// <param name="service">附件核心業務服務實體。</param>
/// <param name="tokens">檔案簽名 Token 驗證服務實體。</param>
[ApiController]
[AllowAnonymous]
[Route("api/internal/files")]
public sealed class InternalFilesController(AttachmentService service, FileTokenService tokens) : ControllerBase
{
    /// <summary>
    /// file-server 完成實體檔案寫入與雜湊檢查後之內部通知回呼端點。
    /// </summary>
    /// <param name="fileId">檔案識別碼。</param>
    /// <param name="request">檔案大小與 SHA-256 資訊。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>成功傳回 204 NoContent。</returns>
    [HttpPost("{fileId:guid}/completed")]
    public async Task<ActionResult> Completed(Guid fileId, UploadCompletedRequest request, CancellationToken cancellationToken)
    {
        if (!tokens.IsValidCallbackSecret(Request.Headers["x-file-callback-secret"].ToString())) return Unauthorized();
        var result = await service.CompleteAsync(fileId, request, cancellationToken);
        return result.Value is null ? StatusCode(result.Status, new { message = result.Message }) : NoContent();
    }
}
