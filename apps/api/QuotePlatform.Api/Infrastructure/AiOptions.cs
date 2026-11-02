using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// AI 設定（<c>ai-settings.json</c> 的 <c>Ai</c> 區段）：使用哪一家、哪個模型、多少強度。
/// 金鑰屬於機密，不放在這裡，仍由環境變數 <c>Quote__AnthropicApiKey</c>／<c>Quote__GeminiApiKey</c> 提供。
/// 環境變數 <c>Ai__Ocr__...</c> 可覆寫設定檔（例如臨時切換供應商）。
/// </summary>
public sealed class AiOptions
{
    public OcrAiOptions Ocr { get; init; } = new();
}

public sealed class OcrAiOptions
{
    /// <summary><c>claude</c>、<c>gemini</c>，或空字串（自動選有金鑰的一家，兩家都有時用 Claude）。</summary>
    public string Provider { get; init; } = string.Empty;
    public ClaudeAiOptions Claude { get; init; } = new();
    public GeminiAiOptions Gemini { get; init; } = new();
}

public sealed class ClaudeAiOptions
{
    public string Model { get; init; } = "claude-opus-5-5";
    /// <summary>強度（effort）：medium、high、xhigh、max（下限 medium）。</summary>
    public string Effort { get; init; } = "medium";
}

public sealed class GeminiAiOptions
{
    public string Model { get; init; } = "gemini-3.8-flash";
    /// <summary>強度（thinking level）：medium、high（下限 medium）。</summary>
    public string ThinkingLevel { get; init; } = "medium";
}

/// <summary>
/// 提供目前有效的 AI 設定。設定檔修改後自動重新載入（不需重啟）；新內容不正確時記錄錯誤並沿用上一份正確的設定，
/// 避免改壞設定檔就讓圖像識別整個停擺。啟動時設定不正確則直接失敗。
/// </summary>
public sealed class AiSettings : IDisposable
{
    // 強度下限為 medium：辨識報價單需要逐列核對數字，太低的強度容易漏列或抄錯，因此不開放 low／minimal。
    // 也不開放 Gemini 的「模型預設」，因為有些模型預設是 minimal。
    public static readonly string[] ClaudeEfforts = ["medium", "high", "xhigh", "max"];
    public static readonly string[] GeminiThinkingLevels = ["medium", "high"];
    private static readonly Regex ModelName = new("^[a-z0-9][a-z0-9.\\-]{1,99}$", RegexOptions.Compiled);

    private readonly IDisposable? subscription;
    private readonly ILogger<AiSettings> logger;
    private volatile AiOptions current;

    public AiSettings(IOptionsMonitor<AiOptions> monitor, ILogger<AiSettings> logger)
    {
        this.logger = logger;
        var initial = monitor.CurrentValue;
        var error = Validate(initial);
        if (error is not null) throw new InvalidOperationException($"AI 設定不正確（ai-settings.json）：{error}");
        current = initial;
        subscription = monitor.OnChange(Reload);
    }

    public AiOptions Current => current;

    private void Reload(AiOptions next)
    {
        var error = Validate(next);
        if (error is not null)
        {
            logger.LogError("AI 設定檔內容不正確，沿用上一份設定：{Error}", error);
            return;
        }
        current = next;
        logger.LogInformation(
            "AI 設定已重新載入：供應商 {Provider}，Claude {ClaudeModel}／{Effort}，Gemini {GeminiModel}／{ThinkingLevel}",
            string.IsNullOrEmpty(next.Ocr.Provider) ? "自動" : next.Ocr.Provider, next.Ocr.Claude.Model, next.Ocr.Claude.Effort, next.Ocr.Gemini.Model, next.Ocr.Gemini.ThinkingLevel);
    }

    /// <summary>檢查設定值；正確時回傳 null，否則回傳繁中說明。值的大小寫不拘。</summary>
    public static string? Validate(AiOptions options)
    {
        var ocr = options.Ocr;
        if (Normalize(ocr.Provider) is not ("" or "claude" or "gemini")) return $"Ai:Ocr:Provider 只能是 claude、gemini 或空字串，目前為「{ocr.Provider}」。";
        if (!ModelName.IsMatch(Normalize(ocr.Claude.Model))) return $"Ai:Ocr:Claude:Model 不是有效的模型名稱：「{ocr.Claude.Model}」。";
        if (!ClaudeEfforts.Contains(Normalize(ocr.Claude.Effort))) return $"Ai:Ocr:Claude:Effort 只能是 {string.Join("、", ClaudeEfforts)}（最低 medium），目前為「{ocr.Claude.Effort}」。";
        if (!ModelName.IsMatch(Normalize(ocr.Gemini.Model))) return $"Ai:Ocr:Gemini:Model 不是有效的模型名稱：「{ocr.Gemini.Model}」。";
        if (!GeminiThinkingLevels.Contains(Normalize(ocr.Gemini.ThinkingLevel))) return $"Ai:Ocr:Gemini:ThinkingLevel 只能是 {string.Join("、", GeminiThinkingLevels)}（最低 medium），目前為「{ocr.Gemini.ThinkingLevel}」。";
        return null;
    }

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    public void Dispose() => subscription?.Dispose();

    public static void AssertExamples()
    {
        if (Validate(new AiOptions()) is not null) throw new InvalidOperationException("AI 設定：預設值應該合法");
        var invalid = new[]
        {
            new AiOptions { Ocr = new OcrAiOptions { Provider = "openai" } },
            new AiOptions { Ocr = new OcrAiOptions { Claude = new ClaudeAiOptions { Effort = "ultra" } } },
            new AiOptions { Ocr = new OcrAiOptions { Gemini = new GeminiAiOptions { ThinkingLevel = "max" } } },
            // 低於下限 medium
            new AiOptions { Ocr = new OcrAiOptions { Claude = new ClaudeAiOptions { Effort = "low" } } },
            new AiOptions { Ocr = new OcrAiOptions { Gemini = new GeminiAiOptions { ThinkingLevel = "low" } } },
            new AiOptions { Ocr = new OcrAiOptions { Gemini = new GeminiAiOptions { ThinkingLevel = "minimal" } } },
            new AiOptions { Ocr = new OcrAiOptions { Gemini = new GeminiAiOptions { ThinkingLevel = "" } } },
            new AiOptions { Ocr = new OcrAiOptions { Gemini = new GeminiAiOptions { Model = "../models" } } },
        };
        if (invalid.Any(value => Validate(value) is null)) throw new InvalidOperationException("AI 設定：不合法的值沒有被擋下");
        if (Validate(new AiOptions { Ocr = new OcrAiOptions { Provider = "Gemini", Claude = new ClaudeAiOptions { Effort = "XHigh" } } }) is not null)
        {
            throw new InvalidOperationException("AI 設定：大小寫應該不拘");
        }
    }
}
