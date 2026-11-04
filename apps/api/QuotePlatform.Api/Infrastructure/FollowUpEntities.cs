namespace QuotePlatform.Api.Infrastructure;

/// <summary>專案跟進紀錄：業務與客戶的一次互動（電話、拜訪、會議等），可附下次聯絡日。</summary>
/// <remarks>
/// 可見範圍完全繼承所屬專案。專案的 <see cref="Project.LastFollowUpAt"/>、<see cref="Project.NextFollowUpOn"/>
/// 由 FollowUpService 依「最新一筆」重算，其他程式不得直接寫入。
/// </remarks>
public sealed class ProjectFollowUp : ICompanyOwned, ISoftDelete
{
    /// <summary>跟進紀錄唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬專案識別碼。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>類型（參考 <see cref="FollowUpTypes"/>）。</summary>
    public string Type { get; set; } = FollowUpTypes.Call;

    /// <summary>互動發生時間（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>對象聯絡人；必須屬於專案的客戶，聯絡人刪除時設為 null。</summary>
    public Guid? ContactId { get; set; }

    /// <summary>對象姓名快照；選了聯絡人時為聯絡人姓名。</summary>
    public string? ContactName { get; set; }

    /// <summary>主旨。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>內容。</summary>
    public string? Content { get; set; }

    /// <summary>下次聯絡日（台灣日期）。</summary>
    public DateOnly? NextFollowUpOn { get; set; }

    /// <summary>下次要做的事；沒有下次聯絡日時為 null。</summary>
    public string? NextAction { get; set; }

    /// <summary>按「不用跟進」的時間（UTC）；null 表示仍需提醒。</summary>
    public DateTimeOffset? NextDismissedAt { get; set; }

    /// <summary>按「不用跟進」的使用者識別碼。</summary>
    public Guid? NextDismissedBy { get; set; }

    /// <summary>撰寫者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後修改者使用者識別碼；沒有修改過為 null。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>是否已軟刪除。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>軟刪除時間戳記（UTC）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>跟進紀錄類型代碼。</summary>
public static class FollowUpTypes
{
    /// <summary>電話。</summary>
    public const string Call = "call";

    /// <summary>拜訪客戶。</summary>
    public const string Visit = "visit";

    /// <summary>會議（含視訊會議）。</summary>
    public const string Meeting = "meeting";

    /// <summary>Email。</summary>
    public const string Email = "email";

    /// <summary>即時訊息（LINE 等）。</summary>
    public const string Message = "message";

    /// <summary>其他。</summary>
    public const string Other = "other";

    /// <summary>全部類型，順序即畫面上的按鈕順序。</summary>
    public static readonly IReadOnlyList<string> All = [Call, Visit, Meeting, Email, Message, Other];
}
