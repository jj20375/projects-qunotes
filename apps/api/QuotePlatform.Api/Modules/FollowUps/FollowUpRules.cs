using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.FollowUps;

/// <summary>跟進紀錄的純規則：輸入檢查、台灣日期換算與提醒範圍；不碰資料庫，供 <c>--self-test</c> 驗證。</summary>
public static class FollowUpRules
{
    /// <summary>主旨長度上限。</summary>
    public const int SummaryMaxLength = 200;

    /// <summary>內容長度上限。</summary>
    public const int ContentMaxLength = 4000;

    /// <summary>對象姓名長度上限。</summary>
    public const int ContactNameMaxLength = 100;

    /// <summary>下次要做的事長度上限。</summary>
    public const int NextActionMaxLength = 200;

    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>專案列表 <c>followUp</c> 篩選可用的值：pending 待跟進、overdue 已逾期、none 沒有下次聯絡。</summary>
    public static readonly IReadOnlyList<string> FilterValues = ["pending", "overdue", "none"];

    /// <summary>不算「待跟進」的專案狀態：未得標、已結案、已取消（日期保留，專案重新啟動後會再出現）。</summary>
    public static readonly IReadOnlyList<string> InactiveProjectStatuses = [ProjectStatuses.Lost, ProjectStatuses.Closed, ProjectStatuses.Cancelled];

    /// <summary>
    /// 檢查跟進紀錄輸入。
    /// </summary>
    /// <param name="input">輸入內容。</param>
    /// <param name="now">目前時間。</param>
    /// <returns>錯誤訊息；通過時為 null。</returns>
    public static string? Validate(FollowUpInput input, DateTimeOffset now)
    {
        if (input.Type is null || !FollowUpTypes.All.Contains(input.Type)) return "請選擇跟進類型。";
        if (input.OccurredAt is not DateTimeOffset occurredAt) return "請輸入發生時間。";
        // 容許使用者電腦與伺服器時鐘的小誤差，避免剛按下「現在」就被判定為未來時間。
        if (occurredAt > now + ClockSkew) return "發生時間不可晚於現在。";
        if (string.IsNullOrWhiteSpace(input.Summary)) return "請輸入主旨。";
        if (input.Summary.Trim().Length > SummaryMaxLength) return $"主旨不可超過 {SummaryMaxLength} 字。";
        if (input.Content?.Trim().Length > ContentMaxLength) return $"內容不可超過 {ContentMaxLength} 字。";
        if (input.ContactName?.Trim().Length > ContactNameMaxLength) return $"對象姓名不可超過 {ContactNameMaxLength} 字。";
        if (input.NextAction?.Trim().Length > NextActionMaxLength) return $"下次要做的事不可超過 {NextActionMaxLength} 字。";
        if (input.NextFollowUpOn is DateOnly next && next < TaiwanDate(occurredAt)) return "下次聯絡日不可早於發生日期。";
        return null;
    }

    /// <summary>
    /// 去除前後空白，空字串轉成 null。
    /// </summary>
    /// <param name="value">原始字串。</param>
    /// <returns>整理後的字串或 null。</returns>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 換算台灣日期。
    /// </summary>
    /// <param name="value">時間。</param>
    /// <returns>該時間在台灣的日期。</returns>
    public static DateOnly TaiwanDate(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(BusinessTime.Offset).DateTime);

    /// <summary>供 <c>--self-test</c> 驗證輸入檢查與台灣日期換算。</summary>
    public static void AssertExamples()
    {
        var now = new DateTimeOffset(2026, 11, 4, 2, 0, 0, TimeSpan.Zero);
        FollowUpInput Valid() => new("call", now.AddHours(-1), null, null, "確認規格", null, null, null);

        Check(Validate(Valid(), now) is null, "合法輸入應通過");
        Check(Validate(Valid() with { Type = "fax" }, now) == "請選擇跟進類型。", "不支援的類型");
        Check(Validate(Valid() with { Type = null }, now) == "請選擇跟進類型。", "沒有類型");
        Check(Validate(Valid() with { OccurredAt = null }, now) == "請輸入發生時間。", "沒有發生時間");
        Check(Validate(Valid() with { OccurredAt = now.AddMinutes(4) }, now) is null, "容許 5 分鐘時鐘誤差");
        Check(Validate(Valid() with { OccurredAt = now.AddMinutes(6) }, now) == "發生時間不可晚於現在。", "未來時間");
        Check(Validate(Valid() with { Summary = "  " }, now) == "請輸入主旨。", "空白主旨");
        Check(Validate(Valid() with { Summary = new string('字', 201) }, now) == "主旨不可超過 200 字。", "主旨過長");
        Check(Validate(Valid() with { Content = new string('字', 4001) }, now) == "內容不可超過 4000 字。", "內容過長");
        Check(Validate(Valid() with { ContactName = new string('字', 101) }, now) == "對象姓名不可超過 100 字。", "姓名過長");
        Check(Validate(Valid() with { NextAction = new string('字', 201) }, now) == "下次要做的事不可超過 200 字。", "下次要做的事過長");

        // UTC 11/3 17:00 是台灣 11/4 01:00：下次聯絡日以台灣日期比較，11/3 要擋下、11/4 可以。
        var lateNight = new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.Zero);
        Check(TaiwanDate(lateNight) == new DateOnly(2026, 11, 4), "台灣日期換算");
        Check(Validate(Valid() with { OccurredAt = lateNight, NextFollowUpOn = new DateOnly(2026, 11, 3) }, now) == "下次聯絡日不可早於發生日期。", "下次聯絡早於發生日");
        Check(Validate(Valid() with { OccurredAt = lateNight, NextFollowUpOn = new DateOnly(2026, 11, 4) }, now) is null, "下次聯絡同一天可以");
        Check(Clean("  ") is null && Clean(" 甲 ") == "甲", "Clean");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"FollowUpRules：{message}");
    }
}

/// <summary>跟進紀錄查詢的共用排序。</summary>
public static class FollowUpQueryExtensions
{
    /// <summary>
    /// 「最新一筆」的排序：發生時間新到舊，同時間再比建立時間。
    /// </summary>
    /// <param name="rows">跟進紀錄查詢。</param>
    /// <returns>排序後的查詢。</returns>
    /// <remarks>補登一筆較早的拜訪不會變成最新一筆。重算專案狀態、取下次要做的事與時間軸都必須用這個排序，三者才會一致。</remarks>
    public static IOrderedQueryable<ProjectFollowUp> LatestFirst(this IQueryable<ProjectFollowUp> rows) =>
        rows.OrderByDescending(value => value.OccurredAt).ThenByDescending(value => value.CreatedAt);
}
