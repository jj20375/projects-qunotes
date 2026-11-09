using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>
/// 「調整至目標總價」試算請求。
/// </summary>
/// <param name="Quote">編輯中（可尚未儲存）的報價；只用到幣別、整單折扣、稅別與明細。</param>
/// <param name="Target">目標金額。</param>
/// <param name="Basis">目標金額口徑（total 含稅總計、net 未稅金額）。</param>
/// <param name="Mode">調整方式（discount 整單折讓、allocate 分攤到未鎖定品項）。</param>
public sealed record QuoteFitRequest(QuoteInput? Quote, decimal? Target, string? Basis, string? Mode);

/// <summary>
/// 試算後的單一品項列。
/// </summary>
/// <param name="Index">此列在請求 Items 陣列中的索引。</param>
/// <param name="UnitPrice">調整後單價。</param>
/// <param name="Amount">調整後金額。</param>
/// <param name="Changed">單價是否有變動。</param>
/// <param name="BelowCost">調整後的折後單價是否低於成本（只在具 view_cost 時判斷）。</param>
public sealed record QuoteFitLine(int Index, decimal UnitPrice, decimal Amount, bool Changed, bool BelowCost);

/// <summary>
/// 「調整至目標總價」試算結果。
/// </summary>
/// <param name="Exact">是否剛好湊到目標；false 時只提供最接近的可達金額，不提供調整內容。</param>
/// <param name="NearestBelow">湊不到時，低於目標且最接近的可達金額。</param>
/// <param name="NearestAbove">湊不到時，高於目標且最接近的可達金額。</param>
/// <param name="DiscountType">調整後的整單折扣方式。</param>
/// <param name="DiscountValue">調整後的折扣數值。</param>
/// <param name="Items">每一列品項調整後的單價與金額（只含 item 列）。</param>
/// <param name="Totals">調整後的合計；湊不到時為 null。</param>
public sealed record QuoteFitResult(bool Exact, decimal? NearestBelow, decimal? NearestAbove, string DiscountType, decimal DiscountValue, QuoteFitLine[] Items, QuoteTotals? Totals);

/// <summary>
/// 議價時把報價調整到指定的目標金額（含稅總計或未稅金額），只試算不寫入。
/// </summary>
/// <remarks>
/// - 稅額要四捨五入，外加稅時大約每 21 個總額就有 1 個怎麼調都湊不到；這時回傳上下最接近的可達金額，讓使用者改目標。
/// - 整單折讓：明細不動，原本的整單折扣改成剛好的折讓金額。
/// - 分攤：未鎖定的品項依目前金額等比例調整單價（取到幣別位數）。尾差先找一列能剛好吸收的品項，
///   放不下時才放進整單折讓；表頭是百分比折扣時不改折扣方式，改用小數兩位的單價吸收。
/// - 套用後由使用者存檔，金額仍以 <see cref="QuoteCalculator"/> 重算的結果為準。
/// </remarks>
public static class QuoteTargetFitter
{
    /// <summary>目標為含稅總計。</summary>
    public const string BasisTotal = "total";

    /// <summary>目標為未稅金額。</summary>
    public const string BasisNet = "net";

    /// <summary>以整單折讓調整，品項不動。</summary>
    public const string ModeDiscount = "discount";

    /// <summary>分攤到未鎖定的品項單價。</summary>
    public const string ModeAllocate = "allocate";

    private const decimal MaxAmount = 1_000_000_000_000m;

    /// <summary>
    /// 試算調整內容。
    /// </summary>
    /// <param name="request">目前報價內容與目標條件。</param>
    /// <param name="canSeeCost">是否具 view_cost；沒有時不判斷低於成本。</param>
    /// <returns>試算結果，或條件不合理時的錯誤訊息。</returns>
    public static ServiceResult<QuoteFitResult> Fit(QuoteFitRequest request, bool canSeeCost)
    {
        var input = request.Quote;
        if (input?.Items is not { Length: > 0 } rawItems) return Bad("報價明細是空的。");
        if (rawItems.Length > 500) return Bad("明細最多 500 列。");
        var basis = request.Basis ?? BasisTotal;
        var mode = request.Mode ?? ModeDiscount;
        if (basis is not (BasisTotal or BasisNet)) return Bad("目標口徑不正確。");
        if (mode is not (ModeDiscount or ModeAllocate)) return Bad("調整方式不正確。");
        var digits = QuoteCalculator.Digits(input.Currency?.ToUpperInvariant() ?? "TWD");
        var unit = digits == 0 ? 1m : 0.01m;
        if (request.Target is not decimal rawTarget || rawTarget <= 0 || rawTarget > MaxAmount) return Bad("請輸入大於 0 的目標金額。");
        var target = QuoteCalculator.Round(rawTarget, digits);

        var discountType = input.DiscountType ?? QuoteDiscountTypes.None;
        var taxType = input.TaxType ?? QuoteTaxTypes.Taxable;
        if (!QuoteDiscountTypes.All.Contains(discountType) || !QuoteTaxTypes.All.Contains(taxType)) return Bad("折扣或稅別不正確。");
        var discountValue = discountType == QuoteDiscountTypes.None ? 0 : input.DiscountValue ?? 0;
        var taxIncluded = input.TaxIncluded ?? false;
        var taxRate = input.TaxRate ?? 5;
        if (taxRate is < 0 or > 100 || discountValue < 0 || (discountType == QuoteDiscountTypes.Percent && discountValue > 100)) return Bad("折扣或稅率不正確。");

        var items = rawItems.Select(value => new QuoteItem
        {
            LineType = value.LineType ?? QuoteLineTypes.Item,
            Quantity = value.Quantity ?? 0,
            UnitPrice = value.UnitPrice ?? 0,
            DiscountRate = value.DiscountRate ?? 0,
            UnitCost = canSeeCost ? value.UnitCost : null,
            PriceLocked = value.PriceLocked ?? false,
        }).ToList();
        if (items.Any(value => value.Quantity < 0 || value.UnitPrice < 0 || value.DiscountRate is < 0 or > 100)) return Bad("明細的數量、單價或折扣不正確。");
        var original = items.Select(value => value.UnitPrice).ToArray();
        decimal Line(QuoteItem item) => QuoteCalculator.LineAmount(item, digits);
        var subtotal = items.Sum(Line);

        decimal Metric(decimal lineSubtotal, string type, decimal value)
        {
            var (_, net, _, total) = QuoteCalculator.Summarize(lineSubtotal, digits, type, value, taxType, taxIncluded, taxRate);
            return basis == BasisTotal ? total : net;
        }

        ServiceResult<QuoteFitResult> Done(string type, decimal value)
        {
            var totals = QuoteCalculator.Calculate(items, input.Currency?.ToUpperInvariant() ?? "TWD", type, value, taxType, taxIncluded, taxRate);
            if ((basis == BasisTotal ? totals.Total : totals.NetAmount) != target) return Bad("試算結果與目標不一致，請手動微調。");
            var lines = items
                .Select((item, index) => (item, index))
                .Where(value => value.item.LineType == QuoteLineTypes.Item)
                .Select(value => new QuoteFitLine(
                    value.index,
                    value.item.UnitPrice,
                    Line(value.item),
                    value.item.UnitPrice != original[value.index],
                    value.item.UnitCost is decimal cost && value.item.UnitPrice * (1 - value.item.DiscountRate / 100m) < cost))
                .ToArray();
            return ServiceResult<QuoteFitResult>.Ok(new QuoteFitResult(true, null, null, type, value, lines, totals));
        }

        ServiceResult<QuoteFitResult> NotExact((decimal? Value, decimal? Below, decimal? Above) solved) =>
            ServiceResult<QuoteFitResult>.Ok(new QuoteFitResult(false, solved.Below, solved.Above, discountType, discountValue, [], null));

        if (mode == ModeDiscount)
        {
            if (Metric(subtotal, QuoteDiscountTypes.None, 0) < target) return Bad("目標高於目前未折扣的金額；整單折讓只能往下調，要調高請改用「分攤到品項」。");
            var solved = Solve(after => Metric(after, QuoteDiscountTypes.None, 0), target, 0, subtotal, unit);
            if (solved.Value is not decimal afterDiscount) return NotExact(solved);
            var discount = subtotal - afterDiscount;
            return Done(discount == 0 ? QuoteDiscountTypes.None : QuoteDiscountTypes.Amount, discount);
        }

        var adjustable = items
            .Where(value => value.LineType == QuoteLineTypes.Item && !value.PriceLocked && value.Quantity > 0 && value.UnitPrice > 0 && value.DiscountRate < 100)
            .ToList();
        var adjustableSum = adjustable.Sum(Line);
        if (adjustableSum == 0) return Bad("沒有可調整的品項：請至少保留一列未鎖定、數量與單價都大於 0 的品項。");
        var lockedSum = subtotal - adjustableSum;
        if (Metric(lockedSum, discountType, discountValue) > target) return Bad("鎖定品項的金額已超過目標，請解除部分鎖定或提高目標。");
        if (Metric(MaxAmount, discountType, discountValue) < target) return Bad("目前的整單折扣下無法達到這個目標。");
        var solvedSubtotal = Solve(value => Metric(value, discountType, discountValue), target, lockedSum, MaxAmount, unit);
        if (solvedSubtotal.Value is not decimal neededSubtotal) return NotExact(solvedSubtotal);

        var needed = neededSubtotal - lockedSum;
        var ratio = needed / adjustableSum;
        foreach (var item in adjustable) item.UnitPrice = QuoteCalculator.Round(item.UnitPrice * ratio, digits);
        var residual = Absorb(adjustable, needed - adjustable.Sum(Line), digits, digits);

        var finalType = discountType;
        var finalValue = discountValue;
        if (residual != 0 && discountType != QuoteDiscountTypes.Percent)
        {
            // 尾差放進整單折讓：先讓明細合計略高於需要的金額，再用折讓扣回，折讓才不會變成負數。
            var largest = adjustable.MaxBy(Line)!;
            while (residual > 0)
            {
                var factor = largest.Quantity * (1 - largest.DiscountRate / 100m);
                largest.UnitPrice += Math.Max(unit, Math.Ceiling(residual / factor / unit) * unit);
                residual = needed - adjustable.Sum(Line);
            }
            finalValue -= residual;
            finalType = finalValue == 0 ? QuoteDiscountTypes.None : QuoteDiscountTypes.Amount;
            residual = 0;
        }
        if (residual != 0) residual = Absorb(adjustable, residual, digits, digits + 2);
        if (residual != 0) return Bad("無法剛好湊到目標金額，請調整鎖定的品項或手動微調。");
        return Done(finalType, finalValue);
    }

    /// <summary>
    /// 找一列調整單價後金額剛好變動 residual 的品項（金額大的優先）；找不到就原樣回傳 residual。
    /// </summary>
    private static decimal Absorb(List<QuoteItem> adjustable, decimal residual, int digits, int priceDigits)
    {
        if (residual == 0) return 0;
        var step = 1m / (decimal)Math.Pow(10, priceDigits);
        foreach (var item in adjustable.OrderByDescending(value => QuoteCalculator.LineAmount(value, digits)))
        {
            var factor = item.Quantity * (1 - item.DiscountRate / 100m);
            if (factor <= 0) continue;
            var price = item.UnitPrice;
            var amount = QuoteCalculator.LineAmount(item, digits);
            var delta = QuoteCalculator.Round(residual / factor, priceDigits);
            foreach (var candidate in new[] { delta, delta - step, delta + step })
            {
                if (price + candidate < 0) continue;
                item.UnitPrice = price + candidate;
                if (QuoteCalculator.LineAmount(item, digits) - amount == residual) return 0;
            }
            item.UnitPrice = price;
        }
        return residual;
    }

    /// <summary>
    /// metric 對輸入單調不減；以二分搜尋找出 metric(x) 剛好等於 target 的最小 x（以 unit 為間距）。
    /// 找不到時回傳上下最接近的可達金額。
    /// </summary>
    private static (decimal? Value, decimal? Below, decimal? Above) Solve(Func<decimal, decimal> metric, decimal target, decimal min, decimal max, decimal unit)
    {
        long low = (long)(min / unit), high = (long)(max / unit);
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (metric(middle * unit) >= target) high = middle;
            else low = middle + 1;
        }
        var reached = metric(low * unit);
        if (reached == target) return (low * unit, null, null);
        return (null, low * unit > min ? metric((low - 1) * unit) : null, reached);
    }

    private static ServiceResult<QuoteFitResult> Bad(string message) => ServiceResult<QuoteFitResult>.BadRequest(message);

    /// <summary>供 <c>--self-test</c> 驗證調整規則。</summary>
    public static void AssertExamples()
    {
        QuoteItemInput Line(decimal quantity, decimal price, bool locked = false) => new(null, null, null, "x", null, null, quantity, price, 0, null, null, locked);
        QuoteFitResult Run(QuoteItemInput[] items, decimal target, string mode, string basis = BasisTotal)
        {
            var quote = new QuoteInput(null, null, null, null, null, null, null, null, null, null, "TWD", QuoteDiscountTypes.None, 0, QuoteTaxTypes.Taxable, false, 5, null, null, null, null, items);
            var result = Fit(new QuoteFitRequest(quote, target, basis, mode), canSeeCost: true);
            return result.Value ?? throw new InvalidOperationException($"目標總價驗證失敗：{result.Message}");
        }
        void Expect(string name, bool condition)
        {
            if (!condition) throw new InvalidOperationException($"目標總價驗證失敗（{name}）");
        }

        // 未稅報價 1,520,000，客戶要含稅 1,520,000：未稅 1,447,619、稅 72,381，折讓 72,381。
        var discount = Run([Line(1, 1_520_000)], 1_520_000, ModeDiscount);
        Expect("整單折讓", discount is { Exact: true, DiscountType: QuoteDiscountTypes.Amount, DiscountValue: 72_381, Totals: { NetAmount: 1_447_619, TaxAmount: 72_381, Total: 1_520_000 } });

        // 外加 5%：未稅 9 → 總計 9，未稅 10 → 稅 0.5 進位為 1、總計 11，所以 10 湊不到。
        var unreachable = Run([Line(1, 100)], 10, ModeDiscount);
        Expect("湊不到的總額", unreachable is { Exact: false, NearestBelow: 9, NearestAbove: 11, Totals: null });

        // 鎖定的品項不動，其餘品項分攤，尾差落在數量 1 的品項。
        var allocated = Run([Line(1, 500_000, locked: true), Line(2, 300_000), Line(1, 420_000)], 1_520_000, ModeAllocate);
        Expect("分攤到未鎖定品項", allocated is { Exact: true, DiscountType: QuoteDiscountTypes.None, Totals: { NetAmount: 1_447_619, Total: 1_520_000 } }
            && allocated.Items[0] is { UnitPrice: 500_000, Changed: false }
            && allocated.Items.Skip(1).All(value => value.Changed));

        // 只有數量 3 的品項時湊不到整數單價，尾差改放進整單折讓：334 × 3 = 1,002，折讓 2。
        var absorbed = Run([Line(3, 1000)], 1000, ModeAllocate, BasisNet);
        Expect("尾差放進折讓", absorbed is { Exact: true, DiscountType: QuoteDiscountTypes.Amount, DiscountValue: 2, Totals: { Subtotal: 1002, NetAmount: 1000 } });
    }
}
