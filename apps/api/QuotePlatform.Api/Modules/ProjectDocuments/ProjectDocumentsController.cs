using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.ProjectDocuments;

public sealed record OrderInput(Guid? QuoteId, string? OrderNo, DateOnly? OrderDate, decimal? Amount, decimal? TaxAmount, string? Status, string? Note);
public sealed record ShipmentInput(Guid? OrderId, string? ShipmentNo, DateOnly? ShipDate, string? Status, string? ReceiverName, string? DeliveryAddress, string? TrackingNo, string? Note);
public sealed record WarrantyInput(Guid? OrderId, string? WarrantyNo, string? Subject, DateOnly? StartDate, DateOnly? EndDate, string? Terms, string? Note);
public sealed record InvoiceInput(Guid? OrderId, string? InvoiceNo, DateOnly? InvoiceDate, string? InvoiceType, string? BuyerName, string? BuyerTaxId, decimal? SalesAmount, decimal? TaxAmount, DateOnly? DueDate, string? Note);
public sealed record PaymentInput(decimal PaidAmount, DateOnly? PaidAt);
public sealed record VoidInput(string? Reason);

/// <summary>
/// 專案的訂單、出貨單、保固單、發票（每個專案可多筆）；層級為定案報價 → 訂單 → 出貨單／發票／保固單。可見性跟著專案的資料範圍；
/// 寫入需該資源權限（例如 <c>invoices:create</c>）加上專案寫入身分，因此會計（全公司範圍、專案唯讀）也能登錄發票。
/// </summary>
[Route("api")]
public sealed class ProjectDocumentsController(ProjectDocumentService service, ShipmentService shipments) : ScopedControllerBase
{
    // ---------- 訂單 ----------
    [HttpGet("projects/{projectId:guid}/orders")]
    /// <param name="activeOnly">true 時排除已取消的訂單（開發票選訂單用）。搜尋與分頁（keyword、page、pageSize）由後端處理。</param>
    public async Task<ActionResult> ListOrders(Guid projectId, [FromQuery] bool? activeOnly, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("orders", "read", out var scope) ? ToResult(await service.ListOrdersAsync(scope, projectId, activeOnly ?? false, query, cancellationToken)) : Forbidden();

    [HttpPost("projects/{projectId:guid}/orders")]
    public async Task<ActionResult> CreateOrder(Guid projectId, OrderInput input, CancellationToken cancellationToken) =>
        TryCompany("orders", "create", out var scope) ? ToResult(await service.SaveOrderAsync(scope, projectId, null, input, cancellationToken)) : Forbidden();

    [HttpPut("orders/{orderId:guid}")]
    public async Task<ActionResult> UpdateOrder(Guid orderId, OrderInput input, CancellationToken cancellationToken) =>
        TryCompany("orders", "update", out var scope) ? ToResult(await service.SaveOrderAsync(scope, null, orderId, input, cancellationToken)) : Forbidden();

    [HttpDelete("orders/{orderId:guid}")]
    public async Task<ActionResult> DeleteOrder(Guid orderId, CancellationToken cancellationToken) =>
        TryCompany("orders", "delete", out var scope) ? ToResult(await service.DeleteOrderAsync(scope, orderId, cancellationToken)) : Forbidden();

    // ---------- 出貨（權限沿用訂單） ----------
    [HttpGet("projects/{projectId:guid}/shipments")]
    public async Task<ActionResult> ListShipments(Guid projectId, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("orders", "read", out var scope) ? ToResult(await shipments.ListAsync(scope, projectId, query, cancellationToken)) : Forbidden();

    [HttpPost("projects/{projectId:guid}/shipments")]
    public async Task<ActionResult> CreateShipment(Guid projectId, ShipmentInput input, CancellationToken cancellationToken) =>
        TryCompany("orders", "create", out var scope) ? ToResult(await shipments.SaveAsync(scope, projectId, null, input, cancellationToken)) : Forbidden();

    [HttpPut("shipments/{shipmentId:guid}")]
    public async Task<ActionResult> UpdateShipment(Guid shipmentId, ShipmentInput input, CancellationToken cancellationToken) =>
        TryCompany("orders", "update", out var scope) ? ToResult(await shipments.SaveAsync(scope, null, shipmentId, input, cancellationToken)) : Forbidden();

    [HttpDelete("shipments/{shipmentId:guid}")]
    public async Task<ActionResult> DeleteShipment(Guid shipmentId, CancellationToken cancellationToken) =>
        TryCompany("orders", "delete", out var scope) ? ToResult(await shipments.DeleteAsync(scope, shipmentId, cancellationToken)) : Forbidden();

    // ---------- 保固 ----------
    [HttpGet("projects/{projectId:guid}/warranties")]
    public async Task<ActionResult> ListWarranties(Guid projectId, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("warranties", "read", out var scope) ? ToResult(await service.ListWarrantiesAsync(scope, projectId, query, cancellationToken)) : Forbidden();

    [HttpPost("projects/{projectId:guid}/warranties")]
    public async Task<ActionResult> CreateWarranty(Guid projectId, WarrantyInput input, CancellationToken cancellationToken) =>
        TryCompany("warranties", "create", out var scope) ? ToResult(await service.SaveWarrantyAsync(scope, projectId, null, input, cancellationToken)) : Forbidden();

    [HttpPut("warranties/{warrantyId:guid}")]
    public async Task<ActionResult> UpdateWarranty(Guid warrantyId, WarrantyInput input, CancellationToken cancellationToken) =>
        TryCompany("warranties", "update", out var scope) ? ToResult(await service.SaveWarrantyAsync(scope, null, warrantyId, input, cancellationToken)) : Forbidden();

    [HttpDelete("warranties/{warrantyId:guid}")]
    public async Task<ActionResult> DeleteWarranty(Guid warrantyId, CancellationToken cancellationToken) =>
        TryCompany("warranties", "delete", out var scope) ? ToResult(await service.DeleteWarrantyAsync(scope, warrantyId, cancellationToken)) : Forbidden();

    // ---------- 發票 ----------
    [HttpGet("projects/{projectId:guid}/invoices")]
    public async Task<ActionResult> ListInvoices(Guid projectId, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("invoices", "read", out var scope) ? ToResult(await service.ListInvoicesAsync(scope, projectId, query, cancellationToken)) : Forbidden();

    /// <summary>發票號碼即時防呆：回傳號碼是否已被使用；重複且看得到時一併回傳那張發票、訂單與專案。</summary>
    [HttpGet("invoices/lookup")]
    public async Task<ActionResult> LookupInvoiceNo([FromQuery] string? invoiceNo, [FromQuery] Guid? excludeId, CancellationToken cancellationToken) =>
        TryCompany("invoices", "read", out var scope) ? ToResult(await service.LookupInvoiceNoAsync(scope, invoiceNo, excludeId, cancellationToken)) : Forbidden();

    [HttpPost("projects/{projectId:guid}/invoices")]
    public async Task<ActionResult> CreateInvoice(Guid projectId, InvoiceInput input, CancellationToken cancellationToken) =>
        TryCompany("invoices", "create", out var scope) ? ToResult(await service.SaveInvoiceAsync(scope, projectId, null, input, cancellationToken)) : Forbidden();

    [HttpPut("invoices/{invoiceId:guid}")]
    public async Task<ActionResult> UpdateInvoice(Guid invoiceId, InvoiceInput input, CancellationToken cancellationToken) =>
        TryCompany("invoices", "update", out var scope) ? ToResult(await service.SaveInvoiceAsync(scope, null, invoiceId, input, cancellationToken)) : Forbidden();

    [HttpPost("invoices/{invoiceId:guid}/payment")]
    public async Task<ActionResult> RecordPayment(Guid invoiceId, PaymentInput input, CancellationToken cancellationToken) =>
        TryCompany("invoices", "update", out var scope, "mark_paid") ? ToResult(await service.RecordPaymentAsync(scope, invoiceId, input, cancellationToken)) : Forbidden();

    [HttpPost("invoices/{invoiceId:guid}/void")]
    public async Task<ActionResult> VoidInvoice(Guid invoiceId, VoidInput input, CancellationToken cancellationToken) =>
        TryCompany("invoices", "update", out var scope, "void") ? ToResult(await service.VoidInvoiceAsync(scope, invoiceId, input, cancellationToken)) : Forbidden();

    [HttpDelete("invoices/{invoiceId:guid}")]
    public async Task<ActionResult> DeleteInvoice(Guid invoiceId, CancellationToken cancellationToken) =>
        TryCompany("invoices", "delete", out var scope) ? ToResult(await service.DeleteInvoiceAsync(scope, invoiceId, cancellationToken)) : Forbidden();
}
