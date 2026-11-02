using Microsoft.EntityFrameworkCore;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 公司內的流水號，例如專案 <c>P2026-0001</c>、客戶 <c>C0001</c>。
/// </summary>
/// <remarks>
/// 以 MariaDB 的 <c>INSERT … ON DUPLICATE KEY UPDATE CurrentValue = LAST_INSERT_ID(CurrentValue + 1)</c> 原子遞增，
/// 同一列的並行請求由 InnoDB row lock 排隊，不會取到相同號碼。<c>LAST_INSERT_ID()</c> 依連線計算，
/// 所以兩個指令必須在同一條連線上執行（明確開啟連線，或呼叫端已開交易）。
/// 取號後若業務寫入失敗會留下跳號，這是刻意接受的取捨：不用為了連號鎖住整個建立流程。
/// </remarks>
public sealed class NumberSequenceService(QuoteDbContext db)
{
    public async Task<string> NextAsync(Guid companyId, string docType, string prefix, string period, int digits, CancellationToken cancellationToken) =>
        $"{prefix}{(await NextValueAsync(companyId, docType, prefix, period, cancellationToken)).ToString().PadLeft(digits, '0')}";

    /// <summary>
    /// 依公司設定的單號規則取下一個號碼（見 <see cref="DocumentNumberPattern"/>）。
    /// 規則改過時，新舊規則可能剛好排出相同字串，所以由呼叫端以 <paramref name="exists"/> 檢查，重複就再取下一號。
    /// </summary>
    public async Task<string> NextByPatternAsync(Guid companyId, string docType, string pattern, DateOnly date, Func<string, Task<bool>> exists, CancellationToken cancellationToken)
    {
        var period = DocumentNumberPattern.Period(pattern, date);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = DocumentNumberPattern.Format(pattern, date, await NextValueAsync(companyId, docType, pattern.Length > 20 ? pattern[..20] : pattern, period, cancellationToken));
            if (!await exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("連續取得的單號都已存在，請檢查單號規則。");
    }

    private async Task<long> NextValueAsync(Guid companyId, string docType, string prefix, string period, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO number_sequences (CompanyId, DocType, Period, Prefix, CurrentValue, UpdatedAt)
                VALUES ({companyId}, {docType}, {period}, {prefix}, LAST_INSERT_ID(1), {now})
                ON DUPLICATE KEY UPDATE CurrentValue = LAST_INSERT_ID(CurrentValue + 1), UpdatedAt = {now}
                """,
                cancellationToken);
            return await db.Database.SqlQueryRaw<long>("SELECT LAST_INSERT_ID() AS Value").SingleAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>專案編號：P{西元年}-{4 碼}，每年重新起算。</summary>
    public Task<string> NextProjectNoAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var year = BusinessTime.Today.Year.ToString();
        return NextAsync(companyId, "project", $"P{year}-", year, 4, cancellationToken);
    }

    /// <summary>客戶代碼：C{4 碼}，不分年。</summary>
    public Task<string> NextClientCodeAsync(Guid companyId, CancellationToken cancellationToken) =>
        NextAsync(companyId, "client", "C", "all", 4, cancellationToken);
}
