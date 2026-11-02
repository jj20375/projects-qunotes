namespace QuotePlatform.Api.Infrastructure;

/// <summary>軟刪除的業務主檔；Global Query Filter 會同時排除已刪除資料。</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>客戶（報價對象，由美達特的商家機制轉化）；不登入系統。</summary>
public sealed class Client : ICompanyOwned, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? TaxId { get; set; }
    /// <summary>集團／分公司關係，可選。</summary>
    public Guid? ParentClientId { get; set; }
    public string? Industry { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? InvoiceTitle { get; set; }
    public string? InvoiceAddress { get; set; }
    /// <summary>付款條件，例如月結 30 天；報價時帶入。</summary>
    public string? PaymentTerms { get; set; }
    /// <summary>業務負責人；決定非全公司範圍的人能否修改此客戶。</summary>
    public Guid? OwnerUserId { get; set; }
    /// <summary>active／inactive。</summary>
    public string Status { get; set; } = "active";
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>客戶聯絡人；<see cref="IsPrimary"/> 為報價預設帶入對象。</summary>
public sealed class ClientContact : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>專案：報價、訂單、保固、發票都掛在專案底下並繼承其可見性。</summary>
public sealed class Project : ICompanyOwned, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string ProjectNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public Guid? DepartmentId { get; set; }
    public Guid OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;
    public string Status { get; set; } = ProjectStatuses.Draft;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal? ExpectedAmount { get; set; }
    /// <summary>成交未稅；第三階段起由接受的報價回寫。</summary>
    public decimal? ContractAmount { get; set; }
    /// <summary>成交含稅。</summary>
    public decimal? ContractTotal { get; set; }
    public string Currency { get; set; } = "TWD";
    public string? Description { get; set; }
    public DateTimeOffset? WonAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<ProjectMember> Members { get; set; } = [];
}

/// <summary>專案成員；viewer 只能讀，即使角色有修改權限。</summary>
public sealed class ProjectMember : ICompanyOwned
{
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid CompanyId { get; set; }
    /// <summary>manager／member／viewer。</summary>
    public string ProjectRole { get; set; } = ProjectRoles.Member;
    public Guid AddedBy { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>專案狀態異動紀錄。</summary>
public sealed class ProjectStatusHistory : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>單號序號；以 (CompanyId, DocType, Period) 為主鍵，由 <see cref="NumberSequenceService"/> 原子遞增。</summary>
public sealed class NumberSequence : ICompanyOwned
{
    public Guid CompanyId { get; set; }
    public string DocType { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public long CurrentValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>專案狀態與允許的轉換。</summary>
public static class ProjectStatuses
{
    public const string Draft = "draft";
    public const string Quoting = "quoting";
    public const string Won = "won";
    public const string Lost = "lost";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Closed = "closed";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = [Draft, Quoting, Won, Lost, InProgress, Completed, Closed, Cancelled];

    /// <summary>
    /// 狀態流程：草稿 → 報價中 → 得標／未得標 → 執行中 → 已完工 → 已結案；未得標可重新報價，已取消可回到草稿。
    /// 進入 closed 另需 <c>projects:close</c> 子功能。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>
    {
        [Draft] = [Quoting, Cancelled],
        [Quoting] = [Won, Lost, Draft, Cancelled],
        [Won] = [InProgress, Cancelled],
        [Lost] = [Quoting, Closed],
        [InProgress] = [Completed, Cancelled],
        [Completed] = [Closed, InProgress],
        [Closed] = [],
        [Cancelled] = [Draft],
    };

    public static bool CanTransition(string from, string to) => Transitions.TryGetValue(from, out var next) && next.Contains(to);
}

public static class ProjectRoles
{
    public const string Manager = "manager";
    public const string Member = "member";
    public const string Viewer = "viewer";
    public static readonly string[] All = [Manager, Member, Viewer];
}

/// <summary>業務日期以台灣時間（UTC+8）切日；搜尋「建立日期」等 UTC 時間欄位時用來換算區間。</summary>
public static class BusinessTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);
    public static DateTimeOffset StartOfDay(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), Offset);
    public static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Offset).DateTime);
}
