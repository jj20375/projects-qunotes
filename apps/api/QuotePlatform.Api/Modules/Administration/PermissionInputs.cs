using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>角色權限與個人權限共用的單一資源設定。</summary>
public sealed record PermissionAllowanceInput(string Code, bool PerCreate, bool PerRead, bool PerUpdate, bool PerDelete, string[]? Options)
{
    public PermissionAllowance ToAllowance() => new(Code, PerCreate, PerRead, PerUpdate, PerDelete, (Options ?? []).Distinct(StringComparer.Ordinal).ToArray());
}

/// <summary>權限目錄的讀取與輸入驗證。</summary>
public static class PermissionInputs
{
    /// <summary>驗證代碼存在、屬於指定 scope、不是群組節點、子功能皆有定義；回傳錯誤訊息或 null。</summary>
    public static async Task<(string? Error, IReadOnlyDictionary<string, Guid> PermissionIds)> ValidateAsync(
        QuoteDbContext db,
        IEnumerable<PermissionAllowanceInput> inputs,
        string scope,
        CancellationToken cancellationToken)
    {
        var permissions = await db.Permissions.AsNoTracking().Where(value => value.Scope == scope).ToListAsync(cancellationToken);
        var byCode = permissions.ToDictionary(value => value.Code, StringComparer.Ordinal);
        var list = inputs.ToList();
        if (list.Select(value => value.Code).Distinct(StringComparer.Ordinal).Count() != list.Count) return ("同一資源不可重複設定。", byCode.ToDictionary(value => value.Key, value => value.Value.Id));
        foreach (var input in list)
        {
            if (!byCode.TryGetValue(input.Code, out var permission) || PermissionCatalog.IsGroup(input.Code))
            {
                return ($"權限 {input.Code} 不存在或不可授予。", new Dictionary<string, Guid>());
            }
            var keys = PermissionGrantService.ReadSubFeatureKeys(permission.SubFeaturesJson);
            var unknown = (input.Options ?? []).FirstOrDefault(option => !keys.Contains(option, StringComparer.Ordinal));
            if (unknown is not null) return ($"權限 {input.Code} 沒有子功能 {unknown}。", new Dictionary<string, Guid>());
        }
        return (null, byCode.ToDictionary(value => value.Key, value => value.Value.Id, StringComparer.Ordinal));
    }

    /// <summary>以目錄的 parent 樹與子功能回傳指定 scope 的權限樹，供角色與個人權限編輯畫面使用。</summary>
    public static async Task<IReadOnlyList<object>> CatalogTreeAsync(QuoteDbContext db, string scope, CancellationToken cancellationToken)
    {
        var permissions = await db.Permissions.AsNoTracking().Where(value => value.Scope == scope).OrderBy(value => value.SortOrder).ToListAsync(cancellationToken);
        IReadOnlyList<object> Build(Guid? parentId) => permissions
            .Where(value => value.ParentId == parentId)
            .Select(value => (object)new
            {
                code = value.Code,
                name = value.Name,
                isGroup = PermissionCatalog.IsGroup(value.Code),
                subFeatures = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(value.SubFeaturesJson) ? "[]" : value.SubFeaturesJson),
                children = Build(value.Id),
            })
            .ToList();
        return Build(null);
    }

    public static object ToDto(PermissionGrant grant) => new { code = grant.Code, actions = grant.Actions, options = grant.Options };

    public static string SerializeOptions(IEnumerable<string> options) => JsonSerializer.Serialize(options.Distinct(StringComparer.Ordinal).ToArray());

    /// <summary>遞增使用者的 AuthVersion，使舊 access token 立即失效。</summary>
    public static async Task BumpAuthVersionAsync(QuoteDbContext db, IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var users = await db.Users.Where(value => ids.Contains(value.Id)).ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            user.AuthVersion++;
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
