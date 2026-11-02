using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Identity;

/// <summary>
/// 登入與工作範圍 API。Access token 放在回應本文；refresh token 只放在 HttpOnly cookie，前端 JavaScript 讀不到。
/// </summary>
[Route("api/auth")]
public sealed class AuthController(IdentityService identity, LoginCaptchaService captcha, IOptions<QuoteOptions> options) : ScopedControllerBase
{
    /// <summary>Refresh token cookie；Path 限縮在 /api/auth，其他 API 請求不會帶上。</summary>
    public const string RefreshCookieName = "qp_rt";

    [AllowAnonymous]
    [HttpGet("captcha")]
    public IActionResult GetCaptcha()
    {
        if (!identity.CaptchaEnabled) return Ok(new { enabled = false });
        var (captchaId, image) = captcha.Generate();
        return Ok(new { enabled = true, captchaId, image });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        WithSession(await identity.LoginAsync(request, ClientIp, UserAgent, cancellationToken));

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await identity.RefreshAsync(Request.Cookies[RefreshCookieName], ClientIp, UserAgent, cancellationToken);
        if (result.Status == StatusCodes.Status401Unauthorized) Response.Cookies.Delete(RefreshCookieName, CookieOptions(DateTimeOffset.UnixEpoch));
        return WithSession(result);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await identity.LogoutAsync(Request.Cookies[RefreshCookieName], cancellationToken);
        Response.Cookies.Delete(RefreshCookieName, CookieOptions(DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) ? Ok(await identity.GetMeAsync(scope, cancellationToken)) : Unauthorized();

    [HttpGet("menu")]
    public async Task<IActionResult> Menu(CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) ? Ok(await identity.ListMenuAsync(scope, cancellationToken)) : Unauthorized();

    /// <summary>可切換的工作範圍；平台管理員可進入的公司很多，以 keyword 搜尋、offset／limit 分頁（由後端處理）。</summary>
    [HttpGet("scopes")]
    public async Task<IActionResult> Scopes([FromQuery] string? keyword, [FromQuery] int? limit, [FromQuery] int? offset, CancellationToken cancellationToken) =>
        RequestScope.TryRead(User, out var scope) ? Ok(await identity.ListScopesAsync(scope, keyword, limit, offset, cancellationToken)) : Unauthorized();

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
