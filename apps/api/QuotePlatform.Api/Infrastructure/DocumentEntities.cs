namespace QuotePlatform.Api.Infrastructure;

/// <summary>檔案 metadata；本體由 file-server 存在檔案系統或 MinIO，資料庫不存檔案內容。</summary>
public sealed class FileObject : ICompanyOwned
{
    /// <summary>檔案唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>儲存路徑 Key：{companyId}/{yyyy}/{MM}/{fileId}；由 API 產生並簽入 token，file-server 不接受前端指定路徑。</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>原始上傳檔案名稱。</summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>MIME 內容型態（Content-Type）。</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>檔案大小（位元組 Bytes）。</summary>
    public long Size { get; set; }

    /// <summary>檔案內容 SHA-256 雜湊碼，供完整性校驗與重複判定。</summary>
    public string? Sha256 { get; set; }

    /// <summary>檔案狀態：uploading（上傳中）／ready（就緒）／deleted（已刪除）；參見 <see cref="FileStatuses"/>。</summary>
    public string Status { get; set; } = FileStatuses.Uploading;

    /// <summary>上傳者使用者識別碼。</summary>
    public Guid UploadedBy { get; set; }

    /// <summary>建立上傳時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>檔案上傳並確認完成時間戳記（UTC）。</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>檔案刪除時間戳記（UTC）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>附件：多型關聯到專案、報價、訂單、保固或發票；可見性跟著所屬專案。</summary>
public sealed class Attachment : ICompanyOwned
{
    /// <summary>附件唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>掛載擁有者類型（project、quote、order、shipment、warranty、invoice）；參見 <see cref="AttachmentOwners"/>。</summary>
    public string OwnerType { get; set; } = string.Empty;

    /// <summary>掛載擁有者單據實體識別碼。</summary>
    public Guid OwnerId { get; set; }

    /// <summary>關聯實體檔案識別碼。</summary>
    public Guid FileId { get; set; }

    /// <summary>關聯實體檔案中繼資料物件。</summary>
    public FileObject File { get; set; } = null!;

    /// <summary>附件分類標籤（例如簽收單、規格書、合約書）。</summary>
    public string? Category { get; set; }

    /// <summary>備註說明。</summary>
    public string? Note { get; set; }

    /// <summary>建立者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>客戶訂單（PO）；一個專案可多筆。</summary>
public sealed class ProjectOrder : ICompanyOwned, ISoftDelete
{
    /// <summary>訂單唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬專案識別碼。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>關聯來源報價單識別碼（若由特定報價單轉入）。</summary>
    public Guid? QuoteId { get; set; }

    /// <summary>訂單編號（客戶採購單號或內部訂單號）。</summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>訂單下單日期。</summary>
    public DateOnly OrderDate { get; set; }

    /// <summary>未稅訂單總額。</summary>
    public decimal Amount { get; set; }

    /// <summary>營業稅總額。</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>含稅訂單總額。</summary>
    public decimal Total { get; set; }

    /// <summary>訂單處理狀態：received（已接單）／in_progress（處理中）／delivered（已交貨）／cancelled（已取消）。</summary>
    public string Status { get; set; } = "received";

    /// <summary>備註說明。</summary>
    public string? Note { get; set; }

    /// <summary>建立者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新者使用者識別碼。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>是否已軟刪除。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>軟刪除時間戳記（UTC）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>出貨單；掛在訂單下（一張訂單可分批出貨），記錄表頭與簽收單等附件。</summary>
public sealed class ProjectShipment : ICompanyOwned, ISoftDelete
{
    /// <summary>出貨單唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬專案識別碼。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>所屬訂單識別碼。</summary>
    public Guid OrderId { get; set; }

    /// <summary>出貨單號。</summary>
    public string ShipmentNo { get; set; } = string.Empty;

    /// <summary>實際出貨日期。</summary>
    public DateOnly ShipDate { get; set; }

    /// <summary>出貨狀態：shipped（已出貨）／delivered（已簽收）／cancelled（已取消）。</summary>
    public string Status { get; set; } = "shipped";

    /// <summary>收件人姓名。</summary>
    public string? ReceiverName { get; set; }

    /// <summary>運送送達地址。</summary>
    public string? DeliveryAddress { get; set; }

    /// <summary>物流或貨運追蹤單號。</summary>
    public string? TrackingNo { get; set; }

    /// <summary>備註說明。</summary>
    public string? Note { get; set; }

    /// <summary>建立者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新者使用者識別碼。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>是否已軟刪除。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>軟刪除時間戳記（UTC）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>保固單；一個專案可多筆，到期日供 Dashboard 提醒。</summary>
public sealed class ProjectWarranty : ICompanyOwned, ISoftDelete
{
    /// <summary>保固單唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬專案識別碼。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>關聯來源訂單識別碼（選填）。</summary>
    public Guid? OrderId { get; set; }

    /// <summary>保固書／保固單號。</summary>
    public string WarrantyNo { get; set; } = string.Empty;

    /// <summary>保固標的主旨或設備名稱。</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>保固起始生效日期。</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>保固截止結束日期。</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>保固條款或責任說明。</summary>
    public string? Terms { get; set; }

    /// <summary>備註說明。</summary>
    public string? Note { get; set; }

    /// <summary>建立者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新者使用者識別碼。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>是否已軟刪除。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>軟刪除時間戳記（UTC）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// 發票；一個專案可多筆。發票號碼在公司內唯一，開錯以「作廢」處理；
/// 只有未收款的發票可刪除（實體刪除，快照留在 audit_logs），讓輸入錯誤的號碼可以重新使用。
/// </summary>
public sealed class ProjectInvoice : ICompanyOwned
{
    /// <summary>發票唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬專案識別碼。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>關聯訂單識別碼（選填）。</summary>
    public Guid? OrderId { get; set; }

    /// <summary>統一發票號碼，儲存格式 AB-12345678。</summary>
    public string InvoiceNo { get; set; } = string.Empty;

    /// <summary>發票開立日期。</summary>
    public DateOnly InvoiceDate { get; set; }

    /// <summary>發票種類：duplicate（二聯式）／triplicate（三聯式）／e_invoice（電子發票）。</summary>
    public string InvoiceType { get; set; } = "triplicate";

    /// <summary>買受人抬頭。</summary>
    public string? BuyerName { get; set; }

    /// <summary>買受人統一編號。</summary>
    public string? BuyerTaxId { get; set; }

    /// <summary>銷售額未稅金額。</summary>
    public decimal SalesAmount { get; set; }

    /// <summary>營業稅額。</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>含稅總額。</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>發票狀態：issued（已開立）／void（已作廢）。</summary>
    public string Status { get; set; } = "issued";

    /// <summary>收款狀態：unpaid（未收款）／partial（部分收款）／paid（已結清）。</summary>
    public string PaymentStatus { get; set; } = "unpaid";

    /// <summary>累計已收款金額。</summary>
    public decimal PaidAmount { get; set; }

    /// <summary>預計收款到期日。</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>款項結清日期。</summary>
    public DateOnly? PaidAt { get; set; }

    /// <summary>發票作廢時間戳記（UTC）。</summary>
    public DateTimeOffset? VoidedAt { get; set; }

    /// <summary>發票作廢原因。</summary>
    public string? VoidReason { get; set; }

    /// <summary>備註說明。</summary>
    public string? Note { get; set; }

    /// <summary>建立者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新者使用者識別碼。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>檔案狀態常數定義。</summary>
public static class FileStatuses
{
    /// <summary>檔案上傳中，尚未確認完成。</summary>
    public const string Uploading = "uploading";

    /// <summary>檔案已確認上傳成功，可供正常讀取與下載。</summary>
    public const string Ready = "ready";

    /// <summary>檔案已被標記為已刪除。</summary>
    public const string Deleted = "deleted";
}

/// <summary>附件所屬對象與對應的權限資源代碼。</summary>
public static class AttachmentOwners
{
    /// <summary>擁有者類型對應之權限資源代碼字典。</summary>
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
    /// <summary>單一檔案最大允許上限大小（位元組，目前為 20MB）。</summary>
    public const long MaxBytes = 20L * 1024 * 1024;

    /// <summary>副檔名對照之 MIME 內容類型對照字典。</summary>
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

    /// <summary>
    /// 依據檔名判斷並取得對應的標準 MIME 類型。
    /// </summary>
    /// <param name="fileName">檔案名稱或副檔名。</param>
    /// <returns>對應的 Content-Type 字串；若不在允許清單中則回傳 null。</returns>
    public static string? ContentTypeOf(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.');
        return ContentTypes.GetValueOrDefault(extension);
    }
}
