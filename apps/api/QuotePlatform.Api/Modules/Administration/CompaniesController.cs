using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>
/// 建立新租戶公司與初始管理員帳號之輸入契約。
/// </summary>
/// <param name="Name">公司名稱。</param>
/// <param name="Slug">公司租戶唯一網址代碼（長度 2~50 小寫英數與連字號）。</param>
/// <param name="TaxId">統一編號。</param>
/// <param name="Phone">代表電話。</param>
/// <param name="Address">登記地址。</param>
/// <param name="AdminAccount">初始最高管理員登入帳號。</param>
/// <param name="AdminDisplayName">初始管理員姓名。</param>
/// <param name="AdminEmail">初始管理員電子郵件。</param>
/// <param name="AdminPassword">初始管理員密碼。</param>
public sealed record CreateCompanyRequest(string? Name, string? Slug, string? TaxId, string? Phone, string? Address, string? AdminAccount, string? AdminDisplayName, string? AdminEmail, string? AdminPassword);

/// <summary>
/// 更新租戶公司基本資料與權限旗標之輸入契約。
/// </summary>
/// <param name="Name">公司名稱。</param>
/// <param name="TaxId">統一編號。</param>
/// <param name="Phone">電話號碼。</param>
/// <param name="Address">地址。</param>
/// <param name="Status">營運狀態（active 或 suspended）。</param>
/// <param name="IsRoleCrudConfigurationEnabled">是否允許該公司自訂角色 CRUD。</param>
/// <param name="IsRoleOptionConfigurationEnabled">是否允許該公司配置角色細部功能選項。</param>
public sealed record UpdateCompanyRequest(string? Name, string? TaxId, string? Phone, string? Address, string? Status, bool? IsRoleCrudConfigurationEnabled, bool? IsRoleOptionConfigurationEnabled);

/// <summary>
/// 平台公司管理控制器（平台管理員專屬 platform scope）。
/// 負責租戶公司的開通、狀態變更與權限旗標配置。
/// </summary>
/// <param name="service">公司管理核心業務服務實體。</param>
[Route("api/platform/companies")]
public sealed class CompaniesController(CompanyService service) : ScopedControllerBase
{
    /// <summary>
    /// 分頁查詢所有註冊之租戶公司清單（支援名稱、代碼與統編關鍵字搜尋）。
    /// </summary>
    /// <param name="query">分頁與關鍵字條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁公司資料與成員人數統計。</returns>
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryPlatform("platform.companies", "read", out _) ? Ok(await service.ListAsync(query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 開通新租戶公司並初始化其最高管理員帳號、預設部門與預設角色。
    /// </summary>
    /// <param name="request">公司與管理員帳號輸入內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新開通公司之 ID 與代碼。</returns>
    [HttpPost]
    public async Task<ActionResult> Create(CreateCompanyRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.companies", "create", out var scope) ? ToResult(await service.CreateAsync(scope, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新指定公司之基本資料或停權狀態。
    /// </summary>
    /// <param name="companyId">公司唯一識別碼。</param>
    /// <param name="request">更新資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPatch("{companyId:guid}")]
    public async Task<ActionResult> Update(Guid companyId, UpdateCompanyRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.companies", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, companyId, request, cancellationToken)) : Forbidden();
}

/// <summary>
/// 租戶公司管理與初始化領域服務。
/// </summary>
/// <param name="db">資料庫上下文實體。</param>
/// <param name="provisioner">租戶自動佈建服務實體。</param>
/// <param name="audit">稽核紀錄服務實體。</param>
public sealed partial class CompanyService(QuoteDbContext db, CompanyProvisioner provisioner, AuditService audit)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,49}$")]
    private static partial Regex SlugPattern();

    /// <summary>
    /// 條件分頁查詢公司列表。
    /// </summary>
    /// <param name="query">查詢參數。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回包含成員數統計之分頁清單。</returns>
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

    /// <summary>
    /// 建立並開通新公司。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="request">公司與管理員輸入契約。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立後公司物件。</returns>
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

    /// <summary>
    /// 更新指定公司設定與狀態。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="companyId">公司唯一識別碼。</param>
    /// <param name="request">更新資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
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
