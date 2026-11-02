namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 報價單。送審後內容鎖定，只能改版（同 QuoteNo、RevisionNo + 1）；保留客戶資料快照，客戶主檔之後修改不影響已發出的報價。
/// </summary>
public sealed class Quote : ICompanyOwned, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ClientId { get; set; }
    public string QuoteNo { get; set; } = string.Empty;
    public int RevisionNo { get; set; }
    public Guid? RevisionOfId { get; set; }
    public bool IsLatest { get; set; } = true;
    public string Status { get; set; } = QuoteStatuses.Draft;
    public string? Title { get; set; }
    public DateOnly QuoteDate { get; set; }
    public DateOnly? ValidUntil { get; set; }

    // 客戶快照
    public string ClientName { get; set; } = string.Empty;
    public string? ClientTaxId { get; set; }
    public string? ClientAddress { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    // 金額（一律由 QuoteCalculator 計算）
    public string Currency { get; set; } = "TWD";
    public decimal ExchangeRate { get; set; } = 1;
    public decimal Subtotal { get; set; }
    public string DiscountType { get; set; } = QuoteDiscountTypes.None;
    public decimal DiscountValue { get; set; }
    public decimal DiscountAmount { get; set; }
    public string TaxType { get; set; } = QuoteTaxTypes.Taxable;
    public bool TaxIncluded { get; set; }
    public decimal TaxRate { get; set; } = 5m;
    /// <summary>未稅金額（折扣後）；含稅報價時由總額反推。</summary>
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal? CostTotal { get; set; }
    public decimal? GrossMargin { get; set; }

    public string? PaymentTerms { get; set; }
    public string? DeliveryTerms { get; set; }
    public string? Terms { get; set; }
    public string? Note { get; set; }

    public string Source { get; set; } = QuoteSources.Manual;
    public Guid? ImportJobId { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    /// <summary>退回或客戶拒絕的原因。</summary>
    public string? StatusReason { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<QuoteItem> Items { get; set; } = [];
}

/// <summary>報價明細；LineType 為 section／note 時只有名稱、不計金額。</summary>
public sealed class QuoteItem : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid QuoteId { get; set; }
    public int SortOrder { get; set; }
    public string LineType { get; set; } = QuoteLineTypes.Item;
    public Guid? ProductId { get; set; }
    public string? ItemCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>單項折扣百分比，0～100。</summary>
    public decimal DiscountRate { get; set; }
    public decimal Amount { get; set; }
    public decimal? UnitCost { get; set; }
    public string? Remark { get; set; }
}

/// <summary>品項價目表；報價明細自動完成用。</summary>
public sealed class Product : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public string? Category { get; set; }
    public decimal DefaultPrice { get; set; }
    public decimal? DefaultCost { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Excel 範本／舊報價／圖像識別共用的兩階段匯入工作。</summary>
public sealed class ImportJob : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = ImportKinds.ExcelTemplate;
    /// <summary>第四階段檔案服務上線後保存原始檔；目前只保存檔名。</summary>
    public Guid? FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Guid? MappingProfileId { get; set; }
    public Guid? TargetProjectId { get; set; }
    public string Status { get; set; } = "preview";
    public string? PreviewJson { get; set; }
    public string? ErrorsJson { get; set; }
    public Guid? CreatedQuoteId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>舊報價 Excel 的格式對照；欄位以欄字母（B、E）、表頭以儲存格（C3）指定。</summary>
public sealed class ImportMappingProfile : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SheetName { get; set; }
    public int HeaderRow { get; set; } = 1;
    public int DataStartRow { get; set; } = 2;
    public string ColumnMapJson { get; set; } = "{}";
    public string CellMapJson { get; set; } = "{}";
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class QuoteStatuses
{
    public const string Draft = "draft";
    public const string PendingApproval = "pending_approval";
    public const string Approved = "approved";
    public const string Sent = "sent";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Superseded = "superseded";
    public static readonly string[] All = [Draft, PendingApproval, Approved, Sent, Accepted, Rejected, Superseded];
    /// <summary>可以改版的狀態：送審後到客戶回覆前，以及客戶拒絕後重新報價。</summary>
    public static readonly string[] Revisable = [PendingApproval, Approved, Sent, Rejected];
}

public static class QuoteLineTypes
{
    public const string Item = "item";
    public const string Section = "section";
    public const string Note = "note";
    public static readonly string[] All = [Item, Section, Note];
}

public static class QuoteDiscountTypes
{
    public const string None = "none";
    public const string Percent = "percent";
    public const string Amount = "amount";
    public static readonly string[] All = [None, Percent, Amount];
}

public static class QuoteTaxTypes
{
    public const string Taxable = "taxable";
    public const string Zero = "zero";
    public const string Exempt = "exempt";
    public static readonly string[] All = [Taxable, Zero, Exempt];
}

public static class QuoteSources
{
    public const string Manual = "manual";
    public const string ExcelTemplate = "excel_template";
    public const string LegacyExcel = "legacy_excel";
    public const string Ocr = "ocr";
    public const string Copy = "copy";
}

public static class ImportKinds
{
    public const string ExcelTemplate = "excel_template";
    public const string LegacyExcel = "legacy_excel";
    public const string Ocr = "ocr";
}
