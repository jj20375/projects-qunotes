namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>
/// 報價單表頭與明細項目輸入資料契約；客戶快照欄位為 null 時由客戶主檔自動帶入。
/// </summary>
/// <param name="Title">報價單抬頭標題。</param>
/// <param name="ClientId">客戶唯一識別碼；若為 null 則視為手動快照輸入。</param>
/// <param name="QuoteDate">報價日期。</param>
/// <param name="ValidUntil">報價有效截止日期。</param>
/// <param name="ClientName">客戶名稱快照。</param>
/// <param name="ClientTaxId">客戶統一編號快照。</param>
/// <param name="ClientAddress">客戶聯絡地址快照。</param>
/// <param name="ContactName">聯絡人姓名快照。</param>
/// <param name="ContactPhone">聯絡人電話快照。</param>
/// <param name="ContactEmail">聯絡人電子郵件快照。</param>
/// <param name="Currency">計價幣別代碼（如 TWD、USD）。</param>
/// <param name="DiscountType">全單折扣方式（none、percent、amount）。</param>
/// <param name="DiscountValue">折扣數值（比例或折讓金額）。</param>
/// <param name="TaxType">稅別（taxable 應稅、zero 零稅率、exempt 免稅）。</param>
/// <param name="TaxIncluded">單價是否已包含營業稅。</param>
/// <param name="TaxRate">營業稅率百分比（預設 5%）。</param>
/// <param name="PaymentTerms">付款條件文字說明。</param>
/// <param name="DeliveryTerms">交貨條件文字說明。</param>
/// <param name="Terms">其他約定條款（一條一行）。</param>
/// <param name="Note">內部備註說明。</param>
/// <param name="Items">報價品項明細清單。</param>
public sealed record QuoteInput(
    string? Title,
    Guid? ClientId,
    DateOnly? QuoteDate,
    DateOnly? ValidUntil,
    string? ClientName,
    string? ClientTaxId,
    string? ClientAddress,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    string? Currency,
    string? DiscountType,
    decimal? DiscountValue,
    string? TaxType,
    bool? TaxIncluded,
    decimal? TaxRate,
    string? PaymentTerms,
    string? DeliveryTerms,
    string? Terms,
    string? Note,
    QuoteItemInput[]? Items);

/// <summary>
/// 報價單品項或分組明細項目輸入契約。
/// </summary>
/// <param name="LineType">明細列類型（item 品項、section 分組小計標題、note 備註說明）。</param>
/// <param name="ProductId">品項庫關聯商品 ID（若非主檔品項則為 null）。</param>
/// <param name="ItemCode">自訂品號或料號代碼。</param>
/// <param name="Name">品名或分組標題名稱。</param>
/// <param name="Spec">規格尺寸說明。</param>
/// <param name="Unit">計量單位（如 式、個、台、米）。</param>
/// <param name="Quantity">數量。</param>
/// <param name="UnitPrice">單價。</param>
/// <param name="DiscountRate">單列折扣率（百分比，如 5 代表 5%）。</param>
/// <param name="UnitCost">單位成本（僅具檢視成本權限者可見與設定）。</param>
/// <param name="Remark">單列備註說明。</param>
/// <param name="PriceLocked">議價調整總價時是否鎖定此列單價。</param>
public sealed record QuoteItemInput(
    string? LineType,
    Guid? ProductId,
    string? ItemCode,
    string? Name,
    string? Spec,
    string? Unit,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? DiscountRate,
    decimal? UnitCost,
    string? Remark,
    bool? PriceLocked = null);

/// <summary>
/// 報價單退回或拒絕原因請求。
/// </summary>
/// <param name="Reason">退回或客戶拒絕原因說明文字。</param>
public sealed record QuoteReasonRequest(string? Reason);

/// <summary>
/// 跨專案條件搜尋報價單之查詢參數。
/// </summary>
/// <param name="Keyword">搜尋關鍵字（單號、標題或客戶名稱）。</param>
/// <param name="Status">狀態篩選陣列（draft、approved 等）。</param>
/// <param name="ProjectId">鎖定之專案 ID。</param>
/// <param name="ClientId">鎖定之客戶 ID。</param>
/// <param name="From">報價日期起日。</param>
/// <param name="To">報價日期訖日。</param>
/// <param name="AmountMin">總金額下限。</param>
/// <param name="AmountMax">總金額上限。</param>
/// <param name="LatestOnly">是否僅查詢各報價單之最新有效版次（排除歷史 superseded 版次）。</param>
/// <param name="Page">頁碼（由 1 起算）。</param>
/// <param name="PageSize">每頁筆數。</param>
/// <param name="Sort">排序欄位名稱（如 quoteNo 依單號版次、預設依報價日期）。</param>
public sealed record QuoteSearchQuery(
    string? Keyword,
    string[]? Status,
    Guid? ProjectId,
    Guid? ClientId,
    DateOnly? From,
    DateOnly? To,
    decimal? AmountMin,
    decimal? AmountMax,
    bool? LatestOnly,
    int? Page,
    int? PageSize,
    string? Sort = null);
