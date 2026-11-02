namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>報價表頭與明細輸入；客戶快照欄位為 null 時由客戶主檔帶入。</summary>
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
    string? Remark);

public sealed record QuoteReasonRequest(string? Reason);

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
    /// <summary><c>quoteNo</c>：依單號、版次（新到舊）排序，專案明細用來把同一張報價的各版本排在一起；預設依報價日。</summary>
    string? Sort = null);
