namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 報價單實體。
/// 業務規則：送審核准後內容鎖定，只能改版（同 QuoteNo、RevisionNo + 1）；
/// 保留客戶資料快照，客戶主檔日後修改不影響已發出之報價。
/// </summary>
public sealed class Quote : ICompanyOwned, ISoftDelete
{
    /// <summary>報價單唯一識別碼 (Primary Key)。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司租戶 ID。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬專案 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>報價客戶 ID。</summary>
    public Guid ClientId { get; set; }

    /// <summary>報價單號（如 Q202610-0001）。</summary>
    public string QuoteNo { get; set; } = string.Empty;

    /// <summary>版次編號（初版為 0，改版依序為 1, 2, ...）。</summary>
    public int RevisionNo { get; set; }

    /// <summary>被改版之原始報價單 ID（初版為 null）。</summary>
    public Guid? RevisionOfId { get; set; }

    /// <summary>是否為該報價單號之最新有效版次。</summary>
    public bool IsLatest { get; set; } = true;

    /// <summary>業務流程狀態（參考 <see cref="QuoteStatuses"/>）。</summary>
    public string Status { get; set; } = QuoteStatuses.Draft;

    /// <summary>報價單抬頭標題。</summary>
    public string? Title { get; set; }

    /// <summary>報價日期。</summary>
    public DateOnly QuoteDate { get; set; }

    /// <summary>報價有效截止日期。</summary>
    public DateOnly? ValidUntil { get; set; }

    // 客戶快照欄位
    /// <summary>客戶全名快照。</summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>客戶統一編號快照。</summary>
    public string? ClientTaxId { get; set; }

    /// <summary>客戶聯絡地址快照。</summary>
    public string? ClientAddress { get; set; }

    /// <summary>客戶聯絡人姓名快照。</summary>
    public string? ContactName { get; set; }

    /// <summary>客戶聯絡人電話快照。</summary>
    public string? ContactPhone { get; set; }

    /// <summary>客戶聯絡人電子郵件快照。</summary>
    public string? ContactEmail { get; set; }

    // 金額欄位（一律由 QuoteCalculator 計算）
    /// <summary>計價幣別代碼（如 TWD、USD）。</summary>
    public string Currency { get; set; } = "TWD";

    /// <summary>對新台幣匯率（預設為 1）。</summary>
    public decimal ExchangeRate { get; set; } = 1;

    /// <summary>明細折後小計（尚未扣除整筆折扣前）。</summary>
    public decimal Subtotal { get; set; }

    /// <summary>整筆折扣類型（參考 <see cref="QuoteDiscountTypes"/>）。</summary>
    public string DiscountType { get; set; } = QuoteDiscountTypes.None;

    /// <summary>整筆折扣數值。</summary>
    public decimal DiscountValue { get; set; }

    /// <summary>整筆折扣金額。</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>營業稅別（參考 <see cref="QuoteTaxTypes"/>）。</summary>
    public string TaxType { get; set; } = QuoteTaxTypes.Taxable;

    /// <summary>單價是否為含稅價。</summary>
    public bool TaxIncluded { get; set; }

    /// <summary>營業稅率百分比（預設 5%）。</summary>
    public decimal TaxRate { get; set; } = 5m;

    /// <summary>未稅金額（扣除整筆折扣後）；含稅報價時由總額反推。</summary>
    public decimal NetAmount { get; set; }

    /// <summary>營業稅額。</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>報價總計金額（含稅）。</summary>
    public decimal Total { get; set; }

    /// <summary>採購總成本（無 view_cost 權限者遮蔽為 null）。</summary>
    public decimal? CostTotal { get; set; }

    /// <summary>預估毛利額（未稅金額 − 採購總成本）。</summary>
    public decimal? GrossMargin { get; set; }

    /// <summary>付款條件說明。</summary>
    public string? PaymentTerms { get; set; }

    /// <summary>交貨條件說明。</summary>
    public string? DeliveryTerms { get; set; }

    /// <summary>約定條款（一條一行、不含編號）。</summary>
    public string? Terms { get; set; }

    /// <summary>內部備註。</summary>
    public string? Note { get; set; }

    /// <summary>報價來源途徑（參考 <see cref="QuoteSources"/>）。</summary>
    public string Source { get; set; } = QuoteSources.Manual;

    /// <summary>關聯匯入工作識別碼（若由匯入建立）。</summary>
    public Guid? ImportJobId { get; set; }

    /// <summary>送審人員 User ID。</summary>
    public Guid? SubmittedBy { get; set; }

    /// <summary>送審時間。</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>核准人員 User ID。</summary>
    public Guid? ApprovedBy { get; set; }

    /// <summary>核准時間。</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>正式發送客戶時間。</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>客戶接受時間。</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>客戶拒絕時間。</summary>
    public DateTimeOffset? RejectedAt { get; set; }

    /// <summary>退回或客戶拒絕原因說明。</summary>
    public string? StatusReason { get; set; }

    /// <summary>建立人員 User ID。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後修改人員 User ID。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最後修改時間（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>是否軟刪除。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>軟刪除時間（UTC）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>報價品項明細導航集合。</summary>
    public List<QuoteItem> Items { get; set; } = [];
}

/// <summary>
/// 報價明細品項實體。
/// LineType 為 section 或 note 時僅作為群組標題或附註，不計入金額統計。
/// </summary>
public sealed class QuoteItem : ICompanyOwned
{
    /// <summary>明細項目識別碼 (Primary Key)。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司租戶 ID。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬報價單 ID。</summary>
    public Guid QuoteId { get; set; }

    /// <summary>排列順序號碼。</summary>
    public int SortOrder { get; set; }

    /// <summary>明細列類型（參考 <see cref="QuoteLineTypes"/>）。</summary>
    public string LineType { get; set; } = QuoteLineTypes.Item;

    /// <summary>關聯品項主檔 ID（若自建則為 null）。</summary>
    public Guid? ProductId { get; set; }

    /// <summary>品號料號代碼。</summary>
    public string? ItemCode { get; set; }

    /// <summary>品名或分組標題名稱。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>規格說明。</summary>
    public string? Spec { get; set; }

    /// <summary>計量單位。</summary>
    public string? Unit { get; set; }

    /// <summary>數量。</summary>
    public decimal Quantity { get; set; }

    /// <summary>銷售單價。</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>單列折扣百分比（0~100）。</summary>
    public decimal DiscountRate { get; set; }

    /// <summary>單列折後小計金額。</summary>
    public decimal Amount { get; set; }

    /// <summary>單位採購成本（檢視受權限保護）。</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>單列備註說明。</summary>
    public string? Remark { get; set; }

    /// <summary>議價調整總價時鎖定此列單價，不參與分攤；改版時一併沿用。</summary>
    public bool PriceLocked { get; set; }
}

/// <summary>
/// 品項價目主檔實體；供報價明細輸入時快速自動完成。
/// </summary>
public sealed class Product : ICompanyOwned
{
    /// <summary>品項唯一識別碼 (Primary Key)。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司租戶 ID。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>品號代碼。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>品名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>規格尺寸。</summary>
    public string? Spec { get; set; }

    /// <summary>計量單位。</summary>
    public string? Unit { get; set; }

    /// <summary>品類分類標籤。</summary>
    public string? Category { get; set; }

    /// <summary>預設銷售單價。</summary>
    public decimal DefaultPrice { get; set; }

    /// <summary>預設採購成本。</summary>
    public decimal? DefaultCost { get; set; }

    /// <summary>是否啟用中。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後修改時間（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 報價試算表範本／舊報價／AI OCR 共用之兩階段匯入工作實體。
/// </summary>
public sealed class ImportJob : ICompanyOwned
{
    /// <summary>匯入工作識別碼 (Primary Key)。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司租戶 ID。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>匯入類型（參考 <see cref="ImportKinds"/>）。</summary>
    public string Kind { get; set; } = ImportKinds.ExcelTemplate;

    /// <summary>原始上傳檔案識別碼（files.Id）。</summary>
    public Guid? FileId { get; set; }

    /// <summary>原始檔案名稱。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>套用之自訂對照範本 ID。</summary>
    public Guid? MappingProfileId { get; set; }

    /// <summary>確認後建立報價單之目標專案 ID。</summary>
    public Guid? TargetProjectId { get; set; }

    /// <summary>工作處理狀態（preview 預覽中、confirmed 已確認建立、failed 解析失敗）。</summary>
    public string Status { get; set; } = "preview";

    /// <summary>解析產出之預覽 JSON 字串。</summary>
    public string? PreviewJson { get; set; }

    /// <summary>錯誤訊息清單之 JSON 字串。</summary>
    public string? ErrorsJson { get; set; }

    /// <summary>確認建立後關聯之報價單 ID。</summary>
    public Guid? CreatedQuoteId { get; set; }

    /// <summary>上傳人員 User ID。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後修改時間（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 舊報價 Excel 欄位與儲存格自訂對照範本實體。
/// 欄位對照以欄字母指定（如 B、E），表頭對照以儲存格指定（如 C3）。
/// </summary>
public sealed class ImportMappingProfile : ICompanyOwned
{
    /// <summary>對照範本識別碼 (Primary Key)。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司租戶 ID。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>對照範本名稱。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>指定工作表名稱（null 代表讀取第一張工作表）。</summary>
    public string? SheetName { get; set; }

    /// <summary>表頭欄位所在列號（由 1 起算）。</summary>
    public int HeaderRow { get; set; } = 1;

    /// <summary>品項資料起始列號。</summary>
    public int DataStartRow { get; set; } = 2;

    /// <summary>品項明細欄位對照 JSON 字串。</summary>
    public string ColumnMapJson { get; set; } = "{}";

    /// <summary>表頭儲存格位置對照 JSON 字串。</summary>
    public string CellMapJson { get; set; } = "{}";

    /// <summary>建立人員 User ID。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後修改時間（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 報價單流程狀態常數定義。
/// </summary>
public static class QuoteStatuses
{
    /// <summary>草稿：編輯中，尚未送審。</summary>
    public const string Draft = "draft";

    /// <summary>待核准：已送出審核，等待主管核可。</summary>
    public const string PendingApproval = "pending_approval";

    /// <summary>已核准：主管已核准，內容鎖定，可發送客戶。</summary>
    public const string Approved = "approved";

    /// <summary>已送出：已正式提供給客戶報價。</summary>
    public const string Sent = "sent";

    /// <summary>客戶接受：客戶已確認接受此報價（得標定案）。</summary>
    public const string Accepted = "accepted";

    /// <summary>客戶拒絕：客戶未採納此報價單。</summary>
    public const string Rejected = "rejected";

    /// <summary>已被取代：新版次送審後，舊版本標記為已被取代，不能再被接受。</summary>
    public const string Superseded = "superseded";

    /// <summary>所有合法狀態清單。</summary>
    public static readonly string[] All = [Draft, PendingApproval, Approved, Sent, Accepted, Rejected, Superseded];

    /// <summary>允許執行改版的狀態（送審後到客戶回覆前，以及客戶拒絕後重新報價）。</summary>
    public static readonly string[] Revisable = [PendingApproval, Approved, Sent, Rejected];
}

/// <summary>
/// 報價明細列類型常數定義。
/// </summary>
public static class QuoteLineTypes
{
    /// <summary>標準品項明細列（計算單價與金額）。</summary>
    public const string Item = "item";

    /// <summary>分組小計標題列（不計單價與數量）。</summary>
    public const string Section = "section";

    /// <summary>說明與備註列（僅顯示備註內容）。</summary>
    public const string Note = "note";

    /// <summary>所有合法明細列類型陣列。</summary>
    public static readonly string[] All = [Item, Section, Note];
}

/// <summary>
/// 報價全單折扣方式常數定義。
/// </summary>
public static class QuoteDiscountTypes
{
    /// <summary>無折扣。</summary>
    public const string None = "none";

    /// <summary>比例折扣（依百分比計算折讓金額）。</summary>
    public const string Percent = "percent";

    /// <summary>定額折讓（直接扣減固定未稅金額）。</summary>
    public const string Amount = "amount";

    /// <summary>所有合法折扣方式陣列。</summary>
    public static readonly string[] All = [None, Percent, Amount];
}

/// <summary>
/// 營業稅別常數定義。
/// </summary>
public static class QuoteTaxTypes
{
    /// <summary>應稅（計算營業稅，預設 5%）。</summary>
    public const string Taxable = "taxable";

    /// <summary>零稅率（如外銷適用）。</summary>
    public const string Zero = "zero";

    /// <summary>免稅。</summary>
    public const string Exempt = "exempt";

    /// <summary>所有合法稅別陣列。</summary>
    public static readonly string[] All = [Taxable, Zero, Exempt];
}

/// <summary>
/// 報價建立來源常數定義。
/// </summary>
public static class QuoteSources
{
    /// <summary>手動在編輯器建立。</summary>
    public const string Manual = "manual";

    /// <summary>透過標準 Excel 範本匯入。</summary>
    public const string ExcelTemplate = "excel_template";

    /// <summary>透過舊報價自訂對照範本匯入。</summary>
    public const string LegacyExcel = "legacy_excel";

    /// <summary>透過 AI 圖文 OCR 識別匯入。</summary>
    public const string Ocr = "ocr";

    /// <summary>複製既有報價單建立。</summary>
    public const string Copy = "copy";
}

/// <summary>
/// 匯入來源檔案類型常數定義。
/// </summary>
public static class ImportKinds
{
    /// <summary>標準 Excel 範本試算表。</summary>
    public const string ExcelTemplate = "excel_template";

    /// <summary>歷史舊報價自訂試算表。</summary>
    public const string LegacyExcel = "legacy_excel";

    /// <summary>照片、掃描圖或 PDF 圖文辨識。</summary>
    public const string Ocr = "ocr";
}
