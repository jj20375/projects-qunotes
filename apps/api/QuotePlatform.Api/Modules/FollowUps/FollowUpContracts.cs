namespace QuotePlatform.Api.Modules.FollowUps;

/// <summary>新增或修改跟進紀錄的輸入。</summary>
/// <param name="Type">類型（call、visit、meeting、email、message、other）。</param>
/// <param name="OccurredAt">互動發生時間，不可晚於現在。</param>
/// <param name="ContactId">對象聯絡人；必須屬於專案的客戶，選了就以聯絡人姓名為準。</param>
/// <param name="ContactName">對象姓名；沒有選聯絡人時使用（100 字內）。</param>
/// <param name="Summary">主旨（必填，200 字內）。</param>
/// <param name="Content">內容（4000 字內）。</param>
/// <param name="NextFollowUpOn">下次聯絡日（台灣日期），不可早於發生日期。</param>
/// <param name="NextAction">下次要做的事（200 字內）；沒有下次聯絡日時不儲存。</param>
public sealed record FollowUpInput(string? Type, DateTimeOffset? OccurredAt, Guid? ContactId, string? ContactName, string? Summary, string? Content, DateOnly? NextFollowUpOn, string? NextAction);

/// <summary>跨專案跟進紀錄總表的查詢條件；範圍一律限於目前使用者看得到的專案。</summary>
/// <param name="Keyword">關鍵字（主旨、內容、對象姓名、專案編號、專案名稱、客戶名稱）。</param>
/// <param name="Type">類型。</param>
/// <param name="CreatedBy">撰寫人使用者識別碼。</param>
/// <param name="DepartmentId">專案所屬部門（含下層部門）。</param>
/// <param name="ProjectId">指定專案。</param>
/// <param name="From">發生日期起（台灣日期，含當天）。</param>
/// <param name="To">發生日期迄（台灣日期，含當天）。</param>
/// <param name="Page">頁碼（預設 1）。</param>
/// <param name="PageSize">每頁筆數（預設 20，最多 200）。</param>
/// <param name="Sort">排序欄位：occurredAt（預設）或 createdAt。</param>
/// <param name="Desc">是否降冪（預設 true）。</param>
public sealed record FollowUpSearchQuery(string? Keyword, string? Type, Guid? CreatedBy, Guid? DepartmentId, Guid? ProjectId, DateOnly? From, DateOnly? To, int? Page, int? PageSize, string? Sort, bool? Desc);

/// <summary>專案目前的跟進狀態；每次寫入後回傳，畫面據此更新。</summary>
/// <param name="LastFollowUpAt">最新一筆的發生時間；沒有紀錄時為 null。</param>
/// <param name="NextFollowUpOn">下次聯絡日；沒有排定時為 null。</param>
/// <param name="NextAction">下次要做的事。</param>
public sealed record FollowUpProjectState(DateTimeOffset? LastFollowUpAt, DateOnly? NextFollowUpOn, string? NextAction);

/// <summary>人員摘要（撰寫人、修改人）。</summary>
/// <param name="Id">使用者識別碼。</param>
/// <param name="DisplayName">顯示名稱。</param>
public sealed record FollowUpUser(Guid Id, string? DisplayName);

/// <summary>一筆跟進紀錄的回傳內容。</summary>
/// <param name="Id">跟進紀錄識別碼。</param>
/// <param name="ProjectId">所屬專案識別碼。</param>
/// <param name="Type">類型。</param>
/// <param name="OccurredAt">發生時間。</param>
/// <param name="ContactId">對象聯絡人識別碼。</param>
/// <param name="ContactName">對象姓名。</param>
/// <param name="Summary">主旨。</param>
/// <param name="Content">內容。</param>
/// <param name="NextFollowUpOn">下次聯絡日。</param>
/// <param name="NextAction">下次要做的事。</param>
/// <param name="NextDismissedAt">按「不用跟進」的時間。</param>
/// <param name="CreatedBy">撰寫人。</param>
/// <param name="CreatedAt">建立時間。</param>
/// <param name="UpdatedBy">最後修改人；沒有修改過為 null。</param>
/// <param name="UpdatedAt">最後更新時間。</param>
public sealed record FollowUpItem(
    Guid Id,
    Guid ProjectId,
    string Type,
    DateTimeOffset OccurredAt,
    Guid? ContactId,
    string? ContactName,
    string Summary,
    string? Content,
    DateOnly? NextFollowUpOn,
    string? NextAction,
    DateTimeOffset? NextDismissedAt,
    FollowUpUser CreatedBy,
    DateTimeOffset CreatedAt,
    FollowUpUser? UpdatedBy,
    DateTimeOffset UpdatedAt);
