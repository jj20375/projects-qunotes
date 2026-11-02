using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Common;

/// <summary>Service 回傳結果；Controller 統一轉成 HTTP 回應，錯誤本文固定為 <c>{ message }</c>。</summary>
public sealed record ServiceResult<T>(int Status, T? Value, string? Message)
{
    public static ServiceResult<T> Ok(T value) => new(StatusCodes.Status200OK, value, null);
    public static ServiceResult<T> Created(T value) => new(StatusCodes.Status201Created, value, null);
    public static ServiceResult<T> BadRequest(string message) => new(StatusCodes.Status400BadRequest, default, message);
    public static ServiceResult<T> Forbidden(string message) => new(StatusCodes.Status403Forbidden, default, message);
    public static ServiceResult<T> NotFound(string message) => new(StatusCodes.Status404NotFound, default, message);
    public static ServiceResult<T> Conflict(string message) => new(StatusCodes.Status409Conflict, default, message);
}

/// <summary>沒有回傳本文的成功結果。</summary>
public sealed record Empty
{
    public static readonly Empty Value = new();
}

/// <summary>
/// 所有需登入 API 的基底：從 JWT 讀取 scope 並檢查資源權限。
/// </summary>
/// <remarks>company／platform 範圍一律來自已驗證 JWT，Controller 不接受前端傳入的 companyId。</remarks>
[ApiController]
[Authorize]
public abstract class ScopedControllerBase : ControllerBase
{
    /// <summary>公司範圍且具備指定 CRUD 權限（可再要求子功能）。</summary>
    protected bool TryCompany(string code, string action, out RequestScope scope, string? option = null) =>
        TryScope(code, action, option, requirePlatform: false, out scope);

    /// <summary>平台範圍且具備指定 CRUD 權限（可再要求子功能）。</summary>
    protected bool TryPlatform(string code, string action, out RequestScope scope, string? option = null) =>
        TryScope(code, action, option, requirePlatform: true, out scope);

    private bool TryScope(string code, string action, string? option, bool requirePlatform, out RequestScope scope)
    {
        if (RequestScope.TryRead(User, out scope)
            && (requirePlatform ? scope.IsPlatform : scope.IsCompany)
            && scope.Has(code, action)
            && (option is null || scope.HasOption(code, option)))
        {
            return true;
        }
        scope = null!;
        return false;
    }

    protected ObjectResult Forbidden(string message = "沒有執行此操作的權限。") =>
        StatusCode(StatusCodes.Status403Forbidden, new { message });

    protected ActionResult ToResult<T>(ServiceResult<T> result) => result.Status switch
    {
        StatusCodes.Status200OK when result.Value is Empty => NoContent(),
        StatusCodes.Status200OK => Ok(result.Value),
        StatusCodes.Status201Created => StatusCode(StatusCodes.Status201Created, result.Value),
        _ => StatusCode(result.Status, new { message = result.Message }),
    };
}
