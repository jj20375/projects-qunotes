using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

public sealed record CreateRoleRequest(string? Code, string? Name, string? DataScope, PermissionAllowanceInput[]? Permissions);
public sealed record UpdateRoleRequest(string? Name, string? DataScope);
public sealed record ReplaceRolePermissionsRequest(PermissionAllowanceInput[]? Permissions);

/// <summary>公司角色（company scope）：系統角色可檢視，只有本公司自訂角色可修改。</summary>
[Route("api/admin/roles")]
public sealed class RolesController(RoleService service) : ScopedControllerBase
{
    /// <summary>帳號管理指派角色時需要角色清單，因此 company.users 的 assign_role 也可讀取。</summary>
    [HttpGet]
    public async Task<ActionResult> List(CancellationToken cancellationToken) =>
        TryCompany("company.roles", "read", out var scope) || TryCompany("company.users", "read", out scope, "assign_role")
            ? Ok(await service.ListCompanyRolesAsync(scope, cancellationToken))
            : Forbidden();

    [HttpGet("{roleId:guid}/permissions")]
    public async Task<ActionResult> GetPermissions(Guid roleId, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "read", out var scope) ? ToResult(await service.GetPermissionsAsync(scope, roleId, cancellationToken)) : Forbidden();

    [HttpPost]
    public async Task<ActionResult> Create(CreateRoleRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "create", out var scope) ? ToResult(await service.CreateCompanyRoleAsync(scope, request, cancellationToken)) : Forbidden();

    [HttpPatch("{roleId:guid}")]
    public async Task<ActionResult> Update(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "update", out var scope) ? ToResult(await service.UpdateRoleAsync(scope, roleId, request, cancellationToken)) : Forbidden();

    [HttpPut("{roleId:guid}/permissions")]
    public async Task<ActionResult> ReplacePermissions(Guid roleId, ReplaceRolePermissionsRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "update", out var scope) ? ToResult(await service.ReplacePermissionsAsync(scope, roleId, request, cancellationToken)) : Forbidden();

    [HttpDelete("{roleId:guid}")]
    public async Task<ActionResult> Delete(Guid roleId, CancellationToken cancellationToken) =>
        TryCompany("company.roles", "delete", out var scope) ? ToResult(await service.DeleteCompanyRoleAsync(scope, roleId, cancellationToken)) : Forbidden();
}

/// <summary>系統角色（platform scope）：平台管理員調整系統角色的權限基線。</summary>
[Route("api/platform/roles")]
public sealed class SystemRolesController(RoleService service) : ScopedControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List(CancellationToken cancellationToken) =>
        TryPlatform("platform.roles", "read", out _) ? Ok(await service.ListSystemRolesAsync(cancellationToken)) : Forbidden();

    [HttpGet("{roleId:guid}/permissions")]
    public async Task<ActionResult> GetPermissions(Guid roleId, CancellationToken cancellationToken) =>
        TryPlatform("platform.roles", "read", out var scope) ? ToResult(await service.GetPermissionsAsync(scope, roleId, cancellationToken)) : Forbidden();

    [HttpPatch("{roleId:guid}")]
    public async Task<ActionResult> Update(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.roles", "update", out var scope) ? ToResult(await service.UpdateRoleAsync(scope, roleId, request, cancellationToken)) : Forbidden();

    [HttpPut("{roleId:guid}/permissions")]
    public async Task<ActionResult> ReplacePermissions(Guid roleId, ReplaceRolePermissionsRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.roles", "update", out var scope) ? ToResult(await service.ReplacePermissionsAsync(scope, roleId, request, cancellationToken)) : Forbidden();
}

/// <summary>權限目錄樹；角色與個人權限編輯畫面共用。</summary>
[Route("api/admin/permission-catalog")]
public sealed class PermissionCatalogController(QuoteDbContext db) : ScopedControllerBase
{
    /// <summary>company scope 只能讀 company 目錄；platform scope 可讀兩者。</summary>
    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] string? scope, CancellationToken cancellationToken)
    {
        if (!RequestScope.TryRead(User, out var current)) return Unauthorized();
        var target = scope ?? (current.IsPlatform ? "platform" : "company");
        if (target is not ("platform" or "company") || (target == "platform" && !current.IsPlatform)) return Forbidden();
        return Ok(await PermissionInputs.CatalogTreeAsync(db, target, cancellationToken));
    }
}
