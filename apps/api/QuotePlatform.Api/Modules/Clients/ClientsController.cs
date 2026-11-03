using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Clients;

/// <summary>
/// 客戶主檔建立與修改之資料輸入契約。
/// </summary>
/// <param name="Code">客戶代碼（選填，若未填由後端自動取號）。</param>
/// <param name="Name">客戶全名。</param>
/// <param name="ShortName">客戶簡稱。</param>
/// <param name="TaxId">統一編號。</param>
/// <param name="ParentClientId">母公司或集團客戶識別碼。</param>
/// <param name="Industry">產業類別名稱。</param>
/// <param name="Phone">公司代表電話。</param>
/// <param name="Fax">傳真號碼。</param>
/// <param name="Email">公司代表電子郵件信箱。</param>
/// <param name="Address">公司登記或聯絡地址。</param>
/// <param name="InvoiceTitle">發票買受人抬頭。</param>
/// <param name="InvoiceAddress">發票寄送地址。</param>
/// <param name="PaymentTerms">交易付款條件說明（如月結 30 天）。</param>
/// <param name="OwnerUserId">負責業務人員使用者 ID。</param>
/// <param name="DepartmentId">客戶歸屬部門 ID（僅全公司範圍管理者可變更）。</param>
/// <param name="Status">狀態（active 啟用、inactive 停用）。</param>
/// <param name="Note">內部備註說明。</param>
/// <param name="Contacts">聯絡人清單。</param>
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
    Guid? DepartmentId,
    string? Status,
    string? Note,
    ContactInput[]? Contacts);

/// <summary>
/// 客戶聯絡人輸入契約。
/// </summary>
/// <param name="Id">聯絡人識別碼（更新時傳入，新增時為 null）。</param>
/// <param name="Name">聯絡人姓名。</param>
/// <param name="Title">職稱。</param>
/// <param name="Phone">辦公室電話。</param>
/// <param name="Mobile">行動電話。</param>
/// <param name="Email">電子郵件信箱。</param>
/// <param name="IsPrimary">是否設為預設主要聯絡人。</param>
/// <param name="Note">備註說明。</param>
public sealed record ContactInput(Guid? Id, string? Name, string? Title, string? Phone, string? Mobile, string? Email, bool IsPrimary, string? Note);

/// <summary>
/// 客戶分頁搜尋查詢條件契約。
/// </summary>
/// <param name="Keyword">搜尋關鍵字（代碼、名稱、統編或電話）。</param>
/// <param name="Status">狀態篩選（active 或 inactive）。</param>
/// <param name="OwnerUserId">鎖定特定負責業務 ID。</param>
/// <param name="Page">頁碼（由 1 起算）。</param>
/// <param name="PageSize">每頁筆數。</param>
public sealed record ClientSearchQuery(string? Keyword, string? Status, Guid? OwnerUserId, int? Page, int? PageSize);

/// <summary>
/// 客戶主檔與聯絡人管理控制器（公司租戶範圍）。
/// 可見範圍依資料範圍與客戶所屬部門決定；修改權限限制為自己負責或可見部門成員負責之客戶。
/// </summary>
/// <param name="service">客戶領域核心服務實體。</param>
/// <param name="transfer">客戶 Excel 匯入與匯出資料傳輸服務實體。</param>
[Route("api/clients")]
public sealed class ClientsController(ClientService service, ClientTransferService transfer) : ScopedControllerBase
{
    /// <summary>
    /// 匯出符合資料範圍之客戶清單為 Excel 試算表。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 Excel 試算表二進位串流檔案。</returns>
    [HttpGet("export")]
    public async Task<ActionResult> Export(CancellationToken cancellationToken) =>
        TryCompany("clients", "read", out var scope, "export")
            ? File(await transfer.ExportAsync(scope, cancellationToken), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"客戶清單_{BusinessTime.Today:yyyyMMdd}.xlsx")
            : Forbidden();

    /// <summary>
    /// 上傳客戶 Excel 檔進行批次驗證試算或正式匯入。
    /// </summary>
    /// <param name="file">上傳之 Excel 檔案（限 .xlsx 格式）。</param>
    /// <param name="dryRun">是否僅為模擬比對（true 僅回傳預計更新與新增筆數，不寫入資料庫）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回匯入處理明細與統計摘要。</returns>
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

    /// <summary>
    /// 條件搜尋客戶清單並分頁回傳。
    /// </summary>
    /// <param name="query">查詢條件與分頁參數。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁客戶資料列與總筆數。</returns>
    [HttpGet]
    public async Task<ActionResult> Search([FromQuery] ClientSearchQuery query, CancellationToken cancellationToken) =>
        TryCompany("clients", "read", out var scope) ? Ok(await service.SearchAsync(scope, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 取得單一客戶主檔詳細資料（含聯絡人明細）。
    /// </summary>
    /// <param name="clientId">客戶唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回客戶完整明細。</returns>
    [HttpGet("{clientId:guid}")]
    public async Task<ActionResult> Get(Guid clientId, CancellationToken cancellationToken) =>
        TryCompany("clients", "read", out var scope) ? ToResult(await service.GetAsync(scope, clientId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 建立新客戶主檔。
    /// </summary>
    /// <param name="input">客戶輸入資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新建立之客戶 ID 與客戶代碼。</returns>
    [HttpPost]
    public async Task<ActionResult> Create(ClientInput input, CancellationToken cancellationToken) =>
        TryCompany("clients", "create", out var scope) ? ToResult(await service.CreateAsync(scope, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 整筆更新客戶主檔與聯絡人清單。
    /// </summary>
    /// <param name="clientId">客戶唯一識別碼。</param>
    /// <param name="input">更新內容資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPut("{clientId:guid}")]
    public async Task<ActionResult> Update(Guid clientId, ClientInput input, CancellationToken cancellationToken) =>
        TryCompany("clients", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, clientId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除指定客戶（若已有報價單或專案關聯則拒絕刪除）。
    /// </summary>
    /// <param name="clientId">客戶唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("{clientId:guid}")]
    public async Task<ActionResult> Delete(Guid clientId, CancellationToken cancellationToken) =>
        TryCompany("clients", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, clientId, cancellationToken)) : Forbidden();
}
