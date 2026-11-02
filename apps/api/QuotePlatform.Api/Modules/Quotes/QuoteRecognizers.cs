using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>辨識供應商共用的錯誤訊息，兩家給使用者看的文字一致。</summary>
internal static class OcrMessages
{
    public const string Refused = "辨識服務拒絕處理這份文件，請改用 Excel 匯入或手動輸入。";
    public const string TooLong = "報價單內容太長，無法一次辨識；請分頁拍照後分次匯入。";
    public const string Unparsable = "辨識結果無法解析，請重試。";
    public const string InvalidKey = "圖像識別服務的金鑰無效，請管理員檢查設定。";
    public const string Busy = "圖像識別服務忙碌中，請稍後再試。";
    public const string BadFile = "這個檔案無法辨識，請確認圖片清晰、格式為 JPG／PNG／WebP／PDF。";
    public const string Unavailable = "圖像識別服務暫時無法使用，請稍後再試。";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static (OcrOutput? Output, string? Error) Parse(string text)
    {
        var output = JsonSerializer.Deserialize<OcrOutput>(text, Json);
        return output is null ? (null, Unparsable) : (output, null);
    }
}

/// <summary>
/// Claude（Anthropic 官方 C# SDK，beta Messages API）。金鑰：<c>Quote__AnthropicApiKey</c>；模型與強度（effort）：<c>ai-settings.json</c> 的 <c>Ai:Ocr:Claude</c>。
/// structured output 以 <c>output_config.format</c> 指定 JSON schema；模型支援時加上 <c>fallbacks: "default"</c>，被安全機制拒絕時由伺服器改用其他模型重試。
/// </summary>
public sealed class ClaudeQuoteRecognizer(IOptions<QuoteOptions> options, AiSettings settings, ILogger<ClaudeQuoteRecognizer> logger) : IQuoteRecognizer
{
    /// <summary>支援 server-side refusal fallback（<c>fallbacks: "default"</c>）的模型。</summary>
    private static readonly HashSet<string> FallbackModels = ["claude-opus-5-5", "claude-opus-5", "claude-fable-5-1", "claude-sonnet-5-5"];

    private AnthropicClient? client;

    public string Provider => "claude";
    public bool Configured => !string.IsNullOrWhiteSpace(options.Value.AnthropicApiKey);

    public (string Model, string Strength) CurrentSettings
    {
        get
        {
            var claude = settings.Current.Ocr.Claude;
            return (claude.Model.Trim(), AiSettings.Normalize(claude.Effort));
        }
    }

    // 設定檔已限制最低 medium（見 AiSettings），這裡不會收到 low。
    private static Effort ToEffort(string value) => value switch
    {
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };

    public async Task<(OcrOutput? Output, string? Error)> RecognizeAsync(byte[] content, string mediaType, CancellationToken cancellationToken)
    {
        var (model, effort) = CurrentSettings;
        var data = Convert.ToBase64String(content);
        BetaContentBlockParam document = mediaType == "application/pdf"
            ? new BetaRequestDocumentBlock { Source = new BetaBase64PdfSource { Data = data } }
            : new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = data, MediaType = mediaType } };

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 16000,
            System = OcrPrompt.System,
            OutputConfig = new BetaOutputConfig
            {
                Effort = ToEffort(effort),
                Format = new BetaJsonOutputFormat { Schema = OcrSchema.Claude },
            },
            Messages =
            [
                new BetaUserMessageParam
                {
                    Content = new List<BetaContentBlockParam> { document, new BetaTextBlockParam { Text = OcrPrompt.User } },
                },
            ],
        };
        if (FallbackModels.Contains(model))
        {
            parameters = parameters with { Betas = ["server-side-fallback-2026-07-01"], Fallbacks = new Default() };
        }

        try
        {
            var response = await Client.Beta.Messages.Create(parameters, cancellationToken);
            if (response.StopReason == "refusal") return (null, OcrMessages.Refused);
            if (response.StopReason == "max_tokens") return (null, OcrMessages.TooLong);
            return OcrMessages.Parse(string.Concat(response.Content.Select(block => block.TryPickText(out var value) ? value.Text : string.Empty)));
        }
        catch (AnthropicUnauthorizedException)
        {
            logger.LogError("Anthropic API 金鑰無效");
            return (null, OcrMessages.InvalidKey);
        }
        catch (AnthropicRateLimitException)
        {
            return (null, OcrMessages.Busy);
        }
        catch (AnthropicBadRequestException exception)
        {
            logger.LogWarning(exception, "Claude 拒絕圖像識別請求");
            return (null, OcrMessages.BadFile);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Claude 辨識結果不是預期的 JSON");
            return (null, OcrMessages.Unparsable);
        }
        catch (Exception exception) when (exception is AnthropicException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Claude 圖像識別呼叫失敗");
            return (null, OcrMessages.Unavailable);
        }
    }

    private AnthropicClient Client => client ??= new AnthropicClient
    {
        ApiKey = options.Value.AnthropicApiKey,
        BaseUrl = string.IsNullOrWhiteSpace(options.Value.AnthropicBaseUrl) ? "https://api.anthropic.com" : options.Value.AnthropicBaseUrl,
        Timeout = TimeSpan.FromSeconds(150),
        MaxRetries = 1,
    };
}

/// <summary>
/// Gemini（Google Gemini API，REST <c>models/{model}:generateContent</c>）。金鑰：<c>Quote__GeminiApiKey</c>；模型與強度（thinking level）：<c>ai-settings.json</c> 的 <c>Ai:Ocr:Gemini</c>。
/// 圖片與 PDF 以 <c>inlineData</c>（base64）送出；structured output 以 <c>generationConfig.responseMimeType = application/json</c> 加 <c>responseSchema</c>（OpenAPI 子集）指定。
/// 金鑰放在 <c>x-goog-api-key</c> header，不放網址，避免出現在記錄檔。
/// </summary>
public sealed class GeminiQuoteRecognizer(IOptions<QuoteOptions> options, AiSettings settings, IHttpClientFactory httpClients, ILogger<GeminiQuoteRecognizer> logger) : IQuoteRecognizer
{
    public const string HttpClientName = "gemini";

    public string Provider => "gemini";
    public bool Configured => !string.IsNullOrWhiteSpace(options.Value.GeminiApiKey);

    public (string Model, string Strength) CurrentSettings
    {
        get
        {
            var gemini = settings.Current.Ocr.Gemini;
            return (gemini.Model.Trim(), AiSettings.Normalize(gemini.ThinkingLevel));
        }
    }

    public async Task<(OcrOutput? Output, string? Error)> RecognizeAsync(byte[] content, string mediaType, CancellationToken cancellationToken)
    {
        var (model, thinkingLevel) = CurrentSettings;
        var body = BuildRequest(content, mediaType, thinkingLevel).ToJsonString();
        var baseUrl = string.IsNullOrWhiteSpace(options.Value.GeminiBaseUrl) ? "https://generativelanguage.googleapis.com" : options.Value.GeminiBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        var http = httpClients.CreateClient(HttpClientName);

        // 與 Claude 端相同：連線錯誤、逾時與 5xx 重試一次。
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                request.Headers.Add("x-goog-api-key", options.Value.GeminiApiKey);
                using var response = await http.SendAsync(request, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                if ((int)response.StatusCode >= 500 && attempt == 1) continue;
                if (!response.IsSuccessStatusCode) return (null, MapError(response.StatusCode, text));
                var (output, error) = ParseResponse(text);
                if (error is not null) logger.LogWarning("Gemini 圖像識別未完成：{Error}", error);
                return (output, error);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Gemini 辨識結果不是預期的 JSON");
                return (null, OcrMessages.Unparsable);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt == 1) continue;
                logger.LogError(exception, "Gemini 圖像識別呼叫失敗");
                return (null, OcrMessages.Unavailable);
            }
        }
    }

    private string MapError(HttpStatusCode status, string body)
    {
        // 金鑰錯誤時 generateContent 可能回 400（API_KEY_INVALID），也可能回 401／403。
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || status == HttpStatusCode.BadRequest && body.Contains("API_KEY_INVALID", StringComparison.Ordinal))
        {
            logger.LogError("Gemini API 金鑰無效或沒有權限（HTTP {Status}）", (int)status);
            return OcrMessages.InvalidKey;
        }
        if (status == HttpStatusCode.TooManyRequests) return OcrMessages.Busy;
        if ((int)status >= 500) return OcrMessages.Unavailable;
        logger.LogWarning("Gemini 拒絕圖像識別請求（HTTP {Status}）：{Body}", (int)status, body.Length > 500 ? body[..500] : body);
        return OcrMessages.BadFile;
    }

    /// <param name="thinkingLevel">medium、high（設定檔限制最低 medium）；空字串表示不送 thinkingConfig。</param>
    public static JsonObject BuildRequest(byte[] content, string mediaType, string thinkingLevel)
    {
        var request = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = OcrPrompt.System }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = mediaType, ["data"] = Convert.ToBase64String(content) } },
                    new JsonObject { ["text"] = OcrPrompt.User }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = JsonNode.Parse(OcrSchema.Gemini.GetRawText()),
                ["maxOutputTokens"] = 32768,
            },
        };
        // API discovery 文件中 ThinkingConfig.thinkingLevel 的列舉值為大寫（MINIMAL／LOW／MEDIUM／HIGH）。
        if (thinkingLevel.Length > 0) request["generationConfig"]!["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = thinkingLevel.ToUpperInvariant() };
        return request;
    }

    /// <summary>
    /// 解析 generateContent 回應：prompt 被擋（promptFeedback.blockReason）或沒有候選結果視為拒絕；
    /// finishReason 為 MAX_TOKENS 視為太長，STOP 以外的其他原因（SAFETY、RECITATION…）視為拒絕。思考內容（thought）不計入結果。
    /// </summary>
    public static (OcrOutput? Output, string? Error) ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var blocked) && blocked.GetString() is { Length: > 0 } reason && reason != "BLOCK_REASON_UNSPECIFIED")
        {
            return (null, OcrMessages.Refused);
        }
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0) return (null, OcrMessages.Refused);
        var candidate = candidates[0];
        var finish = candidate.TryGetProperty("finishReason", out var value) ? value.GetString() : null;
        if (finish == "MAX_TOKENS") return (null, OcrMessages.TooLong);
        if (finish is not (null or "STOP" or "FINISH_REASON_UNSPECIFIED")) return (null, OcrMessages.Refused);

        var text = new StringBuilder();
        if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                if (part.TryGetProperty("text", out var piece)) text.Append(piece.GetString());
            }
        }
        return text.Length == 0 ? (null, OcrMessages.Unparsable) : OcrMessages.Parse(text.ToString());
    }

    public static void AssertExamples()
    {
        var request = BuildRequest([0x89, 0x50], "image/png", "high");
        if (request["contents"]![0]!["parts"]![0]!["inlineData"]!["mimeType"]!.GetValue<string>() != "image/png"
            || request["generationConfig"]!["responseSchema"]!["type"]!.GetValue<string>() != "OBJECT"
            || request["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>() != "HIGH"
            || BuildRequest([0x89], "image/png", string.Empty)["generationConfig"]!["thinkingConfig"] is not null)
        {
            throw new InvalidOperationException("圖像識別：Gemini 請求格式不正確");
        }
        const string ok = """{"candidates":[{"content":{"parts":[{"text":"思考中","thought":true},{"text":"{\"isQuote\":true,\"header\":{},\"items\":[],\"totals\":{},\"notes\":[]}"}]},"finishReason":"STOP"}]}""";
        if (ParseResponse(ok) is not ({ IsQuote: true }, null)) throw new InvalidOperationException("圖像識別：Gemini 回應解析失敗");
        if (ParseResponse("""{"candidates":[{"finishReason":"SAFETY"}]}""").Error != OcrMessages.Refused
            || ParseResponse("""{"promptFeedback":{"blockReason":"PROHIBITED_CONTENT"}}""").Error != OcrMessages.Refused
            || ParseResponse("""{"candidates":[{"content":{"parts":[{"text":"{"}]},"finishReason":"MAX_TOKENS"}]}""").Error != OcrMessages.TooLong)
        {
            throw new InvalidOperationException("圖像識別：Gemini 拒絕或截斷判斷不正確");
        }
    }
}
