using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Identity;

/// <summary>
/// 身分驗證、登入、Refresh Token 輪替與工作租戶切換控制器。
/// 安全防禦機制：Access Token 回傳於回應本文中，Refresh Token 僅儲存於 HttpOnly Cookie，客戶端 JS 無法讀取。
/// </summary>
/// <param name="identity">身分驗證核心服務實體。</param>
/// <param name="captcha">圖形驗證碼服務實體。</param>
/// <param name="options">系統全域組態選項。</param>
[Route("api/auth")]
public sealed class AuthController(IdentityService identity, LoginCaptchaService captcha, IOptions<QuoteOptions> options) : ScopedControllerBase
{
    /// <summary>Refresh token cookie 名稱；Path 限縮在 /api/auth，其他業務 API 請求不攜帶。</summary>
    public const string RefreshCookieName = "qp_rt";

    /// <summary>
    /// 取得圖形驗證碼狀態與圖片資料（匿名存取）。
    /// </summary>
    /// <returns>傳回是否啟用驗證碼與圖片 Base64 資料。</returns>
    [AllowAnonymous]
    [HttpGet("captcha")]
    public IActionResult GetCaptcha()
    {
        if (!identity.CaptchaEnabled) return Ok(new { enabled = false });
        var (captchaId, image) = captcha.Generate();
        return Ok(new { enabled = true, captchaId, image });
    }

    /// <summary>
    /// 使用者帳號與密碼身分登入驗證（匿名存取）。
    /// </summary>
    /// <param name="request">登入認證請求資訊。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>登入成功傳回 JWT Access Token，並寫入 HttpOnly Refresh Token Cookie。</returns>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        WithSession(await identity.LoginAsync(request, ClientIp, UserAgent, cancellationToken));

    /// <summary>
    /// 使用 HttpOnly Cookie 攜帶之 Refresh Token 輪替並換發全新 Access Token（匿名存取）。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新 Access Token，並刷新 Cookie 之 Refresh Token。</returns>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await identity.RefreshAsync(Request.Cookies[RefreshCookieName], ClientIp, UserAgent, cancellationToken);
        if (result.Status == StatusCodes.Status401Unauthorized) Response.Cookies.Delete(RefreshCookieName, CookieOptions(DateTimeOffset.UnixEpoch));
        return WithSession(result);
    }

    /// <summary>
    /// 註銷當前工作階段與 Refresh Token（匿名存取）。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 204 NoContent 並清除 Cookie。</returns>
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await identity.LogoutAsync(Request.Cookies[RefreshCookieName], cancellationToken);
        Response.Cookies.Delete(RefreshCookieName, CookieOptions(DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    /// <summary>
    /// 取得當前已驗證使用者之個人資料、當前工作範圍與計算後之權限授權清單。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回登入者基本資訊與授權項目。</returns>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) ? Ok(await identity.GetMeAsync(scope, cancellationToken)) : Unauthorized();

    /// <summary>
    /// 取得依當前使用者角色與自訂授權篩選後之後台動態導覽選單樹。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回選單節點陣列。</returns>
    [HttpGet("menu")]
    public async Task<IActionResult> Menu(CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) ? Ok(await identity.ListMenuAsync(scope, cancellationToken)) : Unauthorized();

    /// <summary>
    /// 查詢當前登入者可切換之租戶公司清單與平台範圍選項。
    /// </summary>
    /// <param name="keyword">公司名稱關鍵字。</param>
    /// <param name="limit">每頁筆數限制。</param>
    /// <param name="offset">偏移筆數。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回可切換之範圍項目清單。</returns>
    [HttpGet("scopes")]
    public async Task<IActionResult> Scopes([FromQuery] string? keyword, [FromQuery] int? limit, [FromQuery] int? offset, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) ? Ok(await identity.ListScopesAsync(scope, keyword, limit, offset, cancellationToken)) : Unauthorized();

    /// <summary>
    /// 切換當前操作之租戶工作範圍，並重新簽發對應權限之 JWT 權杖。
    /// </summary>
    /// <param name="request">目標租戶範圍請求。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新工作範圍之 Access Token 封包。</returns>
    [HttpPost("scopes/select")]
    public async Task<ActionResult> SelectScope(SelectScopeRequest request, CancellationToken cancellationToken)
    {
        if (!RequestScope.TryRead(User, out var scope)) return Unauthorized();
        return WithSession(await identity.SelectScopeAsync(scope, request, Request.Cookies[RefreshCookieName], ClientIp, UserAgent, cancellationToken));
    }

    private ActionResult WithSession(ServiceResult<IssuedSession> result)
    {
        if (result.Value is not IssuedSession session) return StatusCode(result.Status, new { message = result.Message });
        Response.Cookies.Append(RefreshCookieName, session.RefreshToken, CookieOptions(session.RefreshExpiresAt));
        return Ok(session.Response);
    }

    private CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = options.Value.RefreshCookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
    };

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private string? UserAgent => Request.Headers.UserAgent.ToString();
}
