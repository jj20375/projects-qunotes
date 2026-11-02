using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Files;

/// <summary>附件（company scope）。權限依所屬對象：專案、報價、訂單、保固、發票。</summary>
[Route("api")]
public sealed class AttachmentsController(AttachmentService service) : ScopedControllerBase
{
    [HttpGet("attachments")]
    public async Task<ActionResult> List([FromQuery] string? ownerType, [FromQuery] Guid ownerId, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? ToResult(await service.ListAsync(scope, ownerType ?? string.Empty, ownerId, cancellationToken)) : Forbidden();

    /// <summary>申請上傳：回傳 file-server 的上傳網址與短效 token。</summary>
    [HttpPost("files/upload-sessions")]
    public async Task<ActionResult> CreateUpload(CreateUploadRequest request, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? ToResult(await service.CreateUploadAsync(scope, request, cancellationToken)) : Forbidden();

    [HttpGet("attachments/{attachmentId:guid}/download")]
    public async Task<ActionResult> Download(Guid attachmentId, [FromQuery] bool inline, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? ToResult(await service.DownloadAsync(scope, attachmentId, inline, cancellationToken)) : Forbidden();

    [HttpDelete("attachments/{attachmentId:guid}")]
    public async Task<ActionResult> Delete(Guid attachmentId, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? ToResult(await service.DeleteAsync(scope, attachmentId, cancellationToken)) : Forbidden();
}

/// <summary>
/// file-server 專用的內部回呼。以共享密鑰驗證；正式環境 nginx 對外封鎖 <c>/api/internal/</c>，只有 compose 內網可呼叫。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/internal/files")]
public sealed class InternalFilesController(AttachmentService service, FileTokenService tokens) : ControllerBase
{
    [HttpPost("{fileId:guid}/completed")]
    public async Task<ActionResult> Completed(Guid fileId, UploadCompletedRequest request, CancellationToken cancellationToken)
    {
        if (!tokens.IsValidCallbackSecret(Request.Headers["x-file-callback-secret"].ToString())) return Unauthorized();
        var result = await service.CompleteAsync(fileId, request, cancellationToken);
        return result.Value is null ? StatusCode(result.Status, new { message = result.Message }) : NoContent();
    }
}
