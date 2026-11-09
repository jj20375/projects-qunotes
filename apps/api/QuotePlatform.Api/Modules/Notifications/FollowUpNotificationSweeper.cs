using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Notifications;

/// <summary>
/// 定時為每家啟用中的公司產生跟進到期通知：啟動後立刻掃一次（補上停機期間錯過的），之後每
/// <see cref="QuoteOptions.FollowUpNotificationSweepSeconds"/> 秒一次，每天從 <see cref="QuoteOptions.FollowUpNotificationStartHour"/> 點起才會產生。
/// </summary>
/// <remarks>
/// 通知以 (收件人, 專案, 到期日) 去重，掃描頻率只影響「多久內收到」，不會造成重複通知。
/// 每家公司各用一個 DI scope，公司範圍由 <see cref="FollowUpNotificationGenerator"/> 明確指定，不會混到別家資料。
/// </remarks>
public sealed class FollowUpNotificationSweeper(IServiceScopeFactory scopes, IOptions<QuoteOptions> options, ILogger<FollowUpNotificationSweeper> logger) : BackgroundService
{
    /// <summary>啟動後先掃一次，之後依設定的間隔重複；單次失敗只記錄不中止。</summary>
    /// <param name="stoppingToken">主機關閉時取消。</param>
    /// <returns>掃描迴圈的工作。</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.FollowUpNotificationSweepSeconds)));
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            // 只有主機關閉才讓取消例外往外傳；其他來源的取消若中止迴圈，BackgroundService 預設會停掉整個 API。
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(error, "跟進到期通知掃描失敗。");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToOffset(BusinessTime.Offset);
        if (now.Hour < Math.Clamp(options.Value.FollowUpNotificationStartHour, 0, 23)) return;
        var today = DateOnly.FromDateTime(now.DateTime);

        List<Guid> companyIds;
        using (var scope = scopes.CreateScope())
        {
            companyIds = await scope.ServiceProvider.GetRequiredService<QuoteDbContext>().Companies.AsNoTracking()
                .Where(value => value.Status == "active").Select(value => value.Id).ToListAsync(cancellationToken);
        }
        foreach (var companyId in companyIds)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<FollowUpNotificationGenerator>().GenerateAsync(companyId, today, cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(error, "公司 {CompanyId} 的跟進到期通知產生失敗。", companyId);
            }
        }
    }
}
