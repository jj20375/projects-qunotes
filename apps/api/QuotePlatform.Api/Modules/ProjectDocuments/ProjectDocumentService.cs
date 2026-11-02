using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;
using QuotePlatform.Api.Modules.Files;

namespace QuotePlatform.Api.Modules.ProjectDocuments;

/// <summary>訂單、保固單、發票的 CRUD 與發票收款／作廢。</summary>
/// <remarks>
/// 層級：定案報價 → 訂單 → 出貨單／發票／保固單。新增訂單必須對應同專案已接受的報價，新增發票與保固單必須對應同專案未取消的訂單；
/// 規則上線前建立、沒有對應的舊資料，編輯時可以維持空白。
/// </remarks>
public sealed partial class ProjectDocumentService(QuoteDbContext db, ProjectAccessService access, AttachmentService attachments, AuditService audit)
{
    private static readonly string[] OrderStatuses = ["received", "in_progress", "delivered", "cancelled"];
    private static readonly string[] InvoiceTypes = ["duplicate", "triplicate", "e_invoice"];

    [GeneratedRegex(@"^([A-Z]{2})-?(\d{8})$")]
    private static partial Regex InvoiceNoPattern();

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex TaxIdPattern();

    // ---------------------------------------------------------------- 訂單

    /// <summary>目前這一頁的列用到的訂單號碼（只查需要的，不載入整個專案的訂單）。</summary>
    internal async Task<Dictionary<Guid, string>> OrderNosAsync(IEnumerable<Guid> orderIds, CancellationToken cancellationToken)
    {
        var ids = orderIds.Distinct().ToList();
        return ids.Count == 0 ? [] : await db.ProjectOrders.AsNoTracking().Where(value => ids.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.OrderNo, cancellationToken);
    }

    public async Task<ServiceResult<object>> ListOrdersAsync(RequestScope scope, Guid projectId, bool activeOnly, ListQuery query, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        var all = db.ProjectOrders.AsNoTracking().Where(value => value.ProjectId == projectId);
        // 合計涵蓋整個專案（不受分頁、關鍵字影響）。
        var activeTotal = await all.Where(value => value.Status != "cancelled").SumAsync(value => (decimal?)value.Total, cancellationToken) ?? 0;
        var orders = activeOnly ? all.Where(value => value.Status != "cancelled") : all;
        if (query.KeywordText is string keyword) orders = orders.Where(value => value.OrderNo.Contains(keyword) || (value.Note != null && value.Note.Contains(keyword)));
        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders.OrderByDescending(value => value.OrderDate).ThenByDescending(value => value.OrderNo).Skip(query.Skip).Take(query.PageSizeValue).ToListAsync(cancellationToken);
        var quoteNos = await QuoteLabelsAsync(rows.Where(value => value.QuoteId is not null).Select(value => value.QuoteId!.Value), cancellationToken);
        var counts = await AttachmentCountsAsync("order", rows.Select(value => value.Id), cancellationToken);
        return ServiceResult<object>.Ok(new
        {
            canCreate = project.CanWriteChild(scope, "orders", "create"),
            canUpdate = project.CanWriteChild(scope, "orders"),
            canDelete = project.CanWriteChild(scope, "orders", "delete"),
            activeTotal,
            total,
            page = query.PageValue,
            pageSize = query.PageSizeValue,
            items = rows.Select(value => new { value.Id, value.QuoteId, quoteNo = value.QuoteId is Guid id ? quoteNos.GetValueOrDefault(id) : null, value.OrderNo, value.OrderDate, value.Amount, value.TaxAmount, value.Total, value.Status, value.Note, attachmentCount = counts.GetValueOrDefault(value.Id) }),
        });
    }

    public async Task<ServiceResult<object>> SaveOrderAsync(RequestScope scope, Guid? projectId, Guid? orderId, OrderInput input, CancellationToken cancellationToken)
    {
        var order = orderId is Guid id ? await db.ProjectOrders.SingleOrDefaultAsync(value => value.Id == id, cancellationToken) : null;
        if (orderId is not null && order is null) return ServiceResult<object>.NotFound("找不到訂單。");
        var project = await access.GetAsync(scope, order?.ProjectId ?? projectId!.Value, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (!project.CanWriteChild(scope, "orders", order is null ? "create" : "update")) return ServiceResult<object>.Forbidden("您對此專案的訂單只有檢視權限。");

        if (string.IsNullOrWhiteSpace(input.OrderNo) || input.OrderDate is null) return ServiceResult<object>.BadRequest("請輸入訂單號碼與訂單日期。");
        if (input.Amount is null or < 0 || input.TaxAmount < 0) return ServiceResult<object>.BadRequest("金額不可為負數。");
        if (input.Status is not null && !OrderStatuses.Contains(input.Status)) return ServiceResult<object>.BadRequest("訂單狀態不正確。");
        if (input.QuoteId is Guid quoteId)
        {
            var quoteStatus = await db.Quotes.Where(value => value.Id == quoteId && value.ProjectId == project.Project.Id).Select(value => value.Status).SingleOrDefaultAsync(cancellationToken);
            if (quoteStatus is null) return ServiceResult<object>.BadRequest("報價單不屬於此專案。");
            if (quoteStatus != QuoteStatuses.Accepted) return ServiceResult<object>.BadRequest("訂單只能對應客戶已接受（定案）的報價單。");
        }
        else if (order is null || order.QuoteId is not null) return ServiceResult<object>.BadRequest("請選擇訂單對應的定案報價單。");

        var before = order is null ? null : new { order.OrderNo, order.Total, order.Status };
        if (order is null)
        {
            order = new ProjectOrder { ProjectId = project.Project.Id, CreatedBy = scope.UserId };
            db.ProjectOrders.Add(order);
        }
        order.QuoteId = input.QuoteId;
        order.OrderNo = input.OrderNo.Trim();
        order.OrderDate = input.OrderDate.Value;
        order.Amount = input.Amount.Value;
        order.TaxAmount = input.TaxAmount ?? 0;
        order.Total = order.Amount + order.TaxAmount;
        order.Status = input.Status ?? order.Status;
        order.Note = Clean(input.Note);
        order.UpdatedBy = scope.UserId;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "order", order.Id, before is null ? "create" : "update", before, new { order.OrderNo, order.Total, order.Status });
        await db.SaveChangesAsync(cancellationToken);
        return before is null ? ServiceResult<object>.Created(new { order.Id }) : ServiceResult<object>.Ok(new { order.Id });
    }

    public async Task<ServiceResult<Empty>> DeleteOrderAsync(RequestScope scope, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.ProjectOrders.SingleOrDefaultAsync(value => value.Id == orderId, cancellationToken);
        if (order is null) return ServiceResult<Empty>.NotFound("找不到訂單。");
        var project = await access.GetAsync(scope, order.ProjectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<Empty>.NotFound("找不到訂單。");
        if (!project.CanWriteChild(scope, "orders", "delete")) return ServiceResult<Empty>.Forbidden("沒有刪除訂單的權限。");
        if (await db.ProjectInvoices.AnyAsync(value => value.OrderId == orderId && value.Status == "issued", cancellationToken)) return ServiceResult<Empty>.BadRequest("此訂單已有開立中的發票，請先處理發票。");
        if (await db.ProjectShipments.AnyAsync(value => value.OrderId == orderId, cancellationToken) || await db.ProjectWarranties.AnyAsync(value => value.OrderId == orderId, cancellationToken))
        {
            return ServiceResult<Empty>.BadRequest("此訂單底下還有出貨單或保固單，請先刪除。");
        }
        order.IsDeleted = true;
        order.DeletedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "order", orderId, "delete", new { order.OrderNo, order.Total });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    // ---------------------------------------------------------------- 保固

    public async Task<ServiceResult<object>> ListWarrantiesAsync(RequestScope scope, Guid projectId, ListQuery query, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        var warranties = db.ProjectWarranties.AsNoTracking().Where(value => value.ProjectId == projectId);
        if (query.KeywordText is string keyword) warranties = warranties.Where(value => value.WarrantyNo.Contains(keyword) || value.Subject.Contains(keyword));
        var total = await warranties.CountAsync(cancellationToken);
        var rows = await warranties.OrderBy(value => value.EndDate).ThenBy(value => value.WarrantyNo).Skip(query.Skip).Take(query.PageSizeValue).ToListAsync(cancellationToken);
        var orderNos = await OrderNosAsync(rows.Where(value => value.OrderId is not null).Select(value => value.OrderId!.Value), cancellationToken);
        var counts = await AttachmentCountsAsync("warranty", rows.Select(value => value.Id), cancellationToken);
        var today = BusinessTime.Today;
        return ServiceResult<object>.Ok(new
        {
            canCreate = project.CanWriteChild(scope, "warranties", "create"),
            canUpdate = project.CanWriteChild(scope, "warranties"),
            canDelete = project.CanWriteChild(scope, "warranties", "delete"),
            total,
            page = query.PageValue,
            pageSize = query.PageSizeValue,
            items = rows.Select(value => new
            {
                value.Id,
                value.OrderId,
                orderNo = value.OrderId is Guid id ? orderNos.GetValueOrDefault(id) : null,
                value.WarrantyNo,
                value.Subject,
                value.StartDate,
                value.EndDate,
                value.Terms,
                value.Note,
                // active／expiring（90 天內到期）／expired，供畫面標示。
                state = value.EndDate < today ? "expired" : value.EndDate <= today.AddDays(90) ? "expiring" : "active",
                attachmentCount = counts.GetValueOrDefault(value.Id),
            }),
        });
    }

    public async Task<ServiceResult<object>> SaveWarrantyAsync(RequestScope scope, Guid? projectId, Guid? warrantyId, WarrantyInput input, CancellationToken cancellationToken)
    {
        var warranty = warrantyId is Guid id ? await db.ProjectWarranties.SingleOrDefaultAsync(value => value.Id == id, cancellationToken) : null;
        if (warrantyId is not null && warranty is null) return ServiceResult<object>.NotFound("找不到保固單。");
        var project = await access.GetAsync(scope, warranty?.ProjectId ?? projectId!.Value, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (!project.CanWriteChild(scope, "warranties", warranty is null ? "create" : "update")) return ServiceResult<object>.Forbidden("您對此專案的保固單只有檢視權限。");

        if (string.IsNullOrWhiteSpace(input.WarrantyNo) || string.IsNullOrWhiteSpace(input.Subject)) return ServiceResult<object>.BadRequest("請輸入保固單號與保固項目。");
        if (input.StartDate is null || input.EndDate is null) return ServiceResult<object>.BadRequest("請輸入保固起訖日。");
        if (input.EndDate < input.StartDate) return ServiceResult<object>.BadRequest("保固結束日不可早於開始日。");
        if (await CheckOrderAsync(project.Project.Id, input.OrderId, warranty?.OrderId, warranty is null, cancellationToken) is string orderError) return ServiceResult<object>.BadRequest(orderError);

        var isNew = warranty is null;
        if (warranty is null)
        {
            warranty = new ProjectWarranty { ProjectId = project.Project.Id, CreatedBy = scope.UserId };
            db.ProjectWarranties.Add(warranty);
        }
        warranty.OrderId = input.OrderId;
        warranty.WarrantyNo = input.WarrantyNo.Trim();
        warranty.Subject = input.Subject.Trim();
        warranty.StartDate = input.StartDate.Value;
        warranty.EndDate = input.EndDate.Value;
        warranty.Terms = Clean(input.Terms);
        warranty.Note = Clean(input.Note);
        warranty.UpdatedBy = scope.UserId;
        warranty.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "warranty", warranty.Id, isNew ? "create" : "update", after: new { warranty.WarrantyNo, warranty.StartDate, warranty.EndDate });
        await db.SaveChangesAsync(cancellationToken);
        return isNew ? ServiceResult<object>.Created(new { warranty.Id }) : ServiceResult<object>.Ok(new { warranty.Id });
    }

    public async Task<ServiceResult<Empty>> DeleteWarrantyAsync(RequestScope scope, Guid warrantyId, CancellationToken cancellationToken)
    {
        var warranty = await db.ProjectWarranties.SingleOrDefaultAsync(value => value.Id == warrantyId, cancellationToken);
        if (warranty is null) return ServiceResult<Empty>.NotFound("找不到保固單。");
        var project = await access.GetAsync(scope, warranty.ProjectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<Empty>.NotFound("找不到保固單。");
        if (!project.CanWriteChild(scope, "warranties", "delete")) return ServiceResult<Empty>.Forbidden("沒有刪除保固單的權限。");
        warranty.IsDeleted = true;
        warranty.DeletedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "warranty", warrantyId, "delete", new { warranty.WarrantyNo });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    // ---------------------------------------------------------------- 發票

    public async Task<ServiceResult<object>> ListInvoicesAsync(RequestScope scope, Guid projectId, ListQuery query, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        var all = db.ProjectInvoices.AsNoTracking().Where(value => value.ProjectId == projectId);
        // 已開立與已收款合計涵蓋整個專案（不受分頁、關鍵字影響）。
        var sums = await all.Where(value => value.Status == "issued").GroupBy(_ => 1).Select(group => new { Total = group.Sum(value => value.TotalAmount), Paid = group.Sum(value => value.PaidAmount) }).SingleOrDefaultAsync(cancellationToken);
        var invoices = query.KeywordText is string keyword ? all.Where(value => value.InvoiceNo.Contains(keyword) || (value.BuyerName != null && value.BuyerName.Contains(keyword))) : all;
        var total = await invoices.CountAsync(cancellationToken);
        var rows = await invoices.OrderByDescending(value => value.InvoiceDate).ThenByDescending(value => value.InvoiceNo).Skip(query.Skip).Take(query.PageSizeValue).ToListAsync(cancellationToken);
        var orderNos = await OrderNosAsync(rows.Where(value => value.OrderId is not null).Select(value => value.OrderId!.Value), cancellationToken);
        var counts = await AttachmentCountsAsync("invoice", rows.Select(value => value.Id), cancellationToken);
        var today = BusinessTime.Today;
        return ServiceResult<object>.Ok(new
        {
            canCreate = project.CanWriteChild(scope, "invoices", "create"),
            canUpdate = project.CanWriteChild(scope, "invoices"),
            canDelete = project.CanWriteChild(scope, "invoices", "delete"),
            canMarkPaid = project.CanWriteChild(scope, "invoices") && scope.HasOption("invoices", "mark_paid"),
            canVoid = project.CanWriteChild(scope, "invoices") && scope.HasOption("invoices", "void"),
            totalAmount = sums?.Total ?? 0,
            paidAmount = sums?.Paid ?? 0,
            total,
            page = query.PageValue,
            pageSize = query.PageSizeValue,
            items = rows.Select(value => new
            {
                value.Id,
                value.OrderId,
                orderNo = value.OrderId is Guid id ? orderNos.GetValueOrDefault(id) : null,
                value.InvoiceNo,
                value.InvoiceDate,
                value.InvoiceType,
                value.BuyerName,
                value.BuyerTaxId,
                value.SalesAmount,
                value.TaxAmount,
                value.TotalAmount,
                value.Status,
                value.PaymentStatus,
                value.PaidAmount,
                value.DueDate,
                value.PaidAt,
                value.VoidReason,
                value.Note,
                isOverdue = value.Status == "issued" && value.PaymentStatus != "paid" && value.DueDate < today,
                attachmentCount = counts.GetValueOrDefault(value.Id),
            }),
        });
    }

    public async Task<ServiceResult<object>> SaveInvoiceAsync(RequestScope scope, Guid? projectId, Guid? invoiceId, InvoiceInput input, CancellationToken cancellationToken)
    {
        var invoice = invoiceId is Guid id ? await db.ProjectInvoices.SingleOrDefaultAsync(value => value.Id == id, cancellationToken) : null;
        if (invoiceId is not null && invoice is null) return ServiceResult<object>.NotFound("找不到發票。");
        var project = await access.GetAsync(scope, invoice?.ProjectId ?? projectId!.Value, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (!project.CanWriteChild(scope, "invoices", invoice is null ? "create" : "update")) return ServiceResult<object>.Forbidden("您對此專案的發票只有檢視權限。");
        if (invoice?.Status == "void") return ServiceResult<object>.BadRequest("已作廢的發票不能修改。");

        var match = InvoiceNoPattern().Match((input.InvoiceNo ?? string.Empty).Trim().ToUpperInvariant());
        if (!match.Success) return ServiceResult<object>.BadRequest("發票號碼格式為兩碼英文加八碼數字，例如 AB-12345678。");
        var invoiceNo = $"{match.Groups[1].Value}-{match.Groups[2].Value}";
        if (input.InvoiceDate is null) return ServiceResult<object>.BadRequest("請輸入發票日期。");
        if (input.InvoiceType is not null && !InvoiceTypes.Contains(input.InvoiceType)) return ServiceResult<object>.BadRequest("發票類型不正確。");
        if (!string.IsNullOrWhiteSpace(input.BuyerTaxId) && !TaxIdPattern().IsMatch(input.BuyerTaxId.Trim())) return ServiceResult<object>.BadRequest("買受人統一編號必須是 8 碼數字。");
        if (input.SalesAmount is null or < 0 || input.TaxAmount < 0) return ServiceResult<object>.BadRequest("金額不可為負數。");
        if (await CheckOrderAsync(project.Project.Id, input.OrderId, invoice?.OrderId, invoice is null, cancellationToken) is string orderError) return ServiceResult<object>.BadRequest(orderError);
        if (await db.ProjectInvoices.AnyAsync(value => value.InvoiceNo == invoiceNo && value.Id != invoiceId, cancellationToken)) return ServiceResult<object>.Conflict($"發票號碼 {invoiceNo} 已登錄過。");

        var total = input.SalesAmount.Value + (input.TaxAmount ?? 0);
        if (invoice is not null && total < invoice.PaidAmount) return ServiceResult<object>.BadRequest("發票總額不可小於已收款金額。");
        var isNew = invoice is null;
        if (invoice is null)
        {
            invoice = new ProjectInvoice { ProjectId = project.Project.Id, CreatedBy = scope.UserId };
            db.ProjectInvoices.Add(invoice);
        }
        var client = await db.Clients.AsNoTracking().Where(value => value.Id == project.Project.ClientId).Select(value => new { value.InvoiceTitle, value.Name, value.TaxId }).SingleOrDefaultAsync(cancellationToken);
        invoice.OrderId = input.OrderId;
        invoice.InvoiceNo = invoiceNo;
        invoice.InvoiceDate = input.InvoiceDate.Value;
        invoice.InvoiceType = input.InvoiceType ?? invoice.InvoiceType;
        // 買受人預設帶專案客戶的發票抬頭與統編。
        invoice.BuyerName = Clean(input.BuyerName) ?? client?.InvoiceTitle ?? client?.Name;
        invoice.BuyerTaxId = Clean(input.BuyerTaxId) ?? (isNew ? client?.TaxId : null);
        invoice.SalesAmount = input.SalesAmount.Value;
        invoice.TaxAmount = input.TaxAmount ?? 0;
        invoice.TotalAmount = total;
        invoice.DueDate = input.DueDate;
        invoice.Note = Clean(input.Note);
        invoice.PaymentStatus = PaymentStatusOf(invoice.PaidAmount, invoice.TotalAmount);
        invoice.UpdatedBy = scope.UserId;
        invoice.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "invoice", invoice.Id, isNew ? "create" : "update", after: new { invoice.InvoiceNo, invoice.TotalAmount });
        await db.SaveChangesAsync(cancellationToken);
        return isNew ? ServiceResult<object>.Created(new { invoice.Id, invoice.InvoiceNo }) : ServiceResult<object>.Ok(new { invoice.Id, invoice.InvoiceNo });
    }

    /// <summary>登記累計收款金額；付清時記錄收款日。</summary>
    public async Task<ServiceResult<object>> RecordPaymentAsync(RequestScope scope, Guid invoiceId, PaymentInput input, CancellationToken cancellationToken)
    {
        var (invoice, error) = await LoadInvoiceForWriteAsync(scope, invoiceId, cancellationToken);
        if (invoice is null) return error!;
        if (invoice.Status == "void") return ServiceResult<object>.BadRequest("已作廢的發票不能登記收款。");
        if (input.PaidAmount < 0 || input.PaidAmount > invoice.TotalAmount) return ServiceResult<object>.BadRequest("收款金額必須介於 0 到發票總額。");
        var before = new { invoice.PaidAmount, invoice.PaymentStatus };
        invoice.PaidAmount = input.PaidAmount;
        invoice.PaymentStatus = PaymentStatusOf(invoice.PaidAmount, invoice.TotalAmount);
        invoice.PaidAt = invoice.PaymentStatus == "paid" ? input.PaidAt ?? BusinessTime.Today : null;
        invoice.UpdatedBy = scope.UserId;
        invoice.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "invoice", invoiceId, "payment", before, new { invoice.PaidAmount, invoice.PaymentStatus, invoice.PaidAt });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { invoice.Id, invoice.PaymentStatus });
    }

    public async Task<ServiceResult<object>> VoidInvoiceAsync(RequestScope scope, Guid invoiceId, VoidInput input, CancellationToken cancellationToken)
    {
        var (invoice, error) = await LoadInvoiceForWriteAsync(scope, invoiceId, cancellationToken);
        if (invoice is null) return error!;
        if (invoice.Status == "void") return ServiceResult<object>.BadRequest("發票已作廢。");
        if (string.IsNullOrWhiteSpace(input.Reason)) return ServiceResult<object>.BadRequest("請填寫作廢原因。");
        if (invoice.PaidAmount > 0) return ServiceResult<object>.BadRequest("已有收款的發票請先把收款金額改為 0 再作廢。");
        invoice.Status = "void";
        invoice.VoidReason = input.Reason.Trim();
        invoice.VoidedAt = DateTimeOffset.UtcNow;
        invoice.UpdatedBy = scope.UserId;
        invoice.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "invoice", invoiceId, "void", after: new { invoice.InvoiceNo, input.Reason });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { invoice.Id, invoice.Status });
    }

    /// <summary>只有未收款、未作廢的發票可刪除（輸入錯誤時讓號碼可重新登錄）；快照與附件刪除紀錄留在 audit_logs。</summary>
    public async Task<ServiceResult<Empty>> DeleteInvoiceAsync(RequestScope scope, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await db.ProjectInvoices.SingleOrDefaultAsync(value => value.Id == invoiceId, cancellationToken);
        if (invoice is null) return ServiceResult<Empty>.NotFound("找不到發票。");
        var project = await access.GetAsync(scope, invoice.ProjectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<Empty>.NotFound("找不到發票。");
        if (!project.CanWriteChild(scope, "invoices", "delete")) return ServiceResult<Empty>.Forbidden("沒有刪除發票的權限。");
        if (invoice.Status == "void" || invoice.PaidAmount > 0) return ServiceResult<Empty>.BadRequest("已作廢或已有收款的發票不能刪除。");
        await attachments.RemoveForOwnerAsync(scope, "invoice", invoiceId, cancellationToken);
        db.ProjectInvoices.Remove(invoice);
        audit.Add(scope.CompanyId, scope.UserId, "invoice", invoiceId, "delete", new { invoice.InvoiceNo, invoice.InvoiceDate, invoice.TotalAmount, invoice.ProjectId });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    private async Task<(ProjectInvoice? Invoice, ServiceResult<object>? Error)> LoadInvoiceForWriteAsync(RequestScope scope, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await db.ProjectInvoices.SingleOrDefaultAsync(value => value.Id == invoiceId, cancellationToken);
        if (invoice is null) return (null, ServiceResult<object>.NotFound("找不到發票。"));
        var project = await access.GetAsync(scope, invoice.ProjectId, track: false, cancellationToken);
        if (project is null) return (null, ServiceResult<object>.NotFound("找不到發票。"));
        if (!project.CanWriteChild(scope, "invoices")) return (null, ServiceResult<object>.Forbidden("您對此專案的發票只有檢視權限。"));
        return (invoice, null);
    }

    /// <summary>
    /// 檢查出貨單、發票、保固單對應的訂單：新增時必填；必須屬於同專案且未取消（沿用原本已對應的已取消訂單不擋）。
    /// 規則上線前沒有對應訂單的舊資料，編輯時可以維持空白。回傳錯誤訊息或 null。
    /// </summary>
    internal async Task<string?> CheckOrderAsync(Guid projectId, Guid? orderId, Guid? currentOrderId, bool isNew, CancellationToken cancellationToken)
    {
        if (orderId is not Guid id) return isNew || currentOrderId is not null ? "請選擇對應的訂單。" : null;
        var status = await db.ProjectOrders.Where(value => value.Id == id && value.ProjectId == projectId).Select(value => value.Status).SingleOrDefaultAsync(cancellationToken);
        if (status is null) return "訂單不屬於此專案。";
        return status == "cancelled" && id != currentOrderId ? "不能選擇已取消的訂單。" : null;
    }

    private static string PaymentStatusOf(decimal paid, decimal total) => paid <= 0 ? "unpaid" : paid >= total ? "paid" : "partial";

    internal async Task<Dictionary<Guid, int>> AttachmentCountsAsync(string ownerType, IEnumerable<Guid> ownerIds, CancellationToken cancellationToken)
    {
        // List：避免 C# 14 把陣列 Contains 綁到 ReadOnlySpan 而無法轉成 SQL。
        var ids = ownerIds.ToList();
        return await db.Attachments.AsNoTracking()
            .Where(value => value.OwnerType == ownerType && ids.Contains(value.OwnerId) && value.File.Status == FileStatuses.Ready)
            .GroupBy(value => value.OwnerId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.Key, value => value.Count, cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> QuoteLabelsAsync(IEnumerable<Guid> quoteIds, CancellationToken cancellationToken)
    {
        var ids = quoteIds.Distinct().ToList();
        var rows = await db.Quotes.AsNoTracking().Where(value => ids.Contains(value.Id)).Select(value => new { value.Id, value.QuoteNo, value.RevisionNo }).ToListAsync(cancellationToken);
        return rows.ToDictionary(value => value.Id, value => value.RevisionNo > 0 ? $"{value.QuoteNo}-R{value.RevisionNo}" : value.QuoteNo);
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
