using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Projects;

/// <summary>專案搜尋條件；status 可多選（status=quoting&amp;status=won）。</summary>
public sealed record ProjectSearchQuery(
    string? Keyword,
    string[]? Status,
    string? DateField,
    DateOnly? From,
    DateOnly? To,
    string? AmountField,
    decimal? AmountMin,
    decimal? AmountMax,
    Guid? ClientId,
    Guid? OwnerUserId,
    Guid? DepartmentId,
    bool? Mine,
    int? Page,
    int? PageSize,
    string? Sort,
    bool? Desc);

public sealed record ProjectInput(
    string? Name,
    Guid? ClientId,
    Guid? DepartmentId,
    Guid? OwnerUserId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal? ExpectedAmount,
    decimal? ContractAmount,
    decimal? ContractTotal,
    string? Description,
    ProjectMemberInput[]? Members);

public sealed record ProjectMemberInput(Guid UserId, string? ProjectRole);
public sealed record ChangeStatusRequest(string? Status, string? Reason);

/// <summary>專案（company scope）。所有讀寫都經過 ProjectAccessService 套用資料範圍；看不到的專案一律回 404。</summary>
[Route("api/projects")]
public sealed class ProjectsController(ProjectService service) : ScopedControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Search([FromQuery] ProjectSearchQuery query, CancellationToken cancellationToken) =>
        TryCompany("projects", "read", out var scope) ? ToResult(await service.SearchAsync(scope, query, cancellationToken)) : Forbidden();

    [HttpGet("{projectId:guid}")]
    public async Task<ActionResult> Get(Guid projectId, CancellationToken cancellationToken) =>
        TryCompany("projects", "read", out var scope) ? ToResult(await service.GetAsync(scope, projectId, cancellationToken)) : Forbidden();

    [HttpPost]
    public async Task<ActionResult> Create(ProjectInput input, CancellationToken cancellationToken) =>
        TryCompany("projects", "create", out var scope) ? ToResult(await service.CreateAsync(scope, input, cancellationToken)) : Forbidden();

    [HttpPut("{projectId:guid}")]
    public async Task<ActionResult> Update(Guid projectId, ProjectInput input, CancellationToken cancellationToken) =>
        TryCompany("projects", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, projectId, input, cancellationToken)) : Forbidden();

    [HttpPost("{projectId:guid}/status")]
    public async Task<ActionResult> ChangeStatus(Guid projectId, ChangeStatusRequest request, CancellationToken cancellationToken) =>
        TryCompany("projects", "update", out var scope) ? ToResult(await service.ChangeStatusAsync(scope, projectId, request, cancellationToken)) : Forbidden();

    [HttpDelete("{projectId:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, CancellationToken cancellationToken) =>
        TryCompany("projects", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, projectId, cancellationToken)) : Forbidden();

    /// <summary>整批取代專案成員（負責人不在清單內，固定有完整權限）。</summary>
    [HttpPut("{projectId:guid}/members")]
    public async Task<ActionResult> ReplaceMembers(Guid projectId, ProjectMemberInput[] members, CancellationToken cancellationToken) =>
        TryCompany("projects", "update", out var scope, "assign_member") ? ToResult(await service.ReplaceMembersAsync(scope, projectId, members, cancellationToken)) : Forbidden();
}
