namespace QuotePlatform.Api.Infrastructure;

/// <summary>單一資源的 CRUD 動作與子功能授予。</summary>
public sealed record PermissionGrant(string Code, IReadOnlyCollection<string> Actions, IReadOnlyCollection<string> Options);

/// <summary>單一資源的 CRUD 與子功能設定，角色權限與個人權限共用。</summary>
public sealed record PermissionAllowance(string Code, bool PerCreate, bool PerRead, bool PerUpdate, bool PerDelete, IReadOnlyCollection<string> Options)
{
    public PermissionGrant ToGrant() => new(
        Code,
        new[] { PerCreate ? "create" : null, PerRead ? "read" : null, PerUpdate ? "update" : null, PerDelete ? "delete" : null }
            .Where(value => value is not null).Cast<string>().ToArray(),
        Options.Distinct(StringComparer.Ordinal).ToArray());
}

/// <summary>
/// 有效權限計算的純規則：
/// <c>有效 grant = (角色 grant ∪ 個人加權) − 個人禁止 ∩ company-admin 上限</c>。
/// </summary>
/// <remarks>禁止優先於加權；上限以 action／option 為單位交集，任何超出都會被靜默移除（fail-closed）。</remarks>
public static class PermissionGrantRules
{
    public static readonly string[] CrudActions = ["create", "read", "update", "delete"];

    /// <summary>合併多組 grant（聯集）。</summary>
    public static IReadOnlyCollection<PermissionGrant> Union(params IEnumerable<PermissionGrant>[] sources) =>
        sources.SelectMany(value => value)
            .GroupBy(value => value.Code, StringComparer.Ordinal)
            .Select(group => new PermissionGrant(
                group.Key,
                group.SelectMany(value => value.Actions).Distinct(StringComparer.Ordinal).ToArray(),
                group.SelectMany(value => value.Options).Distinct(StringComparer.Ordinal).ToArray()))
            .Where(value => value.Actions.Count > 0 || value.Options.Count > 0)
            .OrderBy(value => value.Code, StringComparer.Ordinal)
            .ToArray();

    /// <summary>從 grants 移除 denies 中列出的 action 與 option。</summary>
    public static IReadOnlyCollection<PermissionGrant> Subtract(IEnumerable<PermissionGrant> grants, IEnumerable<PermissionGrant> denies)
    {
        var denyByCode = Union(denies).ToDictionary(value => value.Code, StringComparer.Ordinal);
        return grants
            .Select(value => denyByCode.TryGetValue(value.Code, out var deny)
                ? new PermissionGrant(value.Code, value.Actions.Except(deny.Actions, StringComparer.Ordinal).ToArray(), value.Options.Except(deny.Options, StringComparer.Ordinal).ToArray())
                : value)
            .Where(value => value.Actions.Count > 0 || value.Options.Count > 0)
            .OrderBy(value => value.Code, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>與上限交集；上限沒有的資源整個移除。</summary>
    public static IReadOnlyCollection<PermissionGrant> Restrict(IEnumerable<PermissionGrant> grants, IEnumerable<PermissionGrant> ceiling)
    {
        var ceilingByCode = Union(ceiling).ToDictionary(value => value.Code, StringComparer.Ordinal);
        return grants
            .Where(value => ceilingByCode.ContainsKey(value.Code))
            .Select(value => new PermissionGrant(
                value.Code,
                value.Actions.Intersect(ceilingByCode[value.Code].Actions, StringComparer.Ordinal).ToArray(),
                value.Options.Intersect(ceilingByCode[value.Code].Options, StringComparer.Ordinal).ToArray()))
            .Where(value => value.Actions.Count > 0 || value.Options.Count > 0)
            .OrderBy(value => value.Code, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>計算公司範圍的有效權限。</summary>
    public static IReadOnlyCollection<PermissionGrant> Effective(
        IEnumerable<PermissionGrant> roleGrants,
        IEnumerable<PermissionGrant> personalGrants,
        IEnumerable<PermissionGrant> personalDenies,
        IEnumerable<PermissionGrant> ceiling) =>
        Restrict(Subtract(Union(roleGrants, personalGrants), personalDenies), ceiling);

    /// <summary>
    /// 公司關閉 CRUD／子功能細項設定時，把已授予的資源展開為完整 CRUD 或全部子功能；只在簽發 JWT 時套用，不改寫資料。
    /// </summary>
    public static IReadOnlyCollection<PermissionGrant> ApplyCompanyConfiguration(
        IEnumerable<PermissionGrant> grants,
        bool crudConfigurationEnabled,
        bool optionConfigurationEnabled,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> optionsByCode) =>
        grants.Select(value => new PermissionGrant(
                value.Code,
                crudConfigurationEnabled || value.Actions.Count == 0 ? value.Actions : CrudActions,
                optionConfigurationEnabled ? value.Options : optionsByCode.GetValueOrDefault(value.Code) ?? []))
            .ToArray();

    /// <summary>判斷 requested 是否完全落在 allowed 之內，用於角色／個人權限的寫入驗證。</summary>
    public static bool IsSubsetOf(IEnumerable<PermissionGrant> requested, IEnumerable<PermissionGrant> allowed)
    {
        var allowedByCode = Union(allowed).ToDictionary(value => value.Code, StringComparer.Ordinal);
        return requested.All(value =>
            (value.Actions.Count == 0 && value.Options.Count == 0)
            || (allowedByCode.TryGetValue(value.Code, out var limit)
                && value.Actions.All(action => limit.Actions.Contains(action, StringComparer.Ordinal))
                && value.Options.All(option => limit.Options.Contains(option, StringComparer.Ordinal))));
    }

    /// <summary>供 <c>--self-test</c> 驗證計算規則。</summary>
    public static void AssertExamples()
    {
        PermissionGrant G(string code, string[] actions, string[]? options = null) => new(code, actions, options ?? []);
        string Describe(IEnumerable<PermissionGrant> grants) =>
            string.Join(";", grants.Select(value => $"{value.Code}={string.Join(',', value.Actions.Order())}|{string.Join(',', value.Options.Order())}"));
        void Expect(string name, IEnumerable<PermissionGrant> actual, string expected)
        {
            var text = Describe(actual);
            if (text != expected) throw new InvalidOperationException($"權限規則驗證失敗（{name}）：預期 {expected}，實際 {text}");
        }

        var ceiling = new[]
        {
            G("quotes", ["create", "read", "update", "delete"], ["approve", "export_pdf", "view_cost"]),
            G("projects", ["create", "read", "update", "delete"], ["assign_member"]),
        };

        // 角色 + 個人加權取聯集。
        Expect("聯集",
            Effective([G("quotes", ["read"])], [G("quotes", ["update"], ["export_pdf"])], [], ceiling),
            "quotes=read,update|export_pdf");

        // 禁止優先於角色與個人加權。
        Expect("禁止優先",
            Effective([G("quotes", ["read", "update"], ["approve"])], [G("quotes", ["delete"])], [G("quotes", ["update", "delete"], ["approve"])], ceiling),
            "quotes=read|");

        // 超出上限的 action、option 與資源都會被移除。
        Expect("上限",
            Effective([G("quotes", ["read"], ["export_pdf", "hack"]), G("platform.companies", ["read"])], [], [], ceiling),
            "quotes=read|export_pdf");

        // 全部被禁止的資源整列消失。
        Expect("整列移除", Effective([G("projects", ["read"])], [], [G("projects", ["read"])], ceiling), "");

        // 公司關閉細項設定時展開為完整 CRUD 與全部子功能。
        Expect("簡化設定",
            ApplyCompanyConfiguration([G("projects", ["read"])], false, false, new Dictionary<string, IReadOnlyCollection<string>> { ["projects"] = ["assign_member"] }),
            "projects=create,delete,read,update|assign_member");

        if (!IsSubsetOf([G("quotes", ["read"], ["approve"])], ceiling) || IsSubsetOf([G("quotes", ["read"], ["hack"])], ceiling) || IsSubsetOf([G("clients", ["read"])], ceiling))
        {
            throw new InvalidOperationException("權限規則驗證失敗（子集合判斷）。");
        }
    }
}
