using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>
/// 角色權限與個人自訂權限共用之單一資源授權配置輸入契約。
/// </summary>
/// <param name="Code">權限項目唯一代碼。</param>
/// <param name="PerCreate">是否賦予新增權限。</param>
/// <param name="PerRead">是否賦予檢視權限。</param>
/// <param name="PerUpdate">是否賦予修改權限。</param>
/// <param name="PerDelete">是否賦予刪除權限。</param>
/// <param name="Options">勾選之子功能選項鍵值清單。</param>
public sealed record PermissionAllowanceInput(string Code, bool PerCreate, bool PerRead, bool PerUpdate, bool PerDelete, string[]? Options)
{
    /// <summary>
    /// 轉換為領域層標準 PermissionAllowance 授權物件。
    /// </summary>
    /// <returns>傳回 PermissionAllowance 實體。</returns>
    public PermissionAllowance ToAllowance() => new(Code, PerCreate, PerRead, PerUpdate, PerDelete, (Options ?? []).Distinct(StringComparer.Ordinal).ToArray());
}

/// <summary>
/// 權限目錄讀取、樹狀結構組合與授權輸入資料驗證工具類別。
/// </summary>
public static class PermissionInputs
{
    /// <summary>
    /// 驗證權限代碼存在性、範圍歸屬、群組節點限制與子功能鍵值合法性。
    /// </summary>
    /// <param name="db">資料庫上下文實體。</param>
    /// <param name="inputs">使用者提交之權限授權清單。</param>
    /// <param name="scope">目標適用範圍（company 或 platform）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回錯誤訊息（若無錯誤為 null）及代碼對應權限 ID 字典。</returns>
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

    /// <summary>
    /// 依據權限目錄之父子關聯組合特定 scope 之階層樹狀結構，供權限配置矩陣畫面渲染。
    /// </summary>
    /// <param name="db">資料庫上下文實體。</param>
    /// <param name="scope">目標範圍（company 或 platform）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回階層目錄樹物件陣列。</returns>
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

    /// <summary>
    /// 將 PermissionGrant 授權條目轉為前端 DTO 匿名物件。
    /// </summary>
    /// <param name="grant">領域授權項目。</param>
    /// <returns>DTO 物件。</returns>
    public static object ToDto(PermissionGrant grant) => new { code = grant.Code, actions = grant.Actions, options = grant.Options };

    /// <summary>
    /// 將子功能選項鍵值清單序列化為 JSON 字串。
    /// </summary>
    /// <param name="options">子功能字串集合。</param>
    /// <returns>JSON 字串。</returns>
    public static string SerializeOptions(IEnumerable<string> options) => JsonSerializer.Serialize(options.Distinct(StringComparer.Ordinal).ToArray());

    /// <summary>
    /// 遞增指定使用者的 AuthVersion 權杖撤銷版本號，強制現存之所有舊 Access Token 立即失效。
    /// </summary>
    /// <param name="db">資料庫上下文實體。</param>
    /// <param name="userIds">受影響的使用者 ID 集合。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
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
