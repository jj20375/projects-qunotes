namespace QuotePlatform.Api.Infrastructure;

/// <summary>檔案 metadata；本體由 file-server 存在檔案系統或 MinIO，資料庫不存檔案內容。</summary>
public sealed class FileObject : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    /// <summary>{companyId}/{yyyy}/{MM}/{fileId}；由 API 產生並簽入 token，file-server 不接受前端指定路徑。</summary>
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Sha256 { get; set; }
    /// <summary>uploading／ready／deleted。</summary>
    public string Status { get; set; } = FileStatuses.Uploading;
    public Guid UploadedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>附件：多型關聯到專案、報價、訂單、保固或發票；可見性跟著所屬專案。</summary>
public sealed class Attachment : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public Guid FileId { get; set; }
    public FileObject File { get; set; } = null!;
    public string? Category { get; set; }
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>客戶訂單（PO）；一個專案可多筆。</summary>
public sealed class ProjectOrder : ICompanyOwned, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? QuoteId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    /// <summary>received／in_progress／delivered／cancelled。</summary>
    public string Status { get; set; } = "received";
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>出貨單；掛在訂單下（一張訂單可分批出貨），記錄表頭與簽收單等附件。</summary>
public sealed class ProjectShipment : ICompanyOwned, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid OrderId { get; set; }
    public string ShipmentNo { get; set; } = string.Empty;
    public DateOnly ShipDate { get; set; }
    /// <summary>shipped（已出貨）／delivered（已簽收）／cancelled（已取消）。</summary>
    public string Status { get; set; } = "shipped";
    public string? ReceiverName { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? TrackingNo { get; set; }
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>保固單；一個專案可多筆，到期日供 Dashboard 提醒。</summary>
public sealed class ProjectWarranty : ICompanyOwned, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? OrderId { get; set; }
    public string WarrantyNo { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Terms { get; set; }
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// 發票；一個專案可多筆。發票號碼在公司內唯一，開錯以「作廢」處理；
/// 只有未收款的發票可刪除（實體刪除，快照留在 audit_logs），讓輸入錯誤的號碼可以重新使用。
/// </summary>
public sealed class ProjectInvoice : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? OrderId { get; set; }
    /// <summary>統一發票號碼，儲存格式 AB-12345678。</summary>
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    /// <summary>duplicate（二聯）／triplicate（三聯）／e_invoice（電子）。</summary>
    public string InvoiceType { get; set; } = "triplicate";
    public string? BuyerName { get; set; }
    public string? BuyerTaxId { get; set; }
    public decimal SalesAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>issued／void。</summary>
    public string Status { get; set; } = "issued";
    /// <summary>unpaid／partial／paid，由 PaidAmount 與 TotalAmount 決定。</summary>
    public string PaymentStatus { get; set; } = "unpaid";
    public decimal PaidAmount { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? PaidAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class FileStatuses
{
    public const string Uploading = "uploading";
    public const string Ready = "ready";
    public const string Deleted = "deleted";
}

/// <summary>附件所屬對象與對應的權限資源代碼。</summary>
public static class AttachmentOwners
{
    public static readonly IReadOnlyDictionary<string, string> PermissionCodes = new Dictionary<string, string>
    {
        ["project"] = "projects",
        ["quote"] = "quotes",
        ["order"] = "orders",
        // 出貨單是訂單履約的一部分，沿用訂單的權限。
        ["shipment"] = "orders",
        ["warranty"] = "warranties",
        ["invoice"] = "invoices",
    };
}

/// <summary>允許上傳的檔案類型與大小；清單必須與 file-server 的 file-type.mjs 一致。</summary>
public static class FileRules
{
    public const long MaxBytes = 20L * 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "application/pdf",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["webp"] = "image/webp",
        ["heic"] = "image/heic",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    public static string? ContentTypeOf(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.');
        return ContentTypes.GetValueOrDefault(extension);
    }
}
