namespace QuotePlatform.Api.Infrastructure;

/// <summary>API 的安全與啟動組態，對應環境變數 <c>Quote__*</c>。</summary>
public sealed class QuoteOptions
{
    /// <summary>JWT 簽章金鑰，至少 32 字元；正式環境必須由環境變數注入。</summary>
    public string JwtKey { get; init; } = string.Empty;
    public string JwtIssuer { get; init; } = "quote-platform";
    public string JwtAudience { get; init; } = "quote-admin";
    /// <summary>Access token 有效分鐘數。</summary>
    public int AccessTokenMinutes { get; init; } = 30;
    /// <summary>Refresh token 有效天數；每次使用都會輪替。</summary>
    public int RefreshTokenDays { get; init; } = 14;
    /// <summary>Refresh token cookie 是否只走 HTTPS；本機 http 開發時設為 false。</summary>
    public bool RefreshCookieSecure { get; init; } = true;
    /// <summary>登入是否要求圖形驗證碼；只有本機自動化測試可關閉。</summary>
    public bool LoginCaptchaEnabled { get; init; } = true;
    /// <summary>啟動時套用 migration。</summary>
    public bool MigrateOnStart { get; init; }
    /// <summary>啟動時套用 migration 並執行 Seeder（權限目錄、系統角色、初始帳號）。</summary>
    public bool BootstrapOnStart { get; init; }
    /// <summary>Seeder 建立 <c>admin</c> 系統帳號時的初始密碼；只在帳號不存在時使用，不會覆寫既有密碼。</summary>
    public string BootstrapAdminPassword { get; init; } = string.Empty;
    /// <summary>是否建立示範公司與帳號；僅限開發環境。</summary>
    public bool SeedDemoData { get; init; }
    /// <summary>虛擬辦公室巡檢 AuthVersion 的間隔秒數（最少 5）；smoke 測試時調短以加快驗證。</summary>
    public int OfficeAuthSweepSeconds { get; init; } = 60;
    /// <summary>跟進到期通知的掃描間隔秒數（最少 5）；同一個到期日只會通知一次，所以掃描得再頻繁也不會重複。</summary>
    public int FollowUpNotificationSweepSeconds { get; init; } = 600;
    /// <summary>每天從台灣時間幾點開始產生跟進到期通知（0–23）；smoke 測試設為 0 以免受測試時間影響。</summary>
    public int FollowUpNotificationStartHour { get; init; } = 8;
    /// <summary>簽發 file-server 上傳／下載 token 的金鑰，必須與 file-server 的 FILE_TOKEN_KEY 相同（至少 32 字元）。</summary>
    public string FileTokenKey { get; init; } = string.Empty;
    /// <summary>file-server 回呼 API 時帶的共享密鑰，對應 FILE_CALLBACK_SECRET。</summary>
    public string FileCallbackSecret { get; init; } = string.Empty;
    /// <summary>瀏覽器存取 file-server 的路徑前綴（nginx／Vite 轉發）。</summary>
    public string FilePublicBasePath { get; init; } = "/files";
    /// <summary>API 自己向 file-server 取檔（例如報價 PDF 的公司 Logo）用的內部位址；docker-compose 為 <c>http://file-server:5190</c>。</summary>
    public string FileServerInternalUrl { get; init; } = "http://127.0.0.1:5190";
    /// <summary>Claude API 金鑰（圖像識別匯入用）；未設定時圖像識別關閉，其他功能不受影響。</summary>
    public string AnthropicApiKey { get; init; } = string.Empty;
    /// <summary>Claude API 位址；空白為官方端點。僅供公司代理或本機測試用的模擬端點。</summary>
    public string AnthropicBaseUrl { get; init; } = string.Empty;
    /// <summary>Google Gemini API 金鑰（圖像識別匯入用）。</summary>
    public string GeminiApiKey { get; init; } = string.Empty;
    /// <summary>Gemini API 位址；空白為官方端點。僅供公司代理或本機測試用的模擬端點。</summary>
    public string GeminiBaseUrl { get; init; } = string.Empty;
    /// <summary>AI 設定檔（供應商、模型、強度）的路徑；相對路徑以內容根目錄為準，預設 <c>ai-settings.json</c>。見 <see cref="AiOptions"/>。</summary>
    public string AiSettingsPath { get; init; } = string.Empty;
}
