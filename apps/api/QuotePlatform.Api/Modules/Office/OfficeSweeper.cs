using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>記錄每條 hub 連線的 context，讓背景工作可以主動斷線（IHubContext 做不到）。</summary>
public sealed class OfficeConnections
{
    private readonly ConcurrentDictionary<string, HubCallerContext> _contexts = new(StringComparer.Ordinal);

    /// <summary>登記一條新連線。</summary>
    /// <param name="context">該連線的 hub context。</param>
    public void Register(HubCallerContext context) => _contexts[context.ConnectionId] = context;

    /// <summary>連線結束時移除登記。</summary>
    /// <param name="connectionId">SignalR 連線識別碼。</param>
    public void Unregister(string connectionId) => _contexts.TryRemove(connectionId, out _);

    /// <summary>主動斷開連線；連線已不存在時什麼都不做。</summary>
    /// <param name="connectionId">SignalR 連線識別碼。</param>
    public void Abort(string connectionId)
    {
        if (_contexts.TryGetValue(connectionId, out var context)) context.Abort();
    }
}

/// <summary>
/// 每 5 秒處理閒置、離線與離開辦公室（寬限期後）並廣播；每 <see cref="QuoteOptions.OfficeAuthSweepSeconds"/> 秒比對一次
/// AuthVersion、帳號、成員身分與公司狀態。帳號、成員身分或公司停用的連線送 ForceDisconnect（前端登出）；
/// 只有 AuthVersion 變動的連線送 ReauthRequired（前端換發 token 後重連），兩者都接著斷線。
/// </summary>
/// <remarks>AuthVersion 巡檢把所有連線合成一次查詢；轉離線時依公司分批寫入 office_profiles.LastSeenAt。</remarks>
public sealed class OfficeSweeper(
    PresenceTracker tracker,
    OfficeConnections connections,
    IHubContext<OfficeHub, IOfficeClient> hub,
    IServiceScopeFactory scopes,
    IOptions<QuoteOptions> options,
    ILogger<OfficeSweeper> logger) : BackgroundService
{
    // IHubContext 送出的訊息先進連線的寫入佇列，Abort 太快會讓最後一則事件來不及送達。
    private static readonly TimeSpan AbortDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>以 5 秒為週期執行巡檢，單次失敗只記錄不中止。</summary>
    /// <param name="stoppingToken">主機關閉時取消。</param>
    /// <returns>巡檢迴圈的工作。</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var authInterval = TimeSpan.FromSeconds(Math.Max(5, options.Value.OfficeAuthSweepSeconds));
        var lastAuthSweep = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepPresenceAsync(stoppingToken);
                if (DateTimeOffset.UtcNow - lastAuthSweep >= authInterval)
                {
                    lastAuthSweep = DateTimeOffset.UtcNow;
                    await SweepAuthAsync(stoppingToken);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "虛擬辦公室巡檢失敗。");
            }
        }
    }

    private async Task SweepPresenceAsync(CancellationToken cancellationToken)
    {
        var events = tracker.Sweep();
        foreach (var change in events)
        {
            switch (change)
            {
                case OccupantLeftEvent left:
                    await hub.Clients.Group(OfficeGroups.Office(left.CompanyId, left.DepartmentId)).OccupantLeft(left.MembershipId);
                    break;
                case PresenceChangedEvent changed:
                    await hub.Clients.Group(OfficeGroups.Company(changed.CompanyId)).PresenceChanged(changed.Entry, changed.LastSeenAt);
                    break;
            }
        }
        foreach (var company in events.OfType<PresenceChangedEvent>().Where(value => value.Entry.Presence == "offline" && value.LastSeenAt is not null).GroupBy(value => value.CompanyId))
        {
            using var scope = scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<OfficeService>();
            service.UseCompany(company.Key);
            await service.WriteLastSeenAsync(company.Select(value => (value.Entry.MembershipId, value.LastSeenAt!.Value)).ToList(), cancellationToken);
        }
    }

    private async Task SweepAuthAsync(CancellationToken cancellationToken)
    {
        var all = tracker.AllConnections();
        if (all.Count == 0) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuoteDbContext>();
        var userIds = all.Values.Select(value => value.UserId).Distinct().ToList();
        var companyIds = all.Values.Select(value => value.CompanyId).Distinct().ToList();
        var membershipIds = all.Values.Select(value => value.MembershipId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(value => userIds.Contains(value.Id))
            .Select(value => new { value.Id, value.Status, value.AuthVersion })
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var activeCompanies = (await db.Companies.AsNoTracking()
            .Where(value => companyIds.Contains(value.Id) && value.Status == "active")
            .Select(value => value.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        // Seeder／背景工作沒有公司範圍，成員身分表要略過公司過濾才查得到。
        var activeMemberships = (await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(value => membershipIds.Contains(value.Id) && value.IsActive)
            .Select(value => value.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var revoked = new List<string>();
        var stale = new List<string>();
        foreach (var (connectionId, identity) in all)
        {
            if (!users.TryGetValue(identity.UserId, out var user)
                || user.Status != "active"
                || !activeCompanies.Contains(identity.CompanyId)
                || !activeMemberships.Contains(identity.MembershipId))
            {
                revoked.Add(connectionId);
            }
            else if (user.AuthVersion != identity.AuthVersion)
            {
                stale.Add(connectionId);
            }
        }
        if (revoked.Count == 0 && stale.Count == 0) return;

        // 版本變更不等於登出：REST 的 401 會默默換發 token，所以這裡只要求前端換發後重連。
        foreach (var connectionId in revoked) await TrySendAsync(connectionId, client => client.ForceDisconnect("帳號或公司已停用，請重新登入。"));
        foreach (var connectionId in stale) await TrySendAsync(connectionId, client => client.ReauthRequired());
        await Task.Delay(AbortDelay, cancellationToken);
        foreach (var connectionId in revoked.Concat(stale)) connections.Abort(connectionId);
    }

    private async Task TrySendAsync(string connectionId, Func<IOfficeClient, Task> send)
    {
        try
        {
            await send(hub.Clients.Client(connectionId));
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "通知連線 {ConnectionId} 失敗，仍會斷線。", connectionId);
        }
    }
}
