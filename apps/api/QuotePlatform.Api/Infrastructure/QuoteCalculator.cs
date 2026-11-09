namespace QuotePlatform.Api.Infrastructure;

/// <summary>報價計算結果。</summary>
public sealed record QuoteTotals(decimal Subtotal, decimal DiscountAmount, decimal NetAmount, decimal TaxAmount, decimal Total, decimal? CostTotal, decimal? GrossMargin);

/// <summary>
/// 報價金額的唯一計算來源；前端編輯器以相同公式即時顯示，但存檔時一律以這裡的結果為準。
/// </summary>
/// <remarks>
/// - 新台幣算到元，其他幣別算到小數兩位，四捨五入採 AwayFromZero（0.5 進位，符合一般報價習慣）。
/// - 明細金額 = 數量 × 單價 × (1 − 單項折扣%)；section／note 列不計金額。
/// - 外加稅：稅額 = 未稅 × 稅率；內含稅：未稅 = 總額 ÷ (1 + 稅率)，稅額 = 總額 − 未稅（確保相加剛好等於總額）。
/// - 零稅率與免稅的稅額為 0。
/// - 有任一明細填了成本才計算成本與毛利；毛利以未稅金額計。
/// </remarks>
public static class QuoteCalculator
{
    public static int Digits(string currency) => currency == "TWD" ? 0 : 2;

    public static decimal Round(decimal value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);

    public static decimal LineAmount(QuoteItem item, int digits) =>
        item.LineType == QuoteLineTypes.Item ? Round(item.Quantity * item.UnitPrice * (1 - item.DiscountRate / 100m), digits) : 0;

    public static QuoteTotals Calculate(IReadOnlyCollection<QuoteItem> items, string currency, string discountType, decimal discountValue, string taxType, bool taxIncluded, decimal taxRate)
    {
        var digits = Digits(currency);
        var priced = items.Where(value => value.LineType == QuoteLineTypes.Item).ToList();
        var subtotal = priced.Sum(value => LineAmount(value, digits));
        var (discount, net, tax, total) = Summarize(subtotal, digits, discountType, discountValue, taxType, taxIncluded, taxRate);
        decimal? cost = priced.Any(value => value.UnitCost is not null) ? Round(priced.Sum(value => value.Quantity * (value.UnitCost ?? 0)), digits) : null;
        return new QuoteTotals(subtotal, discount, net, tax, total, cost, cost is null ? null : net - cost);
    }

    /// <summary>由明細小計算出整單折扣、未稅、稅額與總計。</summary>
    /// <param name="subtotal">明細金額合計（已四捨五入到幣別位數）。</param>
    /// <param name="digits">幣別小數位數。</param>
    /// <param name="discountType">整單折扣方式。</param>
    /// <param name="discountValue">折扣百分比或折讓金額。</param>
    /// <param name="taxType">稅別。</param>
    /// <param name="taxIncluded">單價是否含稅。</param>
    /// <param name="taxRate">稅率百分比。</param>
    /// <returns>折扣金額、未稅金額、稅額與總計。</returns>
    public static (decimal Discount, decimal Net, decimal Tax, decimal Total) Summarize(decimal subtotal, int digits, string discountType, decimal discountValue, string taxType, bool taxIncluded, decimal taxRate)
    {
        var discount = discountType switch
        {
            QuoteDiscountTypes.Percent => Round(subtotal * discountValue / 100m, digits),
            QuoteDiscountTypes.Amount => Math.Min(Round(discountValue, digits), subtotal),
            _ => 0m,
        };
        var afterDiscount = subtotal - discount;

        if (taxType != QuoteTaxTypes.Taxable || taxRate == 0) return (discount, afterDiscount, 0m, afterDiscount);
        if (taxIncluded)
        {
            var included = Round(afterDiscount / (1 + taxRate / 100m), digits);
            return (discount, included, afterDiscount - included, afterDiscount);
        }
        var tax = Round(afterDiscount * taxRate / 100m, digits);
        return (discount, afterDiscount, tax, afterDiscount + tax);
    }

    /// <summary>重算明細金額與表頭合計，寫回 entity。</summary>
    public static void Apply(Quote quote)
    {
        var digits = Digits(quote.Currency);
        foreach (var item in quote.Items) item.Amount = LineAmount(item, digits);
        var totals = Calculate(quote.Items, quote.Currency, quote.DiscountType, quote.DiscountValue, quote.TaxType, quote.TaxIncluded, quote.TaxRate);
        quote.Subtotal = totals.Subtotal;
        quote.DiscountAmount = totals.DiscountAmount;
        quote.NetAmount = totals.NetAmount;
        quote.TaxAmount = totals.TaxAmount;
        quote.Total = totals.Total;
        quote.CostTotal = totals.CostTotal;
        quote.GrossMargin = totals.GrossMargin;
    }

    /// <summary>供 <c>--self-test</c> 驗證計算規則。</summary>
    public static void AssertExamples()
    {
        QuoteItem Line(decimal qty, decimal price, decimal rate = 0, decimal? cost = null, string type = QuoteLineTypes.Item) =>
            new() { LineType = type, Quantity = qty, UnitPrice = price, DiscountRate = rate, UnitCost = cost, Name = "x" };
        void Expect(string name, QuoteTotals actual, QuoteTotals expected)
        {
            if (actual != expected) throw new InvalidOperationException($"報價計算驗證失敗（{name}）：預期 {expected}，實際 {actual}");
        }

        // 2 × 1,500 + 999.5（四捨五入到 1,000）＝ 4,000；九折 400；外加 5% 稅 180。
        Expect("外加稅與百分比折扣",
            Calculate([Line(2, 1500, cost: 1000), Line(1, 999.5m), Line(0, 0, type: QuoteLineTypes.Section)], "TWD", QuoteDiscountTypes.Percent, 10, QuoteTaxTypes.Taxable, false, 5),
            new QuoteTotals(4000, 400, 3600, 180, 3780, 2000, 1600));

        // 內含稅：總額 1,050 → 未稅 1,000、稅 50。
        Expect("內含稅", Calculate([Line(1, 1050)], "TWD", QuoteDiscountTypes.None, 0, QuoteTaxTypes.Taxable, true, 5), new QuoteTotals(1050, 0, 1000, 50, 1050, null, null));

        // 單項八五折：3 × 100 × 0.85 ＝ 255；金額折扣不可超過小計。
        Expect("單項折扣與金額折扣上限", Calculate([Line(3, 100, 15)], "TWD", QuoteDiscountTypes.Amount, 999, QuoteTaxTypes.Zero, false, 5), new QuoteTotals(255, 255, 0, 0, 0, null, null));

        // 外幣算到小數兩位。
        Expect("外幣小數", Calculate([Line(3, 33.333m)], "USD", QuoteDiscountTypes.None, 0, QuoteTaxTypes.Exempt, false, 5), new QuoteTotals(100.00m, 0, 100.00m, 0, 100.00m, null, null));
    }
}
