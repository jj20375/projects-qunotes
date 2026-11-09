namespace QuotePlatform.Api.Infrastructure;

/// <summary>站內通知：寫給單一使用者的一則提醒，目前只有「跟進到期」一種。</summary>
/// <remarks>
/// 通知只存「誰、哪個專案、哪一天到期」，不存專案名稱或內容：顯示時才依收件人目前看得到的專案組出文字，
/// 專案被移出收件人的資料範圍後通知就不再出現，也不會洩漏專案名稱。
/// (<see cref="UserId"/>, <see cref="Type"/>, <see cref="ProjectId"/>, <see cref="DueOn"/>) 唯一，
/// 同一個到期日只通知一次；下次聯絡日改期後是新的到期日，會有新的通知。
/// </remarks>
public sealed class Notification : ICompanyOwned
{
    /// <summary>通知唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>收件人使用者識別碼。</summary>
    public Guid UserId { get; set; }

    /// <summary>通知類型（參考 <see cref="NotificationTypes"/>）。</summary>
    public string Type { get; set; } = NotificationTypes.FollowUpDue;

    /// <summary>相關專案識別碼。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>觸發通知的下次聯絡日（台灣日期）。</summary>
    public DateOnly DueOn { get; set; }

    /// <summary>通知產生時間（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>收件人標示已讀的時間（UTC）；未讀為 null。</summary>
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>通知類型代碼。</summary>
public static class NotificationTypes
{
    /// <summary>跟進到期：專案的下次聯絡日已到或已逾期。</summary>
    public const string FollowUpDue = "followup_due";

    /// <summary>所有合法的通知類型。</summary>
    public static readonly IReadOnlyList<string> All = [FollowUpDue];
}
