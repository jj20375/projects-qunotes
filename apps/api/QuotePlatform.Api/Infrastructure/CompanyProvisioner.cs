using Microsoft.EntityFrameworkCore;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>新公司開通的輸入。</summary>
public sealed record CompanyOnboarding(
    string Name,
    string Slug,
    string? TaxId,
    string? Phone,
    string? Address,
    string AdminAccount,
    string AdminDisplayName,
    string? AdminEmail,
    string AdminPassword);

/// <summary>
/// 公司開通：同一交易建立公司、公司設定、根部門與 closure、第一位 company-admin 並設為受保護主管理員。
/// </summary>
/// <remarks>平台 scope 沒有 company_id，因此以 <see cref="CompanyContext.UseCompany"/> 明確切換到新公司寫入。</remarks>
public sealed class CompanyProvisioner(QuoteDbContext db, CompanyContext companyContext)
{
    public async Task<(Company Company, CompanyMembership AdminMembership)> CreateAsync(CompanyOnboarding request, CancellationToken cancellationToken)
    {
        var companyAdminRole = await db.Roles.SingleAsync(value => value.CompanyId == null && value.IsSystem && value.Code == PermissionCatalog.CompanyAdmin, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var company = new Company
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            TaxId = NullIfBlank(request.TaxId),
            Phone = NullIfBlank(request.Phone),
            Address = NullIfBlank(request.Address),
        };
        db.Companies.Add(company);
        companyContext.UseCompany(company.Id);
        db.CompanySettings.Add(new CompanySetting { CompanyId = company.Id });

        var rootDepartment = new Department { CompanyId = company.Id, Name = company.Name, Code = "ROOT" };
        db.Departments.Add(rootDepartment);
        db.DepartmentClosures.Add(new DepartmentClosure { AncestorId = rootDepartment.Id, DescendantId = rootDepartment.Id, CompanyId = company.Id, Depth = 0 });

        var admin = new User
        {
            Account = request.AdminAccount.Trim(),
            DisplayName = request.AdminDisplayName.Trim(),
            Email = NullIfBlank(request.AdminEmail),
        };
        admin.PasswordHash = PasswordRules.Hash(admin, request.AdminPassword);
        db.Users.Add(admin);

        var membership = new CompanyMembership
        {
            CompanyId = company.Id,
            UserId = admin.Id,
            DepartmentId = rootDepartment.Id,
            RoleId = companyAdminRole.Id,
        };
        db.CompanyMemberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);

        // companies 與 company_memberships 互相參照，必須先存 membership 再回寫主管理員。
        company.RootAdminMembershipId = membership.Id;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (company, membership);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>部門樹與 closure table 的共用操作。</summary>
public static class DepartmentTree
{
    /// <summary>新增部門並寫入所有祖先路徑；呼叫端負責 SaveChanges。</summary>
    public static async Task<Department> AddAsync(QuoteDbContext db, Guid companyId, Guid parentId, string name, string? code, int sortOrder, CancellationToken cancellationToken)
    {
        var department = new Department { CompanyId = companyId, ParentId = parentId, Name = name, Code = code, SortOrder = sortOrder };
        db.Departments.Add(department);
        var ancestors = await db.DepartmentClosures.Where(value => value.DescendantId == parentId).ToListAsync(cancellationToken);
        db.DepartmentClosures.AddRange(ancestors.Select(value => new DepartmentClosure
        {
            AncestorId = value.AncestorId,
            DescendantId = department.Id,
            CompanyId = companyId,
            Depth = value.Depth + 1,
        }));
        db.DepartmentClosures.Add(new DepartmentClosure { AncestorId = department.Id, DescendantId = department.Id, CompanyId = companyId, Depth = 0 });
        return department;
    }
}
