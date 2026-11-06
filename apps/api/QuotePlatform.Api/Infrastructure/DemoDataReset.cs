using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 重置開發／展示資料庫（<c>--reset-demo-data</c>）：刪除所有公司與其業務資料、以及不屬於平台的帳號，
/// 只留下權限目錄、系統角色與平台帳號；之後由 <see cref="QuoteSeeder"/> 重建 smoke 測試用的 demo 公司與展示公司。
/// </summary>
/// <remarks>
/// 只允許在 <c>Quote__SeedDemoData=true</c> 的環境執行（開發機、展示機），正式環境不會開這個旗標。
/// 這是維運指令，以原生 SQL 執行：表名與欄位一律從 EF 模型取得，新增資料表不必回來改這裡。
/// 刪除期間關閉外鍵檢查，結束前逐一檢查模型上的每個外鍵，留下任何孤兒資料就整批回滾。
/// file-server 上的檔案本體不在資料庫裡，需要另外清除（開發機為 file-server/data）。
/// </remarks>
public sealed class DemoDataReset(QuoteDbContext db, IOptions<QuoteOptions> options, ILogger<DemoDataReset> logger)
{
    /// <summary>執行重置；回傳刪除的公司數與帳號數。</summary>
    /// <exception cref="InvalidOperationException">環境未開啟 SeedDemoData，或刪除後仍有孤兒資料。</exception>
    public async Task<(int Companies, int Users)> RunAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.SeedDemoData) throw new InvalidOperationException("只有 Quote__SeedDemoData=true 的環境可以重置展示資料。");

        var tables = TableEntities().ToList();
        var companyTable = Table(db.Model.FindEntityType(typeof(Company))!);
        var userEntity = db.Model.FindEntityType(typeof(User))!;
        var userTable = Table(userEntity);
        var systemRole = Column(userEntity, userEntity.FindProperty(nameof(User.SystemRoleId))!);
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // FOREIGN_KEY_CHECKS 是連線層級設定，所以整個流程固定在同一條連線上。
            await db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0", cancellationToken);
            foreach (var entity in tables)
            {
                var company = entity.FindProperty(nameof(ICompanyOwned.CompanyId));
                if (company is null || entity.ClrType == typeof(Company)) continue;
                // CompanyId 為 NULL 的是平台層資料（系統角色、平台稽核），保留。
                await ExecuteAsync($"DELETE FROM `{Table(entity)}` WHERE `{Column(entity, company)}` IS NOT NULL", cancellationToken);
            }
            var companies = await ExecuteAsync($"DELETE FROM `{companyTable}`", cancellationToken);
            var users = await ExecuteAsync($"DELETE FROM `{userTable}` WHERE `{systemRole}` IS NULL", cancellationToken);
            await RemoveOrphansAsync(cancellationToken);

            var broken = await CountOrphansAsync(cancellationToken);
            if (broken.Count > 0) throw new InvalidOperationException($"重置後仍有孤兒資料：{string.Join("、", broken)}");
            await db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            logger.LogInformation("已刪除 {Companies} 家公司、{Users} 個帳號。", companies, users);
            return (companies, users);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1", CancellationToken.None);
            throw;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>沒有 CompanyId 的子表（例如 refresh token、角色權限）跟著父資料刪除；可為 NULL 的外鍵改成 NULL。</summary>
    private async Task RemoveOrphansAsync(CancellationToken cancellationToken)
    {
        // 子表的父資料可能也是被連帶刪掉的孤兒，重複幾輪直到沒有變化。
        for (var round = 0; round < 5; round++)
        {
            var changed = 0;
            foreach (var foreignKey in ForeignKeys())
            {
                var (child, parent, join, missing, columns) = Describe(foreignKey);
                changed += foreignKey.IsRequired
                    ? await ExecuteAsync($"DELETE c FROM `{child}` c LEFT JOIN `{parent}` p ON {join} WHERE {missing}", cancellationToken)
                    : await ExecuteAsync($"UPDATE `{child}` c LEFT JOIN `{parent}` p ON {join} SET {string.Join(", ", columns.Select(column => $"c.`{column}` = NULL"))} WHERE {missing}", cancellationToken);
            }
            if (changed == 0) return;
        }
    }

    private async Task<List<string>> CountOrphansAsync(CancellationToken cancellationToken)
    {
        var broken = new List<string>();
        foreach (var foreignKey in ForeignKeys())
        {
            var (child, parent, join, missing, _) = Describe(foreignKey);
            var count = await CountAsync($"SELECT COUNT(*) AS Value FROM `{child}` c LEFT JOIN `{parent}` p ON {join} WHERE {missing}", cancellationToken);
            if (count > 0) broken.Add($"{child}→{parent} {count} 筆");
        }
        return broken;
    }

    // 表名與欄位名無法用參數帶入；這裡組進 SQL 的只有 EF 模型裡的識別字，沒有任何外部輸入。
    private Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken) => db.Database.ExecuteSqlRawAsync(sql, cancellationToken);

    private Task<long> CountAsync(string sql, CancellationToken cancellationToken) => db.Database.SqlQueryRaw<long>(sql).SingleAsync(cancellationToken);

    private IEnumerable<IEntityType> TableEntities() =>
        db.Model.GetEntityTypes().Where(value => !value.IsOwned() && value.GetTableName() is not null && value.GetViewName() is null).DistinctBy(Table);

    private IEnumerable<IForeignKey> ForeignKeys() =>
        TableEntities().SelectMany(value => value.GetForeignKeys()).Where(value => value.PrincipalEntityType.GetTableName() is not null);

    private static (string Child, string Parent, string Join, string Missing, List<string> Columns) Describe(IForeignKey foreignKey)
    {
        var childColumns = foreignKey.Properties.Select(value => Column(foreignKey.DeclaringEntityType, value)).ToList();
        var parentColumns = foreignKey.PrincipalKey.Properties.Select(value => Column(foreignKey.PrincipalEntityType, value)).ToList();
        var join = string.Join(" AND ", childColumns.Zip(parentColumns, (child, parent) => $"c.`{child}` = p.`{parent}`"));
        var missing = $"{string.Join(" AND ", childColumns.Select(column => $"c.`{column}` IS NOT NULL"))} AND p.`{parentColumns[0]}` IS NULL";
        return (Table(foreignKey.DeclaringEntityType), Table(foreignKey.PrincipalEntityType), join, missing, childColumns);
    }

    private static string Table(IReadOnlyEntityType entity) => entity.GetTableName()!;

    private static string Column(IReadOnlyEntityType entity, IReadOnlyProperty property) =>
        property.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema())) ?? property.Name;
}
