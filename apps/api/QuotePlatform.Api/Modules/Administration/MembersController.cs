using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

public sealed record CreateMemberRequest(string? Account, string? DisplayName, string? Email, string? Password, Guid? DepartmentId, Guid? RoleId, string? DataScopeOverride);
public sealed record UpdateMemberRequest(string? DisplayName, string? Email, Guid? DepartmentId, Guid? RoleId, string? DataScopeOverride, bool? ClearDataScopeOverride, bool? IsActive);
public sealed record ResetPasswordRequest(string? Password);
public sealed record UserPermissionInput(string Code, string Effect, bool PerCreate, bool PerRead, bool PerUpdate, bool PerDelete, string[]? Options, string? Reason, DateTimeOffset? ExpiresAt);
public sealed record ReplaceUserPermissionsRequest(UserPermissionInput[]? Items);

/// <summary>
/// 目前公司的帳號與成員（company scope）；路由參數是 membership id，不接受 companyId。
/// </summary>
[Route("api/admin/users")]
public sealed class MembersController(MemberService service) : ScopedControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("company.users", "read", out var scope) ? Ok(await service.ListAsync(scope, query, cancellationToken)) : Forbidden();

    [HttpPost]
    public async Task<ActionResult> Create(CreateMemberRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.users", "create", out var scope) ? ToResult(await service.CreateAsync(scope, request, cancellationToken)) : Forbidden();

    [HttpPatch("{membershipId:guid}")]
    public async Task<ActionResult> Update(Guid membershipId, UpdateMemberRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.users", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, membershipId, request, cancellationToken)) : Forbidden();

    [HttpPost("{membershipId:guid}/reset-password")]
    public async Task<ActionResult> ResetPassword(Guid membershipId, ResetPasswordRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.users", "update", out var scope, "reset_password") ? ToResult(await service.ResetPasswordAsync(scope, membershipId, request, cancellationToken)) : Forbidden();

    [HttpGet("{membershipId:guid}/permissions")]
    public async Task<ActionResult> GetPermissions(Guid membershipId, CancellationToken cancellationToken) =>
        TryCompany("company.users", "read", out _) ? ToResult(await service.GetPermissionsAsync(membershipId, cancellationToken)) : Forbidden();

    [HttpPut("{membershipId:guid}/permissions")]
    public async Task<ActionResult> ReplacePermissions(Guid membershipId, ReplaceUserPermissionsRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.users", "update", out var scope, "assign_permission") ? ToResult(await service.ReplacePermissionsAsync(scope, membershipId, request, cancellationToken)) : Forbidden();
}
