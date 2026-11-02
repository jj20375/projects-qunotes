using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Clients;

public sealed record ClientInput(
    string? Code,
    string? Name,
    string? ShortName,
    string? TaxId,
    Guid? ParentClientId,
    string? Industry,
    string? Phone,
    string? Fax,
    string? Email,
    string? Address,
    string? InvoiceTitle,
    string? InvoiceAddress,
    string? PaymentTerms,
    Guid? OwnerUserId,
    string? Status,
    string? Note,
    ContactInput[]? Contacts);

public sealed record ContactInput(Guid? Id, string? Name, string? Title, string? Phone, string? Mobile, string? Email, bool IsPrimary, string? Note);

public sealed record ClientSearchQuery(string? Keyword, string? Status, Guid? OwnerUserId, int? Page, int? PageSize);

/// <summary>
/// 客戶與聯絡人（company scope）。有 <c>clients:read</c> 的人看得到全公司客戶（報價時要選）；
/// 修改則依資料範圍限制為自己負責或可見部門成員負責的客戶。
/// </summary>
[Route("api/clients")]
public sealed class ClientsController(ClientService service, ClientTransferService transfer) : ScopedControllerBase
{
    [HttpGet("export")]
    public async Task<ActionResult> Export(CancellationToken cancellationToken) =>
        TryCompany("clients", "read", out _, "export")
            ? File(await transfer.ExportAsync(cancellationToken), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"客戶清單_{BusinessTime.Today:yyyyMMdd}.xlsx")
            : Forbidden();

    /// <summary>dryRun=true 只回傳每列預計的處理結果，不寫入。</summary>
    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult> Import([FromForm] IFormFile? file, [FromQuery] bool dryRun, CancellationToken cancellationToken)
    {
        if (!TryCompany("clients", "read", out var scope, "import")) return Forbidden();
        if (file is null || file.Length == 0) return BadRequest(new { message = "請選擇檔案。" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "只支援 .xlsx。" });
        await using var stream = file.OpenReadStream();
        return ToResult(await transfer.ImportAsync(scope, stream, dryRun, cancellationToken));
    }

    [HttpGet]
    public async Task<ActionResult> Search([FromQuery] ClientSearchQuery query, CancellationToken cancellationToken) =>
        TryCompany("clients", "read", out var scope) ? Ok(await service.SearchAsync(scope, query, cancellationToken)) : Forbidden();

    [HttpGet("{clientId:guid}")]
    public async Task<ActionResult> Get(Guid clientId, CancellationToken cancellationToken) =>
        TryCompany("clients", "read", out var scope) ? ToResult(await service.GetAsync(scope, clientId, cancellationToken)) : Forbidden();

    [HttpPost]
    public async Task<ActionResult> Create(ClientInput input, CancellationToken cancellationToken) =>
        TryCompany("clients", "create", out var scope) ? ToResult(await service.CreateAsync(scope, input, cancellationToken)) : Forbidden();

    /// <summary>整筆更新；帶 contacts 時一併以清單取代聯絡人（未列出的刪除）。</summary>
    [HttpPut("{clientId:guid}")]
    public async Task<ActionResult> Update(Guid clientId, ClientInput input, CancellationToken cancellationToken) =>
        TryCompany("clients", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, clientId, input, cancellationToken)) : Forbidden();

    [HttpDelete("{clientId:guid}")]
    public async Task<ActionResult> Delete(Guid clientId, CancellationToken cancellationToken) =>
        TryCompany("clients", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, clientId, cancellationToken)) : Forbidden();
}
