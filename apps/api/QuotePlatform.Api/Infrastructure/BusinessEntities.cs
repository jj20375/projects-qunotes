namespace QuotePlatform.Api.Infrastructure;

/// <summary>軟刪除的業務主檔；Global Query Filter 會同時排除已刪除資料。</summary>
public interface ISoftDelete
{
    /// <summary>是否已軟刪除。</summary>
    bool IsDeleted { get; set; }

    /// <summary>軟刪除時間戳記；null 表示有效未刪除。</summary>
    DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>客戶（報價對象，由美達特的商家機制轉化）；不登入系統。</summary>
public sealed class Client : ICompanyOwned, ISoftDelete
{
    /// <summary>客戶唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>客戶代碼；在公司內不可重複。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>客戶名稱。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>客戶簡稱。</summary>
    public string? ShortName { get; set; }

    /// <summary>統一編號。</summary>
    public string? TaxId { get; set; }

    /// <summary>集團／分公司關係，指向母公司識別碼，可選。</summary>
    public Guid? ParentClientId { get; set; }

    /// <summary>產業類別。</summary>
    public string? Industry { get; set; }

    /// <summary>代表電話。</summary>
    public string? Phone { get; set; }

    /// <summary>傳真號碼。</summary>
    public string? Fax { get; set; }

    /// <summary>電子郵件信箱。</summary>
    public string? Email { get; set; }

    /// <summary>公司登記地址或通訊地址。</summary>
    public string? Address { get; set; }

    /// <summary>發票抬頭。</summary>
    public string? InvoiceTitle { get; set; }

    /// <summary>發票寄送地址。</summary>
    public string? InvoiceAddress { get; set; }

    /// <summary>付款條件，例如月結 30 天；報價時帶入。</summary>
    public string? PaymentTerms { get; set; }

    /// <summary>業務負責人識別碼；決定非全公司範圍的人能否修改此客戶。</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// 客戶所屬部門識別碼；決定誰看得到這個客戶（本人與部門範圍看自己部門，部門樹範圍含下層部門，全公司範圍全部）。
    /// 建立時預設為建立者的部門，只有全公司範圍的管理者可以移動。
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>客戶狀態：active（使用中）／inactive（停用）。</summary>
    public string Status { get; set; } = "active";

    /// <summary>備註說明。</summary>
    public string? Note { get; set; }

    /// <summary>建立者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新者使用者識別碼。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>是否已軟刪除。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>軟刪除時間戳記（UTC）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>客戶聯絡人；<see cref="IsPrimary"/> 為報價預設帶入對象。</summary>
public sealed class ClientContact : ICompanyOwned
{
    /// <summary>聯絡人唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬客戶識別碼。</summary>
    public Guid ClientId { get; set; }

    /// <summary>聯絡人姓名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>職稱。</summary>
    public string? Title { get; set; }

    /// <summary>公司聯絡電話／分機。</summary>
    public string? Phone { get; set; }

    /// <summary>行動電話號碼。</summary>
    public string? Mobile { get; set; }

    /// <summary>電子郵件信箱。</summary>
    public string? Email { get; set; }

    /// <summary>是否為主要聯絡人；同一客戶報價時預設帶入主要聯絡人資訊。</summary>
    public bool IsPrimary { get; set; }

    /// <summary>排序順序編號。</summary>
    public int SortOrder { get; set; }

    /// <summary>備註說明。</summary>
    public string? Note { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>專案：報價、訂單、保固、發票都掛在專案底下並繼承其可見性。</summary>
public sealed class Project : ICompanyOwned, ISoftDelete
{
    /// <summary>專案唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>專案編號；依取號規則自動編配。</summary>
    public string ProjectNo { get; set; } = string.Empty;

    /// <summary>專案名稱。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>客戶識別碼。</summary>
    public Guid ClientId { get; set; }

    /// <summary>關聯客戶主檔實體。</summary>
    public Client Client { get; set; } = null!;

    /// <summary>承辦／歸屬部門識別碼。</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>專案負責人使用者識別碼。</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>專案負責人使用者實體。</summary>
    public User Owner { get; set; } = null!;

    /// <summary>專案狀態；定義於 <see cref="ProjectStatuses"/>。</summary>
    public string Status { get; set; } = ProjectStatuses.Draft;

    /// <summary>專案預計或實際開始日期。</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>專案預計或實際結束日期。</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>預估專案金額。</summary>
    public decimal? ExpectedAmount { get; set; }

    /// <summary>成交未稅金額；由已接受或得標之報價回寫。</summary>
    public decimal? ContractAmount { get; set; }

    /// <summary>成交含稅總金額。</summary>
    public decimal? ContractTotal { get; set; }

    /// <summary>計價幣別，預設 TWD。</summary>
    public string Currency { get; set; } = "TWD";

    /// <summary>專案說明或目標描述。</summary>
    public string? Description { get; set; }

    /// <summary>專案成交／得標確認時間戳記。</summary>
    public DateTimeOffset? WonAt { get; set; }

    /// <summary>專案結案確認時間戳記。</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>最新一筆跟進紀錄的發生時間（UTC）；只由 FollowUpService 重算寫入。</summary>
    public DateTimeOffset? LastFollowUpAt { get; set; }

    /// <summary>下次聯絡日（台灣日期）；最新一筆沒填或已按「不用跟進」時為 null。只由 FollowUpService 重算寫入。</summary>
    public DateOnly? NextFollowUpOn { get; set; }

    /// <summary>建立者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新者使用者識別碼。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>是否已軟刪除。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>軟刪除時間戳記（UTC）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>專案協同參與成員清單。</summary>
    public List<ProjectMember> Members { get; set; } = [];
}

/// <summary>專案成員；viewer 只能讀，即使角色有修改權限。</summary>
public sealed class ProjectMember : ICompanyOwned
{
    /// <summary>所屬專案識別碼。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>成員使用者識別碼。</summary>
    public Guid UserId { get; set; }

    /// <summary>成員使用者實體。</summary>
    public User User { get; set; } = null!;

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>專案角色（manager／member／viewer）；定義於 <see cref="ProjectRoles"/>。</summary>
    public string ProjectRole { get; set; } = ProjectRoles.Member;

    /// <summary>加入操作者使用者識別碼。</summary>
    public Guid AddedBy { get; set; }

    /// <summary>加入時間戳記（UTC）。</summary>
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>專案狀態異動紀錄。</summary>
public sealed class ProjectStatusHistory : ICompanyOwned
{
    /// <summary>紀錄唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬專案識別碼。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>變更前狀態；null 表示初始建立。</summary>
    public string? FromStatus { get; set; }

    /// <summary>變更後新狀態。</summary>
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>狀態異動原因說明。</summary>
    public string? Reason { get; set; }

    /// <summary>變更操作者使用者識別碼。</summary>
    public Guid ChangedBy { get; set; }

    /// <summary>變更時間戳記（UTC）。</summary>
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>單號序號；以 (CompanyId, DocType, Period) 為主鍵，由 <see cref="NumberSequenceService"/> 原子遞增。</summary>
public sealed class NumberSequence : ICompanyOwned
{
    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>單據類型識別碼（如 quote、project、order、shipment、invoice）。</summary>
    public string DocType { get; set; } = string.Empty;

    /// <summary>序號週期字串（例如西元年 yyyy 或年月 yyyyMM）。</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>單號前綴代碼。</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>當前最大流水序號值。</summary>
    public long CurrentValue { get; set; }

    /// <summary>最後更新取號時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>專案狀態與允許的轉換流程。</summary>
public static class ProjectStatuses
{
    /// <summary>草稿狀態：初期規劃中，尚未正式向客戶報價。</summary>
    public const string Draft = "draft";

    /// <summary>報價中狀態：已開立並向客戶提出報價單，等待回應。</summary>
    public const string Quoting = "quoting";

    /// <summary>得標／成案狀態：客戶接受報價或正式得標，進入執行階段。</summary>
    public const string Won = "won";

    /// <summary>未得標／失案狀態：客戶婉拒報價或未得標；可重新啟動報價。</summary>
    public const string Lost = "lost";

    /// <summary>執行中狀態：得標後進行採購、備料、出貨等履約作業。</summary>
    public const string InProgress = "in_progress";

    /// <summary>已完工狀態：所有訂單與履約交付項目完成驗收。</summary>
    public const string Completed = "completed";

    /// <summary>已結案狀態：帳款已結清、保固啟動，完成專案終止閉環。</summary>
    public const string Closed = "closed";

    /// <summary>已取消狀態：專案因故廢止或放棄，可退回草稿重新評估。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>所有定義的專案狀態清單。</summary>
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

    /// <summary>
    /// 檢查專案是否允許從指定來源狀態轉換至目標狀態。
    /// </summary>
    /// <param name="from">來源狀態。</param>
    /// <param name="to">目標狀態。</param>
    /// <returns>若符合狀態機流程則回傳 true；否則為 false。</returns>
    public static bool CanTransition(string from, string to) => Transitions.TryGetValue(from, out var next) && next.Contains(to);
}

/// <summary>專案成員的角色權限層級。</summary>
public static class ProjectRoles
{
    /// <summary>專案主管／負責經理，擁有專案完整維護權限。</summary>
    public const string Manager = "manager";

    /// <summary>專案一般成員，可參與專案內容與單據協作。</summary>
    public const string Member = "member";

    /// <summary>專案檢視者，僅有唯讀權限不可修改任何專案單據。</summary>
    public const string Viewer = "viewer";

    /// <summary>所有專案角色清單。</summary>
    public static readonly string[] All = [Manager, Member, Viewer];
}

/// <summary>業務日期以台灣時間（UTC+8）切日；搜尋「建立日期」等 UTC 時間欄位時用來換算區間。</summary>
public static class BusinessTime
{
    /// <summary>台灣時區偏移量（UTC+8）。</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    /// <summary>
    /// 計算指定業務日期的開始時間戳記（對應台灣時間當日 00:00:00）。
    /// </summary>
    /// <param name="date">業務日期。</param>
    /// <returns>包含時區偏移的日期時間物件。</returns>
    public static DateTimeOffset StartOfDay(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), Offset);

    /// <summary>
    /// 取得目前的台灣業務日期。
    /// </summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Offset).DateTime);
}
