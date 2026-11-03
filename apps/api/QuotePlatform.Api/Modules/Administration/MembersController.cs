using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>
/// 建立公司成員帳號之輸入契約。
/// </summary>
/// <param name="Account">使用者登入帳號名稱。</param>
/// <param name="DisplayName">成員姓名。</param>
/// <param name="Email">電子郵件信箱。</param>
/// <param name="Password">初始登入密碼。</param>
/// <param name="DepartmentId">指派之所屬部門 ID。</param>
/// <param name="RoleId">指派之角色 ID。</param>
/// <param name="DataScopeOverride">個人特殊覆寫之資料範圍（own / department / department_tree / company）。</param>
public sealed record CreateMemberRequest(string? Account, string? DisplayName, string? Email, string? Password, Guid? DepartmentId, Guid? RoleId, string? DataScopeOverride);

/// <summary>
/// 更新公司成員資料之輸入契約。
/// </summary>
/// <param name="DisplayName">成員姓名。</param>
/// <param name="Email">電子郵件信箱。</param>
/// <param name="DepartmentId">所屬部門 ID。</param>
/// <param name="RoleId">指派之角色 ID。</param>
/// <param name="DataScopeOverride">個人特殊覆寫之資料範圍。</param>
/// <param name="ClearDataScopeOverride">是否清除個人資料範圍覆寫（回復由角色決定）。</param>
/// <param name="IsActive">是否啟用在該公司的成員資格。</param>
public sealed record UpdateMemberRequest(string? DisplayName, string? Email, Guid? DepartmentId, Guid? RoleId, string? DataScopeOverride, bool? ClearDataScopeOverride, bool? IsActive);

/// <summary>
/// 重設登入密碼請求契約。
/// </summary>
/// <param name="Password">新密碼明文字串。</param>
public sealed record ResetPasswordRequest(string? Password);

/// <summary>
/// 個人特殊自訂權限項目輸入契約。
/// </summary>
/// <param name="Code">權限項目代碼。</param>
/// <param name="Effect">覆寫效果（grant 加權允許、deny 個別禁止）。</param>
/// <param name="PerCreate">新增權限。</param>
/// <param name="PerRead">檢視權限。</param>
/// <param name="PerUpdate">修改權限。</param>
/// <param name="PerDelete">刪除權限。</param>
/// <param name="Options">子功能選項鍵值清單。</param>
/// <param name="Reason">特殊授權原因說明。</param>
/// <param name="ExpiresAt">授權截止過期時間（可指定臨時權限）。</param>
public sealed record UserPermissionInput(string Code, string Effect, bool PerCreate, bool PerRead, bool PerUpdate, bool PerDelete, string[]? Options, string? Reason, DateTimeOffset? ExpiresAt);

/// <summary>
/// 整批取代成員個人特殊自訂權限之請求契約。
/// </summary>
/// <param name="Items">個人特殊權限覆寫條目清單。</param>
public sealed record ReplaceUserPermissionsRequest(UserPermissionInput[]? Items);

/// <summary>
/// 公司成員帳號與個人自訂權限管理控制器（公司租戶範圍）。
/// 路由參數統一採用 Membership ID，嚴禁信任前端傳入之 CompanyId。
/// </summary>
/// <param name="service">公司成員領域核心業務服務實體。</param>
[Route("api/admin/users")]
public sealed class MembersController(MemberService service) : ScopedControllerBase
{
    /// <summary>
    /// 分頁查詢當前公司成員列表（支援關鍵字、部門與狀態篩選）。
    /// </summary>
    /// <param name="query">分頁與關鍵字條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁成員清單與總筆數。</returns>
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("company.users", "read", out var scope) ? Ok(await service.ListAsync(scope, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 建立新成員帳號並加入當前公司。
    /// </summary>
    /// <param name="request">成員輸入資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新建立之成員結果物件。</returns>
    [HttpPost]
    public async Task<ActionResult> Create(CreateMemberRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.users", "create", out var scope) ? ToResult(await service.CreateAsync(scope, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新成員之職稱姓名、部門、角色或資料範圍覆寫。
    /// </summary>
    /// <param name="membershipId">公司成員資格識別碼。</param>
    /// <param name="request">更新資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPatch("{membershipId:guid}")]
    public async Task<ActionResult> Update(Guid membershipId, UpdateMemberRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.users", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, membershipId, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 重設成員登入密碼，並自動遞增 AuthVersion 強制撤銷現存 JWT Session。
    /// </summary>
    /// <param name="membershipId">公司成員資格識別碼。</param>
    /// <param name="request">新密碼請求物件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回重設結果。</returns>
    [HttpPost("{membershipId:guid}/reset-password")]
    public async Task<ActionResult> ResetPassword(Guid membershipId, ResetPasswordRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.users", "update", out var scope, "reset_password") ? ToResult(await service.ResetPasswordAsync(scope, membershipId, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 取得指定成員之完整權限配置分析（含角色賦予、個人加權/禁止與最終生效結果）。
    /// </summary>
    /// <param name="membershipId">公司成員資格識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回成員權限分析結果物件。</returns>
    [HttpGet("{membershipId:guid}/permissions")]
    public async Task<ActionResult> GetPermissions(Guid membershipId, CancellationToken cancellationToken) =>
        TryCompany("company.users", "read", out _) ? ToResult(await service.GetPermissionsAsync(membershipId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 整批取代成員之個人特殊自訂權限清單，並強制使舊 Access Token 失效。
    /// </summary>
    /// <param name="membershipId">公司成員資格識別碼。</param>
    /// <param name="request">個人自訂權限清單。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回設定結果。</returns>
    [HttpPut("{membershipId:guid}/permissions")]
    public async Task<ActionResult> ReplacePermissions(Guid membershipId, ReplaceUserPermissionsRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.users", "update", out var scope, "assign_permission") ? ToResult(await service.ReplacePermissionsAsync(scope, membershipId, request, cancellationToken)) : Forbidden();
}
