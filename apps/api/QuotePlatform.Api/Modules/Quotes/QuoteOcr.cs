using System.Globalization;
using System.Text.Json;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>模型依 <see cref="OcrSchema"/> 回傳的辨識結果；欄位名稱與 schema 一致（camelCase）。Claude、Gemini 共用。</summary>
public sealed record OcrOutput(bool IsQuote, OcrHeader Header, List<OcrItem> Items, OcrTotals Totals, List<string> Notes);

public sealed record OcrHeader(
    string? ClientName, string? ClientTaxId, string? ClientAddress, string? ContactName, string? ContactPhone, string? ContactEmail,
    string? QuoteDate, string? ValidUntil, string? Title, string? PaymentTerms, string? DeliveryTerms, string? Terms,
    string? TaxType, string? Discount, string? SourceQuoteNo);

public sealed record OcrItem(string LineType, string? ItemCode, string Name, string? Spec, string? Unit, decimal? Quantity, decimal? UnitPrice, decimal? DiscountRate, decimal? Amount, string? Remark, bool Uncertain);

public sealed record OcrTotals(decimal? Subtotal, decimal? Tax, decimal? Total);

/// <summary>辨識供應商：把檔案交給模型抄錄，回傳辨識結果或給使用者看的繁中錯誤訊息。</summary>
public interface IQuoteRecognizer
{
    /// <summary>設定值與畫面顯示用的代碼：<c>claude</c>、<c>gemini</c>。</summary>
    string Provider { get; }
    /// <summary>是否已設定金鑰。</summary>
    bool Configured { get; }
    /// <summary>目前設定檔中的模型與強度（記錄稽核用）。</summary>
    (string Model, string Strength) CurrentSettings { get; }
    Task<(OcrOutput? Output, string? Error)> RecognizeAsync(byte[] content, string mediaType, CancellationToken cancellationToken);
}

/// <summary>
/// 圖像識別匯入：把報價單的照片、掃描圖或 PDF 交給 Claude 或 Gemini（vision），以 structured output 取得表頭與明細，
/// 再轉成與 Excel 匯入相同的 <see cref="ParsedQuote"/>，走同一條「預覽 → 修正 → 建立草稿」流程。
/// </summary>
/// <remarks>
/// - 供應商、模型、強度在 <c>ai-settings.json</c>（<see cref="AiSettings"/>），存檔即生效。供應商空白時自動選有設定金鑰的一家，
///   兩家都有時用 Claude。選定的供應商沒有金鑰時功能關閉（<see cref="Enabled"/> 為 false），前端不顯示此選項。
/// - 辨識結果一律只是預覽；金額仍由後端在建立報價時重算，模型抄錄的「金額」只用來比對、產生提醒。
/// - 檔案只在這次請求中送給供應商，不另外保存。
/// </remarks>
public sealed class QuoteOcrService(AiSettings settings, IEnumerable<IQuoteRecognizer> recognizers)
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    public const long MaxPdfBytes = 10 * 1024 * 1024;

    private readonly List<IQuoteRecognizer> all = recognizers.ToList();

    /// <summary>依目前的設定檔選出的供應商；沒有可用的供應商時為 null。每次讀取都依最新設定，設定檔改了不用重啟。</summary>
    public IQuoteRecognizer? Active
    {
        get
        {
            var provider = AiSettings.Normalize(settings.Current.Ocr.Provider);
            return provider.Length > 0 ? all.Single(value => value.Provider == provider) : all.FirstOrDefault(value => value.Configured);
        }
    }

    public bool Enabled => Active is { Configured: true };

    /// <summary>依檔頭判斷是否為可辨識的格式；回傳兩家都接受的 media type，不支援時為 null。</summary>
    public static string? DetectMediaType(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF-"u8)) return "application/pdf";
        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8)) return "image/gif";
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    public Task<(OcrOutput? Output, string? Error)> RecognizeAsync(IQuoteRecognizer recognizer, byte[] content, string mediaType, CancellationToken cancellationToken) =>
        recognizer.Configured
            ? recognizer.RecognizeAsync(content, mediaType, cancellationToken)
            : Task.FromResult<(OcrOutput?, string?)>((null, "圖像識別尚未啟用，請管理員設定 Quote__AnthropicApiKey 或 Quote__GeminiApiKey。"));

    /// <summary>辨識結果 → 匯入預覽；同時檢查「數量 × 單價」與抄錄金額、明細加總與小計是否一致。</summary>
    public static ParsedQuote ToParsedQuote(OcrOutput output)
    {
        var warnings = new List<string>();
        if (!output.IsQuote) warnings.Add("這份文件看起來不像報價單，請確認內容。");

        var h = output.Header;
        var header = new Dictionary<string, string?>
        {
            ["ClientName"] = h.ClientName, ["ClientTaxId"] = h.ClientTaxId, ["ClientAddress"] = h.ClientAddress,
            ["ContactName"] = h.ContactName, ["ContactPhone"] = h.ContactPhone, ["ContactEmail"] = h.ContactEmail,
            ["QuoteDate"] = h.QuoteDate, ["ValidUntil"] = h.ValidUntil, ["Title"] = h.Title,
            ["PaymentTerms"] = h.PaymentTerms, ["DeliveryTerms"] = h.DeliveryTerms, ["Terms"] = h.Terms,
            ["TaxType"] = h.TaxType, ["Discount"] = h.Discount, ["SourceQuoteNo"] = h.SourceQuoteNo,
        };
        foreach (var key in header.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToList()) header.Remove(key);
        QuoteExcel.NormalizeHeader(header, warnings);

        var items = new List<ParsedQuoteItem>();
        decimal sum = 0;
        foreach (var (item, index) in output.Items.Select((value, index) => (value, index + 1)))
        {
            var name = item.Name.Trim();
            if (name.Length == 0) continue;
            var lineType = item.LineType == QuoteLineTypes.Section ? QuoteLineTypes.Section : QuoteLineTypes.Item;
            if (lineType == QuoteLineTypes.Section)
            {
                items.Add(new ParsedQuoteItem(index, lineType, null, name, null, null, null, null, null, null, null, null));
                continue;
            }
            var quantity = item.Quantity;
            var unitPrice = item.UnitPrice;
            // 只有金額、沒有單價時（例如「一式」），以金額當單價。
            if (unitPrice is null && item.Amount is decimal only && (quantity ?? 1) == 1)
            {
                quantity ??= 1;
                unitPrice = only;
            }
            if (item.Uncertain) warnings.Add($"第 {index} 列「{name}」有看不清楚的地方，請對照原圖。");
            if (quantity is null || unitPrice is null) warnings.Add($"第 {index} 列「{name}」缺少數量或單價。");
            if (quantity is decimal q && unitPrice is decimal p)
            {
                var computed = QuoteCalculator.Round(q * p * (1 - (item.DiscountRate ?? 0) / 100), 2);
                sum += computed;
                if (item.Amount is decimal amount && Math.Abs(amount - computed) > 1)
                {
                    warnings.Add($"第 {index} 列「{name}」數量 × 單價 = {computed.ToString("N0", CultureInfo.InvariantCulture)}，與文件金額 {amount.ToString("N0", CultureInfo.InvariantCulture)} 不同。");
                }
            }
            items.Add(new ParsedQuoteItem(index, lineType, Clean(item.ItemCode), name, Clean(item.Spec), Clean(item.Unit), quantity, unitPrice, item.DiscountRate, null, Clean(item.Remark), item.Amount));
        }
        if (output.Totals.Subtotal is decimal subtotal && items.Count > 0 && Math.Abs(subtotal - sum) > 1)
        {
            warnings.Add($"明細加總 {sum.ToString("N0", CultureInfo.InvariantCulture)} 與文件小計 {subtotal.ToString("N0", CultureInfo.InvariantCulture)} 不同，可能有漏抄或抄錯的列。");
        }
        warnings.AddRange(output.Notes.Where(note => !string.IsNullOrWhiteSpace(note)).Select(note => $"辨識備註：{note.Trim()}"));
        return new ParsedQuote(header, items, warnings, [], string.Empty, 0);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>自我測試：辨識結果轉預覽、金額不一致提醒、只有金額的「一式」列、檔頭判斷。</summary>
    public static void AssertExamples()
    {
        var output = new OcrOutput(
            true,
            new OcrHeader("測試客戶", null, null, null, null, null, "114/10/29", null, "機房建置", "月結 30 天", null, null, "營業稅 5% 外加", "95%", "Q-001"),
            [
                new OcrItem("section", null, "硬體", null, null, null, null, null, null, null, false),
                new OcrItem("item", "SV-1", "伺服器", "2U", "台", 2, 150000, null, 300000, null, false),
                new OcrItem("item", null, "交換器", null, "台", 1, 30000, 10, 30000, null, true),
                new OcrItem("item", null, "安裝工資", null, "式", null, null, null, 12000, null, false),
            ],
            new OcrTotals(339000, null, null),
            ["右下角有手寫修改"]);
        var parsed = ToParsedQuote(output);
        if (parsed.Header.GetValueOrDefault("QuoteDate") != "2025-10-29") throw new InvalidOperationException("圖像識別：民國日期未轉換");
        if (parsed.Header.GetValueOrDefault("DiscountType") != QuoteDiscountTypes.Percent || parsed.Header.GetValueOrDefault("TaxRate") != "5") throw new InvalidOperationException("圖像識別：稅別或折扣未轉換");
        if (parsed.Items.Count != 4 || parsed.Items[0].LineType != QuoteLineTypes.Section) throw new InvalidOperationException("圖像識別：明細數量或分段不正確");
        if (parsed.Items[3] is not { Quantity: 1, UnitPrice: 12000 }) throw new InvalidOperationException("圖像識別：一式列未以金額當單價");
        var expected = new[] { "看不清楚", "與文件金額", "辨識備註" };
        if (!expected.All(text => parsed.Warnings.Any(warning => warning.Contains(text)))) throw new InvalidOperationException($"圖像識別：提醒不完整：{string.Join(" / ", parsed.Warnings)}");
        // 明細加總 300000 + 27000 + 12000 = 339000，與小計相同，不應提醒。
        if (parsed.Warnings.Any(warning => warning.Contains("明細加總"))) throw new InvalidOperationException("圖像識別：小計比對誤判");
        if (DetectMediaType("%PDF-1.7"u8) != "application/pdf" || DetectMediaType(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }) != "image/jpeg" || DetectMediaType("PK\u0003\u0004"u8) is not null)
        {
            throw new InvalidOperationException("圖像識別：檔頭判斷不正確");
        }
        OcrSchema.AssertExamples();
        GeminiQuoteRecognizer.AssertExamples();
    }
}

/// <summary>兩家供應商共用的指示文字。</summary>
public static class OcrPrompt
{
    public const string System = """
        你是報價單抄錄助手。使用者會提供一份報價單的照片、掃描圖或 PDF，請把內容逐字抄錄成指定的 JSON 結構。

        規則：
        - 只抄錄文件上實際看得到的內容，不要推測或補上沒有寫的資料；看不到的欄位填 null。
        - 文字保留原文（繁體中文就用繁體中文），不要翻譯或改寫。日期照文件寫法抄錄（例如 114/10/29 或 2025-10-29）。
        - header.taxType 抄錄稅別相關文字（例如「營業稅 5% 外加」「含稅」「免稅」）；header.discount 抄錄整單折扣（例如「95%」「折讓 2,000」）。
        - items 依文件由上到下列出明細。分類標題或分段列的 lineType 為 "section"、只填 name；小計、合計、稅額、總計列不要放進 items。
        - 數量、單價、折扣率（百分比數字，例如 10 代表 10%）、金額填數字，去掉千分位與幣別符號；看不清楚就填 null 並把 uncertain 設為 true。
        - totals 抄錄文件上印出的未稅小計、稅額、總計。
        - 有任何看不清楚、被遮住或不確定的地方，用繁體中文簡短寫在 notes。
        - 如果文件看起來不是報價單或估價單，isQuote 設為 false。
        """;

    public const string User = "請抄錄這份報價單。";
}

/// <summary>
/// 辨識結果的結構只定義一次，再轉成各家要的 schema 格式：
/// Claude 用 JSON Schema（可缺的值寫成 <c>["string", "null"]</c>、<c>additionalProperties: false</c>）；
/// Gemini 用 OpenAPI 3.0 子集（型別大寫、<c>nullable: true</c>，不支援 additionalProperties）。所有欄位都列在 required。
/// </summary>
public static class OcrSchema
{
    private sealed record Node(string Type, bool Nullable = false, string[]? Enum = null, (string Name, Node Node)[]? Properties = null, Node? Items = null);

    private static readonly Node Root = BuildRoot();

    public static readonly Dictionary<string, JsonElement> Claude = ToDictionary(RenderClaude(Root));
    public static readonly JsonElement Gemini = JsonSerializer.SerializeToElement(RenderGemini(Root));

    private static Node BuildRoot()
    {
        static Node Text(bool nullable = true) => new("string", nullable);
        static Node Number() => new("number", true);
        var header = new[] { "clientName", "clientTaxId", "clientAddress", "contactName", "contactPhone", "contactEmail", "quoteDate", "validUntil", "title", "paymentTerms", "deliveryTerms", "terms", "taxType", "discount", "sourceQuoteNo" }
            .Select(name => (name, Text())).ToArray();
        var item = new Node("object", Properties:
        [
            ("lineType", new Node("string", Enum: ["item", "section"])),
            ("itemCode", Text()),
            ("name", Text(nullable: false)),
            ("spec", Text()),
            ("unit", Text()),
            ("quantity", Number()),
            ("unitPrice", Number()),
            ("discountRate", Number()),
            ("amount", Number()),
            ("remark", Text()),
            ("uncertain", new Node("boolean")),
        ]);
        return new Node("object", Properties:
        [
            ("isQuote", new Node("boolean")),
            ("header", new Node("object", Properties: header)),
            ("items", new Node("array", Items: item)),
            ("totals", new Node("object", Properties: [("subtotal", Number()), ("tax", Number()), ("total", Number())])),
            ("notes", new Node("array", Items: Text(nullable: false))),
        ]);
    }

    private static Dictionary<string, object> RenderClaude(Node node)
    {
        var result = new Dictionary<string, object> { ["type"] = node.Nullable ? new[] { node.Type, "null" } : node.Type };
        if (node.Enum is not null) result["enum"] = node.Enum;
        if (node.Items is not null) result["items"] = RenderClaude(node.Items);
        if (node.Properties is not null)
        {
            result["additionalProperties"] = false;
            result["required"] = node.Properties.Select(property => property.Name).ToArray();
            result["properties"] = node.Properties.ToDictionary(property => property.Name, property => (object)RenderClaude(property.Node));
        }
        return result;
    }

    private static Dictionary<string, object> RenderGemini(Node node)
    {
        var result = new Dictionary<string, object> { ["type"] = node.Type.ToUpperInvariant() };
        if (node.Nullable) result["nullable"] = true;
        if (node.Enum is not null) result["enum"] = node.Enum;
        if (node.Items is not null) result["items"] = RenderGemini(node.Items);
        if (node.Properties is not null)
        {
            var names = node.Properties.Select(property => property.Name).ToArray();
            result["required"] = names;
            // 讓輸出欄位順序固定，與 schema 一致。
            result["propertyOrdering"] = names;
            result["properties"] = node.Properties.ToDictionary(property => property.Name, property => (object)RenderGemini(property.Node));
        }
        return result;
    }

    private static Dictionary<string, JsonElement> ToDictionary(object value) =>
        JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());

    public static void AssertExamples()
    {
        var claudeQuantity = Claude["properties"].GetProperty("items").GetProperty("items").GetProperty("properties").GetProperty("quantity");
        if (claudeQuantity.GetProperty("type").GetArrayLength() != 2 || Claude["additionalProperties"].GetBoolean()) throw new InvalidOperationException("圖像識別：Claude schema 不正確");
        var geminiQuantity = Gemini.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("properties").GetProperty("quantity");
        if (geminiQuantity.GetProperty("type").GetString() != "NUMBER" || !geminiQuantity.GetProperty("nullable").GetBoolean() || Gemini.TryGetProperty("additionalProperties", out _))
        {
            throw new InvalidOperationException("圖像識別：Gemini schema 不正確");
        }
        if (Gemini.GetProperty("required").GetArrayLength() != 5) throw new InvalidOperationException("圖像識別：Gemini schema 缺少 required");
    }
}

