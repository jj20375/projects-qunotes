using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Dashboard;

/// <summary>
/// Dashboard 戰情查詢條件契約；Year 為會計年度（以起始月所在的西元年命名）。
/// </summary>
/// <param name="Year">會計年度（西元年）。</param>
/// <param name="Metric">衡量口徑（accepted 成交報價、orders 訂單、invoiced 已開發票）。</param>
/// <param name="DepartmentId">鎖定部門 ID。</param>
/// <param name="OwnerUserId">鎖定特定負責業務 ID。</param>
public sealed record DashboardQuery(int? Year, string? Metric, Guid? DepartmentId, Guid? OwnerUserId);

/// <summary>單筆業績資料：日期（台灣時間）、未稅金額、專案負責人。</summary>
internal sealed record MetricRow(DateOnly Date, decimal Amount, Guid OwnerUserId);

/// <summary>
/// 戰情 Dashboard。所有數字都先經 <see cref="ProjectAccessService.VisibleProjects"/> 套用資料範圍，
/// 業務只會看到自己的業績、主管看到部門、全公司範圍看到全部。
/// </summary>
/// <remarks>
/// 業績口徑（皆為未稅金額）：
/// - accepted：客戶接受的報價，依接受時間（台灣時間）歸月。
/// - orders：訂單（排除已取消），依訂單日期。
/// - invoiced：開立中的發票（排除作廢），依發票日期。
/// 會計年度依 company_settings.FiscalYearStartMonth；季度為會計年度的第 1～4 季。
/// 目前在記憶體彙總一年＋去年的資料列；資料量變大時改用 monthly_sales_snapshots（見 docs/database-planning.md）。
/// 沒有 dashboard:view_amount 時金額一律為 null，只回傳件數；業務排行另需 dashboard:view_all_staff。
/// </remarks>
public sealed class DashboardService(QuoteDbContext db, ProjectAccessService access)
{
    public static readonly string[] Metrics = ["accepted", "orders", "invoiced"];

    public async Task<ServiceResult<object>> SummaryAsync(RequestScope scope, DashboardQuery query, CancellationToken cancellationToken)
    {
        var metric = query.Metric ?? "accepted";
        if (!Metrics.Contains(metric)) return ServiceResult<object>.BadRequest("業績口徑只能是 accepted、orders 或 invoiced。");
        var startMonth = await db.CompanySettings.AsNoTracking().Select(value => (int?)value.FiscalYearStartMonth).SingleOrDefaultAsync(cancellationToken) ?? 1;
        var today = BusinessTime.Today;
        var currentFiscalYear = today.Month >= startMonth ? today.Year : today.Year - 1;
        var year = query.Year ?? currentFiscalYear;
        if (year < 2000 || year > today.Year + 1) return ServiceResult<object>.BadRequest("年度不正確。");

        var start = new DateOnly(year, startMonth, 1);
        var end = start.AddYears(1);
        var previousStart = start.AddYears(-1);
        var canAmount = scope.HasOption("dashboard", "view_amount");
        var projects = await FilteredProjectsAsync(scope, query, cancellationToken);
        if (projects is null) return ServiceResult<object>.BadRequest("部門不在您的資料範圍內。");

        var rows = new Dictionary<string, List<MetricRow>>
        {
            ["accepted"] = await AcceptedRowsAsync(projects, previousStart, end, cancellationToken),
            ["orders"] = await OrderRowsAsync(projects, previousStart, end, cancellationToken),
            ["invoiced"] = await InvoiceRowsAsync(projects, previousStart, end, cancellationToken),
        };
        // 會計月序：第 0 個月＝起始月。
        int FiscalIndex(DateOnly date) => (date.Year - start.Year) * 12 + date.Month - start.Month;
        decimal[] Monthly(IEnumerable<MetricRow> source, DateOnly from)
        {
            var values = new decimal[12];
            foreach (var row in source.Where(value => value.Date >= from && value.Date < from.AddYears(1)))
            {
                values[(row.Date.Year - from.Year) * 12 + row.Date.Month - from.Month] += row.Amount;
            }
            return values;
        }
        decimal[] Quarters(decimal[] months) => [.. Enumerable.Range(0, 4).Select(quarter => months.Skip(quarter * 3).Take(3).Sum())];
        decimal? Money(decimal value) => canAmount ? value : null;
        decimal?[] MoneyArray(decimal[] values) => [.. values.Select(Money)];

        var selected = rows[metric];
        var thisYear = Monthly(selected, start);
        var lastYear = Monthly(selected, previousStart);
        var isCurrentYear = year == currentFiscalYear;
        var monthIndex = isCurrentYear ? FiscalIndex(today) : 11;
        var quarterIndex = monthIndex / 3;
        // 去年同期：只比到今年目前的月份，避免拿未過完的今年和整年的去年比。
        var yearToDate = thisYear.Take(monthIndex + 1).Sum();
        var lastYearToDate = lastYear.Take(monthIndex + 1).Sum();

        var totals = Metrics.ToDictionary(key => key, key => Money(rows[key].Where(value => value.Date >= start && value.Date < end).Sum(value => value.Amount)));
        var projectStats = await ProjectStatsAsync(projects, start, end, cancellationToken);

        return ServiceResult<object>.Ok(new
        {
            fiscalYear = year,
            fiscalYearStartMonth = startMonth,
            isCurrentYear,
            metric,
            canViewAmount = canAmount,
            monthLabels = Enumerable.Range(0, 12).Select(index => start.AddMonths(index).ToString("yyyy-MM")),
            currentMonthIndex = isCurrentYear ? monthIndex : (int?)null,
            trend = new { thisYear = MoneyArray(thisYear), lastYear = MoneyArray(lastYear) },
            quarters = new { thisYear = MoneyArray(Quarters(thisYear)), lastYear = MoneyArray(Quarters(lastYear)) },
            kpi = new
            {
                yearTotal = Money(thisYear.Sum()),
                yearToDate = Money(yearToDate),
                lastYearToDate = Money(lastYearToDate),
                growthRate = canAmount && lastYearToDate != 0 ? Math.Round((yearToDate - lastYearToDate) / lastYearToDate * 100, 1) : (decimal?)null,
                currentQuarter = isCurrentYear ? Money(Quarters(thisYear)[quarterIndex]) : null,
                currentQuarterNo = isCurrentYear ? quarterIndex + 1 : (int?)null,
                currentMonth = isCurrentYear ? Money(thisYear[monthIndex]) : null,
            },
            totals,
            projects = projectStats,
            ranking = scope.HasOption("dashboard", "view_all_staff") ? await RankingAsync(selected, start, end, canAmount, cancellationToken) : null,
            expiringWarranties = scope.Has("warranties", "read") ? await ExpiringWarrantiesAsync(projects, today, cancellationToken) : null,
            receivables = scope.Has("invoices", "read") ? await ReceivablesAsync(projects, today, canAmount, cancellationToken) : null,
        });
    }

    /// <summary>可見專案再套部門（含下層）與負責人篩選；指定的部門不在可見範圍時回傳 null。</summary>
    private async Task<IQueryable<Project>?> FilteredProjectsAsync(RequestScope scope, DashboardQuery query, CancellationToken cancellationToken)
    {
        var projects = access.VisibleProjects(scope).AsNoTracking();
        if (query.DepartmentId is Guid departmentId)
        {
            if (!await db.Departments.AnyAsync(value => value.Id == departmentId, cancellationToken)) return null;
            projects = projects.Where(value => db.DepartmentClosures.Any(path => path.AncestorId == departmentId && path.DescendantId == value.DepartmentId));
        }
        if (query.OwnerUserId is Guid ownerUserId) projects = projects.Where(value => value.OwnerUserId == ownerUserId);
        return projects;
    }

    private async Task<List<MetricRow>> AcceptedRowsAsync(IQueryable<Project> projects, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var fromAt = BusinessTime.StartOfDay(from);
        var toAt = BusinessTime.StartOfDay(to);
        var rows = await db.Quotes.AsNoTracking()
            .Where(value => value.Status == QuoteStatuses.Accepted && value.AcceptedAt >= fromAt && value.AcceptedAt < toAt)
            .Join(projects, quote => quote.ProjectId, project => project.Id, (quote, project) => new { quote.AcceptedAt, quote.NetAmount, project.OwnerUserId })
            .ToListAsync(cancellationToken);
        return rows.Select(value => new MetricRow(DateOnly.FromDateTime(value.AcceptedAt!.Value.ToOffset(BusinessTime.Offset).DateTime), value.NetAmount, value.OwnerUserId)).ToList();
    }

    private Task<List<MetricRow>> OrderRowsAsync(IQueryable<Project> projects, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        db.ProjectOrders.AsNoTracking()
            .Where(value => value.Status != "cancelled" && value.OrderDate >= from && value.OrderDate < to)
            .Join(projects, order => order.ProjectId, project => project.Id, (order, project) => new MetricRow(order.OrderDate, order.Amount, project.OwnerUserId))
            .ToListAsync(cancellationToken);

    private Task<List<MetricRow>> InvoiceRowsAsync(IQueryable<Project> projects, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        db.ProjectInvoices.AsNoTracking()
            .Where(value => value.Status == "issued" && value.InvoiceDate >= from && value.InvoiceDate < to)
            .Join(projects, invoice => invoice.ProjectId, project => project.Id, (invoice, project) => new MetricRow(invoice.InvoiceDate, invoice.SalesAmount, project.OwnerUserId))
            .ToListAsync(cancellationToken);

    /// <summary>案件數：目前各狀態（不分年度）、本年度新增、得標與未得標件數與成交率。</summary>
    private async Task<object> ProjectStatsAsync(IQueryable<Project> projects, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var fromAt = BusinessTime.StartOfDay(start);
        var toAt = BusinessTime.StartOfDay(end);
        var statusCounts = await projects.GroupBy(value => value.Status).Select(group => new { Status = group.Key, Count = group.Count() }).ToDictionaryAsync(value => value.Status, value => value.Count, cancellationToken);
        var created = await projects.Where(value => value.CreatedAt >= fromAt && value.CreatedAt < toAt).Select(value => value.CreatedAt).ToListAsync(cancellationToken);
        var createdMonthly = new int[12];
        foreach (var createdAt in created)
        {
            var date = DateOnly.FromDateTime(createdAt.ToOffset(BusinessTime.Offset).DateTime);
            createdMonthly[(date.Year - start.Year) * 12 + date.Month - start.Month]++;
        }
        var projectIds = projects.Select(value => value.Id);
        var won = await projects.CountAsync(value => value.WonAt >= fromAt && value.WonAt < toAt, cancellationToken);
        // 未得標以狀態紀錄判斷（同一專案多次未得標只算一次）。
        var lost = await db.ProjectStatusHistories.AsNoTracking()
            .Where(value => value.ToStatus == ProjectStatuses.Lost && value.ChangedAt >= fromAt && value.ChangedAt < toAt && projectIds.Contains(value.ProjectId))
            .Select(value => value.ProjectId).Distinct().CountAsync(cancellationToken);
        return new
        {
            statusCounts,
            active = statusCounts.Where(value => value.Key is ProjectStatuses.Draft or ProjectStatuses.Quoting or ProjectStatuses.Won or ProjectStatuses.InProgress).Sum(value => value.Value),
            createdThisYear = created.Count,
            createdMonthly,
            won,
            lost,
            winRate = won + lost == 0 ? (decimal?)null : Math.Round((decimal)won / (won + lost) * 100, 1),
        };
    }

    private async Task<object> RankingAsync(List<MetricRow> rows, DateOnly start, DateOnly end, bool canAmount, CancellationToken cancellationToken)
    {
        var grouped = rows.Where(value => value.Date >= start && value.Date < end)
            .GroupBy(value => value.OwnerUserId)
            .Select(group => new { OwnerUserId = group.Key, Amount = group.Sum(value => value.Amount), Count = group.Count() })
            .OrderByDescending(value => value.Amount)
            .Take(10)
            .ToList();
        var ids = grouped.Select(value => value.OwnerUserId).ToList();
        var names = await db.Users.AsNoTracking().Where(value => ids.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.DisplayName, cancellationToken);
        return grouped.Select(value => new { value.OwnerUserId, name = names.GetValueOrDefault(value.OwnerUserId) ?? "—", amount = canAmount ? value.Amount : (decimal?)null, value.Count });
    }

    private async Task<object> ExpiringWarrantiesAsync(IQueryable<Project> projects, DateOnly today, CancellationToken cancellationToken)
    {
        var until = today.AddDays(90);
        var query = db.ProjectWarranties.AsNoTracking()
            .Where(value => value.EndDate >= today && value.EndDate <= until)
            .Join(projects, warranty => warranty.ProjectId, project => project.Id, (warranty, project) => new { warranty, project });
        return new
        {
            count = await query.CountAsync(cancellationToken),
            items = await query.OrderBy(value => value.warranty.EndDate).Take(10)
                .Select(value => new { value.warranty.Id, value.warranty.WarrantyNo, value.warranty.Subject, value.warranty.EndDate, projectId = value.project.Id, value.project.ProjectNo, projectName = value.project.Name, clientName = value.project.Client.Name })
                .ToListAsync(cancellationToken),
        };
    }

    private async Task<object> ReceivablesAsync(IQueryable<Project> projects, DateOnly today, bool canAmount, CancellationToken cancellationToken)
    {
        var query = db.ProjectInvoices.AsNoTracking()
            .Where(value => value.Status == "issued" && value.PaymentStatus != "paid")
            .Join(projects, invoice => invoice.ProjectId, project => project.Id, (invoice, project) => new { invoice, project });
        var all = await query.Select(value => new { Outstanding = value.invoice.TotalAmount - value.invoice.PaidAmount, value.invoice.DueDate }).ToListAsync(cancellationToken);
        var items = await query
            .OrderBy(value => value.invoice.DueDate == null).ThenBy(value => value.invoice.DueDate)
            .Take(10)
            .Select(value => new { value.invoice.Id, value.invoice.InvoiceNo, value.invoice.DueDate, value.invoice.TotalAmount, value.invoice.PaidAmount, projectId = value.project.Id, value.project.ProjectNo, projectName = value.project.Name, clientName = value.project.Client.Name })
            .ToListAsync(cancellationToken);
        return new
        {
            count = all.Count,
            overdueCount = all.Count(value => value.DueDate < today),
            outstanding = canAmount ? all.Sum(value => value.Outstanding) : (decimal?)null,
            overdue = canAmount ? all.Where(value => value.DueDate < today).Sum(value => value.Outstanding) : (decimal?)null,
            items = items.Select(value => new
            {
                value.Id,
                value.InvoiceNo,
                value.DueDate,
                outstanding = canAmount ? value.TotalAmount - value.PaidAmount : (decimal?)null,
                isOverdue = value.DueDate < today,
                value.projectId,
                value.ProjectNo,
                value.projectName,
                value.clientName,
            }),
        };
    }
}
