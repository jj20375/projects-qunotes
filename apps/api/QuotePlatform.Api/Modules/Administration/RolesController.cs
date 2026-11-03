using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>
/// 建立角色請求契約。
/// </summary>
/// <param name="Code">角色唯一英文代碼。</param>
/// <param name="Name">角色顯示名稱。</param>
/// <param name="DataScope">預設資料可見範圍（own / department / department_tree / company）。</param>
/// <param name="Permissions">初始功能權限配置清單。</param>
public sealed record CreateRoleRequest(string? Code, string? Name, string? DataScope, PermissionAllowanceInput[]? Permissions);

/// <summary>
/// 更新角色基本資料與資料範圍請求契約。
/// </summary>
/// <param name="Name">角色名稱。</param>
/// <param name="DataScope">資料可見範圍。</param>
public sealed record UpdateRoleRequest(string? Name, string? DataScope);

/// <summary>
/// 整批取代角色權限配置請求契約。
/// </summary>
/// <param name="Permissions">權限授權配置項目清單。</param>
public sealed record ReplaceRolePermissionsRequest(PermissionAllowanceInput[]? Permissions);

/// <summary>
/// 公司內部角色管理控制器（公司租戶範圍）。
/// 系統預設角色為唯讀範本，僅本公司自訂角色可進行修改與刪除。
/// </summary>
/// <param name="service">角色領域核心業務服務實體。</param>
[Route("api/admin/roles")]
public sealed class RolesController(RoleService service) : ScopedControllerBase
{
    /// <summary>
    /// 取得當前公司可見之角色清單（含系統角色與自訂角色）。
    /// </summary>
    /// <param name="forAssignment">是否供表單指派使用（為 true 時自訂角色僅列出自己建立的）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回角色列表陣列。</returns>
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] bool? forAssignment, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "read", out var scope) || TryCompany("company.users", "read", out scope, "assign_role")
            ? Ok(await service.ListCompanyRolesAsync(scope, forAssignment ?? false, cancellationToken))
            : Forbidden();

    /// <summary>
    /// 取得指定角色的功能權限配置清單。
    /// </summary>
    /// <param name="roleId">角色唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回角色的權限配置清單。</returns>
    [HttpGet("{roleId:guid}/permissions")]
    public async Task<ActionResult> GetPermissions(Guid roleId, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "read", out var scope) ? ToResult(await service.GetPermissionsAsync(scope, roleId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 建立自訂公司角色並設定初始功能權限與資料範圍。
    /// </summary>
    /// <param name="request">角色輸入資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新建立之角色 ID 結果。</returns>
    [HttpPost]
    public async Task<ActionResult> Create(CreateRoleRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "create", out var scope) ? ToResult(await service.CreateCompanyRoleAsync(scope, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新自訂角色之名稱或預設資料範圍。
    /// </summary>
    /// <param name="roleId">角色唯一識別碼。</param>
    /// <param name="request">更新資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPatch("{roleId:guid}")]
    public async Task<ActionResult> Update(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "update", out var scope) ? ToResult(await service.UpdateRoleAsync(scope, roleId, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 整批取代自訂角色的功能權限配置項目。
    /// </summary>
    /// <param name="roleId">角色唯一識別碼。</param>
    /// <param name="request">權限項目清單。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回設定結果。</returns>
    [HttpPut("{roleId:guid}/permissions")]
    public async Task<ActionResult> ReplacePermissions(Guid roleId, ReplaceRolePermissionsRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "update", out var scope) ? ToResult(await service.ReplacePermissionsAsync(scope, roleId, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除自訂角色（若已有成員被指派為該角色則拒絕刪除）。
    /// </summary>
    /// <param name="roleId">角色唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("{roleId:guid}")]
    public async Task<ActionResult> Delete(Guid roleId, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "delete", out var scope) ? ToResult(await service.DeleteCompanyRoleAsync(scope, roleId, cancellationToken)) : Forbidden();
}

/// <summary>
/// 平台系統預設角色管理控制器（平台管理員專屬 platform scope）。
/// 負責調整跨租戶系統角色的基礎權限基線。
/// </summary>
/// <param name="service">角色領域核心業務服務實體。</param>
[Route("api/platform/roles")]
public sealed class SystemRolesController(RoleService service) : ScopedControllerBase
{
    /// <summary>
    /// 取得平台系統角色清單（平台專用）。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回系統角色列表。</returns>
    [HttpGet]
    public async Task<ActionResult> List(CancellationToken cancellationToken) =>
        TryPlatform("platform.roles", "read", out _) ? Ok(await service.ListSystemRolesAsync(cancellationToken)) : Forbidden();

    /// <summary>
    /// 取得指定系統角色的功能權限配置。
    /// </summary>
    /// <param name="roleId">系統角色唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回權限配置項目清單。</returns>
    [HttpGet("{roleId:guid}/permissions")]
    public async Task<ActionResult> GetPermissions(Guid roleId, CancellationToken cancellationToken) =>
        TryPlatform("platform.roles", "read", out var scope) ? ToResult(await service.GetPermissionsAsync(scope, roleId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新指定系統角色的名稱或資料範圍基線。
    /// </summary>
    /// <param name="roleId">系統角色唯一識別碼。</param>
    /// <param name="request">更新資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPatch("{roleId:guid}")]
    public async Task<ActionResult> Update(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.roles", "update", out var scope) ? ToResult(await service.UpdateRoleAsync(scope, roleId, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 整批更新指定系統角色的功能權限配置基線。
    /// </summary>
    /// <param name="roleId">系統角色唯一識別碼。</param>
    /// <param name="request">權限項目清單。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回設定結果。</returns>
    [HttpPut("{roleId:guid}/permissions")]
    public async Task<ActionResult> ReplacePermissions(Guid roleId, ReplaceRolePermissionsRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.roles", "update", out var scope) ? ToResult(await service.ReplacePermissionsAsync(scope, roleId, request, cancellationToken)) : Forbidden();
}

/// <summary>
/// 權限目錄樹結構控制器（供角色與個人權限矩陣編輯畫面共用）。
/// </summary>
/// <param name="db">資料庫上下文實體。</param>
[Route("api/admin/permission-catalog")]
public sealed class PermissionCatalogController(QuoteDbContext db) : ScopedControllerBase
{
    /// <summary>
    /// 取得適用於指定範圍之全系統可用權限樹狀目錄結構。
    /// </summary>
    /// <param name="scope">目標範圍（company 或 platform）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回權限目錄樹節點陣列。</returns>
    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] string? scope, CancellationToken cancellationToken)
    {
        if (!RequestScope.TryRead(User, out var current)) return Unauthorized();
        var target = scope ?? (current.IsPlatform ? "platform" : "company");
        if (target is not ("platform" or "company") || (target == "platform" && !current.IsPlatform)) return Forbidden();
        return Ok(await PermissionInputs.CatalogTreeAsync(db, target, cancellationToken));
    }
}
