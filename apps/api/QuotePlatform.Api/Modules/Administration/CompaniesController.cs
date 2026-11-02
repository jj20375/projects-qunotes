using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

public sealed record CreateCompanyRequest(string? Name, string? Slug, string? TaxId, string? Phone, string? Address, string? AdminAccount, string? AdminDisplayName, string? AdminEmail, string? AdminPassword);
public sealed record UpdateCompanyRequest(string? Name, string? TaxId, string? Phone, string? Address, string? Status, bool? IsRoleCrudConfigurationEnabled, bool? IsRoleOptionConfigurationEnabled);

/// <summary>平台管理公司（platform scope）。</summary>
[Route("api/platform/companies")]
public sealed class CompaniesController(CompanyService service) : ScopedControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryPlatform("platform.companies", "read", out _) ? Ok(await service.ListAsync(query, cancellationToken)) : Forbidden();

    [HttpPost]
    public async Task<ActionResult> Create(CreateCompanyRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.companies", "create", out var scope) ? ToResult(await service.CreateAsync(scope, request, cancellationToken)) : Forbidden();

    [HttpPatch("{companyId:guid}")]
    public async Task<ActionResult> Update(Guid companyId, UpdateCompanyRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.companies", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, companyId, request, cancellationToken)) : Forbidden();
}

/// <summary>公司列表、開通與狀態管理。</summary>
public sealed partial class CompanyService(QuoteDbContext db, CompanyProvisioner provisioner, AuditService audit)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,49}$")]
    private static partial Regex SlugPattern();

    /// <summary>公司列表：關鍵字比對名稱、代碼、統編，分頁由資料庫處理。</summary>
    public async Task<object> ListAsync(ListQuery query, CancellationToken cancellationToken)
    {
        // platform scope 沒有 company_id，跨公司統計必須明確忽略 query filter。
        var memberCounts = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(value => value.CompanyId)
            .Select(group => new { CompanyId = group.Key, Count = group.Count(value => value.IsActive) })
            .ToDictionaryAsync(value => value.CompanyId, value => value.Count, cancellationToken);
        var rootAdmins = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Join(db.Companies, membership => membership.Id, company => company.RootAdminMembershipId, (membership, company) => new { company.Id, membership.User.Account, membership.User.DisplayName })
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var filtered = db.Companies.AsNoTracking();
        if (query.KeywordText is string keyword)
        {
            filtered = filtered.Where(value => value.Name.Contains(keyword) || value.Slug.Contains(keyword) || value.TaxId == keyword);
        }
        var total = await filtered.CountAsync(cancellationToken);
        var companies = await filtered.OrderBy(value => value.Name).Skip(query.Skip).Take(query.PageSizeValue).ToListAsync(cancellationToken);
        var items = companies.Select(value => new
        {
            value.Id,
            value.Name,
            value.Slug,
            value.TaxId,
            value.Phone,
            value.Address,
            value.Status,
            value.IsRoleCrudConfigurationEnabled,
            value.IsRoleOptionConfigurationEnabled,
            value.CreatedAt,
            memberCount = memberCounts.GetValueOrDefault(value.Id),
            rootAdminAccount = rootAdmins.GetValueOrDefault(value.Id)?.Account,
            rootAdminName = rootAdmins.GetValueOrDefault(value.Id)?.DisplayName,
        }).ToList();
        return new { total, page = query.PageValue, pageSize = query.PageSizeValue, items };
    }

    public async Task<ServiceResult<object>> CreateAsync(RequestScope scope, CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return ServiceResult<object>.BadRequest("請輸入公司名稱。");
        var slug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SlugPattern().IsMatch(slug)) return ServiceResult<object>.BadRequest("公司代碼只能使用小寫英數與連字號，長度 2～50。");
        if (string.IsNullOrWhiteSpace(request.AdminAccount) || string.IsNullOrWhiteSpace(request.AdminDisplayName)) return ServiceResult<object>.BadRequest("請輸入管理員帳號與名稱。");
        if (PasswordRules.Validate(request.AdminPassword) is string passwordError) return ServiceResult<object>.BadRequest(passwordError);
        if (await db.Companies.AnyAsync(value => value.Slug == slug, cancellationToken)) return ServiceResult<object>.Conflict("公司代碼已被使用。");
        var account = request.AdminAccount.Trim();
        if (await db.Users.AnyAsync(value => value.Account == account, cancellationToken)) return ServiceResult<object>.Conflict("管理員帳號已存在。");

        var (company, membership) = await provisioner.CreateAsync(
            new CompanyOnboarding(request.Name, slug, request.TaxId, request.Phone, request.Address, account, request.AdminDisplayName, request.AdminEmail, request.AdminPassword!),
            cancellationToken);
        audit.Add(company.Id, scope.UserId, "company", company.Id, "create", after: new { company.Name, company.Slug, adminAccount = account, adminMembershipId = membership.Id });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new { company.Id, company.Name, company.Slug });
    }

    public async Task<ServiceResult<object>> UpdateAsync(RequestScope scope, Guid companyId, UpdateCompanyRequest request, CancellationToken cancellationToken)
    {
        var company = await db.Companies.SingleOrDefaultAsync(value => value.Id == companyId, cancellationToken);
        if (company is null) return ServiceResult<object>.NotFound("找不到公司。");
        if (request.Status is not (null or "active" or "suspended")) return ServiceResult<object>.BadRequest("狀態只能是 active 或 suspended。");
        var before = new { company.Name, company.TaxId, company.Phone, company.Address, company.Status, company.IsRoleCrudConfigurationEnabled, company.IsRoleOptionConfigurationEnabled };

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return ServiceResult<object>.BadRequest("公司名稱不可空白。");
            company.Name = request.Name.Trim();
        }
        if (request.TaxId is not null) company.TaxId = NullIfBlank(request.TaxId);
        if (request.Phone is not null) company.Phone = NullIfBlank(request.Phone);
        if (request.Address is not null) company.Address = NullIfBlank(request.Address);

        // 停用公司或改變權限細項設定會影響成員的有效權限，必須讓所有成員的舊 token 失效。
        var affectsGrants = (request.Status is not null && request.Status != company.Status)
            || (request.IsRoleCrudConfigurationEnabled is bool crud && crud != company.IsRoleCrudConfigurationEnabled)
            || (request.IsRoleOptionConfigurationEnabled is bool option && option != company.IsRoleOptionConfigurationEnabled);
        company.Status = request.Status ?? company.Status;
        company.IsRoleCrudConfigurationEnabled = request.IsRoleCrudConfigurationEnabled ?? company.IsRoleCrudConfigurationEnabled;
        company.IsRoleOptionConfigurationEnabled = request.IsRoleOptionConfigurationEnabled ?? company.IsRoleOptionConfigurationEnabled;
        company.UpdatedAt = DateTimeOffset.UtcNow;

        if (affectsGrants)
        {
            var memberIds = await db.CompanyMemberships.IgnoreQueryFilters().Where(value => value.CompanyId == companyId).Select(value => value.UserId).ToListAsync(cancellationToken);
            await PermissionInputs.BumpAuthVersionAsync(db, memberIds, cancellationToken);
        }
        audit.Add(company.Id, scope.UserId, "company", company.Id, "update", before, new { company.Name, company.TaxId, company.Phone, company.Address, company.Status, company.IsRoleCrudConfigurationEnabled, company.IsRoleOptionConfigurationEnabled });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { company.Id });
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
