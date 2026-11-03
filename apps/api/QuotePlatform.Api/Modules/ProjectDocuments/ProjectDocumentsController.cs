using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.ProjectDocuments;

/// <summary>
/// 專案訂單建立與修改之資料輸入契約。
/// </summary>
/// <param name="QuoteId">關聯之報價單識別碼。</param>
/// <param name="OrderNo">訂單編號（若未填則由系統依規則自動取號）。</param>
/// <param name="OrderDate">訂單成立日期。</param>
/// <param name="Amount">未稅金額。</param>
/// <param name="TaxAmount">稅額。</param>
/// <param name="Status">訂單狀態（received 已接單、in_progress 處理中、delivered 已出清、cancelled 已取消）。</param>
/// <param name="Note">內部備註說明。</param>
public sealed record OrderInput(Guid? QuoteId, string? OrderNo, DateOnly? OrderDate, decimal? Amount, decimal? TaxAmount, string? Status, string? Note);

/// <summary>
/// 專案出貨單建立與修改之資料輸入契約。
/// </summary>
/// <param name="OrderId">所屬訂單識別碼。</param>
/// <param name="ShipmentNo">出貨單號。</param>
/// <param name="ShipDate">出貨日期。</param>
/// <param name="Status">出貨狀態（shipped 已出貨、delivered 已送達、cancelled 已取消）。</param>
/// <param name="ReceiverName">收件人姓名。</param>
/// <param name="DeliveryAddress">送貨詳細地址。</param>
/// <param name="TrackingNo">物流追蹤碼或貨運單號。</param>
/// <param name="Note">備註說明。</param>
public sealed record ShipmentInput(Guid? OrderId, string? ShipmentNo, DateOnly? ShipDate, string? Status, string? ReceiverName, string? DeliveryAddress, string? TrackingNo, string? Note);

/// <summary>
/// 專案保固單建立與修改之資料輸入契約。
/// </summary>
/// <param name="OrderId">所屬訂單識別碼。</param>
/// <param name="WarrantyNo">保固單號。</param>
/// <param name="Subject">保固標的主題說明。</param>
/// <param name="StartDate">保固起始日期。</param>
/// <param name="EndDate">保固截止日期。</param>
/// <param name="Terms">保固服務約定條款。</param>
/// <param name="Note">備註說明。</param>
public sealed record WarrantyInput(Guid? OrderId, string? WarrantyNo, string? Subject, DateOnly? StartDate, DateOnly? EndDate, string? Terms, string? Note);

/// <summary>
/// 專案發票開立與修改之資料輸入契約。
/// </summary>
/// <param name="OrderId">所屬訂單識別碼。</param>
/// <param name="InvoiceNo">發票號碼（如 AB-12345678）。</param>
/// <param name="InvoiceDate">開立日期。</param>
/// <param name="InvoiceType">發票聯式（duplicate 二聯、triplicate 三聯、e_invoice 電子發票）。</param>
/// <param name="BuyerName">買受人公司抬頭。</param>
/// <param name="BuyerTaxId">買受人統一編號。</param>
/// <param name="SalesAmount">銷售額（未稅）。</param>
/// <param name="TaxAmount">營業稅額。</param>
/// <param name="DueDate">預計應收日期。</param>
/// <param name="Note">備註說明。</param>
public sealed record InvoiceInput(Guid? OrderId, string? InvoiceNo, DateOnly? InvoiceDate, string? InvoiceType, string? BuyerName, string? BuyerTaxId, decimal? SalesAmount, decimal? TaxAmount, DateOnly? DueDate, string? Note);

/// <summary>
/// 發票收款登記資料輸入契約。
/// </summary>
/// <param name="PaidAmount">本次實收金額。</param>
/// <param name="PaidAt">收款入帳日期。</param>
public sealed record PaymentInput(decimal PaidAmount, DateOnly? PaidAt);

/// <summary>
/// 發票作廢請求契約。
/// </summary>
/// <param name="Reason">發票作廢原因說明。</param>
public sealed record VoidInput(string? Reason);

/// <summary>
/// 專案下游業務單據管理控制器（公司租戶範圍）。
/// 包含訂單、出貨單、保固單與發票。單據可見性嚴格繼承所屬專案之資料範圍。
/// </summary>
/// <param name="service">專案單據整合業務服務實體。</param>
/// <param name="shipments">出貨單專屬業務服務實體。</param>
[Route("api")]
public sealed class ProjectDocumentsController(ProjectDocumentService service, ShipmentService shipments) : ScopedControllerBase
{
    // ---------- 訂單 ----------

    /// <summary>
    /// 查詢指定專案之訂單列表。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="activeOnly">是否排除已取消之訂單。</param>
    /// <param name="query">分頁與關鍵字查詢條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁訂單列表與有效合計金額。</returns>
    [HttpGet("projects/{projectId:guid}/orders")]
    public async Task<ActionResult> ListOrders(Guid projectId, [FromQuery] bool? activeOnly, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("orders", "read", out var scope) ? ToResult(await service.ListOrdersAsync(scope, projectId, activeOnly ?? false, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 取得單筆訂單之完整詳細資訊。
    /// </summary>
    /// <param name="orderId">訂單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回訂單詳細物件。</returns>
    [HttpGet("orders/{orderId:guid}")]
    public async Task<ActionResult> GetOrder(Guid orderId, CancellationToken cancellationToken) =>
        TryCompany("orders", "read", out var scope) ? ToResult(await service.GetOrderAsync(scope, orderId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 於指定專案建立新訂單。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="input">訂單輸入資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新建立訂單之結果物件。</returns>
    [HttpPost("projects/{projectId:guid}/orders")]
    public async Task<ActionResult> CreateOrder(Guid projectId, OrderInput input, CancellationToken cancellationToken) =>
        TryCompany("orders", "create", out var scope) ? ToResult(await service.SaveOrderAsync(scope, projectId, null, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新既有訂單資料。
    /// </summary>
    /// <param name="orderId">訂單唯一識別碼。</param>
    /// <param name="input">訂單更新內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPut("orders/{orderId:guid}")]
    public async Task<ActionResult> UpdateOrder(Guid orderId, OrderInput input, CancellationToken cancellationToken) =>
        TryCompany("orders", "update", out var scope) ? ToResult(await service.SaveOrderAsync(scope, null, orderId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除指定訂單（若已有關聯之出貨、保固或發票則拒絕刪除）。
    /// </summary>
    /// <param name="orderId">訂單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("orders/{orderId:guid}")]
    public async Task<ActionResult> DeleteOrder(Guid orderId, CancellationToken cancellationToken) =>
        TryCompany("orders", "delete", out var scope) ? ToResult(await service.DeleteOrderAsync(scope, orderId, cancellationToken)) : Forbidden();

    // ---------- 出貨（權限沿用訂單） ----------

    /// <summary>
    /// 查詢指定專案之出貨單列表。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="query">分頁與關鍵字查詢條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁出貨單清單。</returns>
    [HttpGet("projects/{projectId:guid}/shipments")]
    public async Task<ActionResult> ListShipments(Guid projectId, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("orders", "read", out var scope) ? ToResult(await shipments.ListAsync(scope, projectId, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 於指定專案建立新出貨單。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="input">出貨單輸入資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立結果。</returns>
    [HttpPost("projects/{projectId:guid}/shipments")]
    public async Task<ActionResult> CreateShipment(Guid projectId, ShipmentInput input, CancellationToken cancellationToken) =>
        TryCompany("orders", "create", out var scope) ? ToResult(await shipments.SaveAsync(scope, projectId, null, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新既有出貨單資料。
    /// </summary>
    /// <param name="shipmentId">出貨單唯一識別碼。</param>
    /// <param name="input">出貨單更新內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPut("shipments/{shipmentId:guid}")]
    public async Task<ActionResult> UpdateShipment(Guid shipmentId, ShipmentInput input, CancellationToken cancellationToken) =>
        TryCompany("orders", "update", out var scope) ? ToResult(await shipments.SaveAsync(scope, null, shipmentId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除指定出貨單。
    /// </summary>
    /// <param name="shipmentId">出貨單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("shipments/{shipmentId:guid}")]
    public async Task<ActionResult> DeleteShipment(Guid shipmentId, CancellationToken cancellationToken) =>
        TryCompany("orders", "delete", out var scope) ? ToResult(await shipments.DeleteAsync(scope, shipmentId, cancellationToken)) : Forbidden();

    // ---------- 保固 ----------

    /// <summary>
    /// 查詢指定專案之保固單列表。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="query">分頁與關鍵字條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回保固單清單。</returns>
    [HttpGet("projects/{projectId:guid}/warranties")]
    public async Task<ActionResult> ListWarranties(Guid projectId, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("warranties", "read", out var scope) ? ToResult(await service.ListWarrantiesAsync(scope, projectId, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 建立新保固單。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="input">保固單輸入資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立結果。</returns>
    [HttpPost("projects/{projectId:guid}/warranties")]
    public async Task<ActionResult> CreateWarranty(Guid projectId, WarrantyInput input, CancellationToken cancellationToken) =>
        TryCompany("warranties", "create", out var scope) ? ToResult(await service.SaveWarrantyAsync(scope, projectId, null, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新既有保固單內容。
    /// </summary>
    /// <param name="warrantyId">保固單唯一識別碼。</param>
    /// <param name="input">保固單更新內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPut("warranties/{warrantyId:guid}")]
    public async Task<ActionResult> UpdateWarranty(Guid warrantyId, WarrantyInput input, CancellationToken cancellationToken) =>
        TryCompany("warranties", "update", out var scope) ? ToResult(await service.SaveWarrantyAsync(scope, null, warrantyId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除指定保固單。
    /// </summary>
    /// <param name="warrantyId">保固單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("warranties/{warrantyId:guid}")]
    public async Task<ActionResult> DeleteWarranty(Guid warrantyId, CancellationToken cancellationToken) =>
        TryCompany("warranties", "delete", out var scope) ? ToResult(await service.DeleteWarrantyAsync(scope, warrantyId, cancellationToken)) : Forbidden();

    // ---------- 發票 ----------

    /// <summary>
    /// 查詢指定專案之發票列表與開立收款統計金額。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="query">分頁與關鍵字條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁發票列表與統計數據。</returns>
    [HttpGet("projects/{projectId:guid}/invoices")]
    public async Task<ActionResult> ListInvoices(Guid projectId, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("invoices", "read", out var scope) ? ToResult(await service.ListInvoicesAsync(scope, projectId, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 即時檢查發票號碼是否已被使用（重複防呆查詢）。
    /// </summary>
    /// <param name="invoiceNo">發票號碼字串。</param>
    /// <param name="excludeId">排除檢查之本筆發票識別碼（修改時傳入）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回發票查重結果物件。</returns>
    [HttpGet("invoices/lookup")]
    public async Task<ActionResult> LookupInvoiceNo([FromQuery] string? invoiceNo, [FromQuery] Guid? excludeId, CancellationToken cancellationToken) =>
        TryCompany("invoices", "read", out var scope) ? ToResult(await service.LookupInvoiceNoAsync(scope, invoiceNo, excludeId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 建立專案發票。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="input">發票輸入資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立結果。</returns>
    [HttpPost("projects/{projectId:guid}/invoices")]
    public async Task<ActionResult> CreateInvoice(Guid projectId, InvoiceInput input, CancellationToken cancellationToken) =>
        TryCompany("invoices", "create", out var scope) ? ToResult(await service.SaveInvoiceAsync(scope, projectId, null, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新既有發票資料。
    /// </summary>
    /// <param name="invoiceId">發票唯一識別碼。</param>
    /// <param name="input">發票更新內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPut("invoices/{invoiceId:guid}")]
    public async Task<ActionResult> UpdateInvoice(Guid invoiceId, InvoiceInput input, CancellationToken cancellationToken) =>
        TryCompany("invoices", "update", out var scope) ? ToResult(await service.SaveInvoiceAsync(scope, null, invoiceId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 登記發票收款紀錄。
    /// </summary>
    /// <param name="invoiceId">發票唯一識別碼。</param>
    /// <param name="input">收款金額與入帳日期。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回收款更新結果。</returns>
    [HttpPost("invoices/{invoiceId:guid}/payment")]
    public async Task<ActionResult> RecordPayment(Guid invoiceId, PaymentInput input, CancellationToken cancellationToken) =>
        TryCompany("invoices", "update", out var scope, "mark_paid") ? ToResult(await service.RecordPaymentAsync(scope, invoiceId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 將指定發票標記為作廢。
    /// </summary>
    /// <param name="invoiceId">發票唯一識別碼。</param>
    /// <param name="input">作廢原因備註。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回作廢結果。</returns>
    [HttpPost("invoices/{invoiceId:guid}/void")]
    public async Task<ActionResult> VoidInvoice(Guid invoiceId, VoidInput input, CancellationToken cancellationToken) =>
        TryCompany("invoices", "update", out var scope, "void") ? ToResult(await service.VoidInvoiceAsync(scope, invoiceId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除指定發票。
    /// </summary>
    /// <param name="invoiceId">發票唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("invoices/{invoiceId:guid}")]
    public async Task<ActionResult> DeleteInvoice(Guid invoiceId, CancellationToken cancellationToken) =>
        TryCompany("invoices", "delete", out var scope) ? ToResult(await service.DeleteInvoiceAsync(scope, invoiceId, cancellationToken)) : Forbidden();
}
