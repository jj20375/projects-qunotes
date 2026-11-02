using Microsoft.EntityFrameworkCore;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 客戶的可見範圍：依角色／個人的資料範圍與客戶「所屬部門」決定，和專案的規則對應但以部門為準。
/// 本人（own）與部門（department）都只看自己部門的客戶；部門樹（department_tree）含所有下層部門；全公司（company）看全部。
/// 沒有部門的帳號（理論上不會發生）看不到任何有部門的客戶。
/// </summary>
public sealed class ClientAccessService(QuoteDbContext db)
{
    public IQueryable<Client> VisibleClients(RequestScope scope)
    {
        var clients = db.Clients.AsQueryable();
        if (scope.DataScope == DataScopes.Company) return clients;
        if (scope.DepartmentId is not Guid departmentId) return clients.Where(_ => false);
        return scope.DataScope == DataScopes.DepartmentTree
            ? clients.Where(value => db.DepartmentClosures.Any(path => path.AncestorId == departmentId && path.DescendantId == value.DepartmentId))
            : clients.Where(value => value.DepartmentId == departmentId);
    }

    /// <summary>建立或移動客戶時可選的部門；null 代表不限（全公司範圍）。</summary>
    public bool CanChooseDepartment(RequestScope scope) => scope.DataScope == DataScopes.Company;
}
