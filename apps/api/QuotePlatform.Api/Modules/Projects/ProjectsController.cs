using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Projects;

/// <summary>
/// 專案搜尋條件參數契約；狀態支援以多選陣列傳入。
/// </summary>
/// <param name="Keyword">關鍵字（專案編號、名稱或客戶名稱）。</param>
/// <param name="Status">狀態篩選陣列（如 quoting, won, in_progress 等）。</param>
/// <param name="DateField">日期比對欄位（created、start、end）。</param>
/// <param name="From">起始日期。</param>
/// <param name="To">結束日期。</param>
/// <param name="AmountField">金額篩選欄位（expected 或 contract）。</param>
/// <param name="AmountMin">金額下限。</param>
/// <param name="AmountMax">金額上限。</param>
/// <param name="ClientId">客戶唯一識別碼。</param>
/// <param name="OwnerUserId">負責業務使用者 ID。</param>
/// <param name="DepartmentId">歸屬部門 ID。</param>
/// <param name="Mine">是否僅查詢自己負責或參與的專案。</param>
/// <param name="Page">頁碼（預設為 1）。</param>
/// <param name="PageSize">每頁筆數（預設為 20）。</param>
/// <param name="Sort">排序欄位名稱。</param>
/// <param name="Desc">是否為降冪排序。</param>
/// <param name="FollowUp">跟進狀態篩選（pending 待跟進、overdue 已逾期、none 沒有下次聯絡）；沒有 followups:read 時忽略。</param>
public sealed record ProjectSearchQuery(
    string? Keyword,
    string[]? Status,
    string? DateField,
    DateOnly? From,
    DateOnly? To,
    string? AmountField,
    decimal? AmountMin,
    decimal? AmountMax,
    Guid? ClientId,
    Guid? OwnerUserId,
    Guid? DepartmentId,
    bool? Mine,
    int? Page,
    int? PageSize,
    string? Sort,
    bool? Desc,
    string? FollowUp);

/// <summary>
/// 專案建立與修改之資料輸入契約。
/// </summary>
/// <param name="Name">專案名稱。</param>
/// <param name="ClientId">所屬客戶識別碼。</param>
/// <param name="DepartmentId">所屬部門 ID。</param>
/// <param name="OwnerUserId">負責業務 ID。</param>
/// <param name="StartDate">預計開始日期。</param>
/// <param name="EndDate">預計完工或截止日期。</param>
/// <param name="ExpectedAmount">預估商機金額。</param>
/// <param name="ContractAmount">契約未稅金額。</param>
/// <param name="ContractTotal">契約總金額（含稅）。</param>
/// <param name="Description">專案詳細說明與需求摘要。</param>
/// <param name="Members">專案參與成員清單。</param>
public sealed record ProjectInput(
    string? Name,
    Guid? ClientId,
    Guid? DepartmentId,
    Guid? OwnerUserId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal? ExpectedAmount,
    decimal? ContractAmount,
    decimal? ContractTotal,
    string? Description,
    ProjectMemberInput[]? Members);

/// <summary>
/// 專案參與成員輸入契約。
/// </summary>
/// <param name="UserId">使用者帳號唯一識別碼。</param>
/// <param name="ProjectRole">在專案中的角色職責（manager、member、viewer）。</param>
public sealed record ProjectMemberInput(Guid UserId, string? ProjectRole);

/// <summary>
/// 變更專案狀態請求契約。
/// </summary>
/// <param name="Status">目標專案狀態代碼。</param>
/// <param name="Reason">狀態變更原因說明（未得標或取消時填寫）。</param>
public sealed record ChangeStatusRequest(string? Status, string? Reason);

/// <summary>
/// 專案主檔管理控制器（公司租戶範圍）。
/// 所有讀寫均由 ProjectAccessService 套用資料範圍限制；超出可見範圍之專案一律回傳 404。
/// </summary>
/// <param name="service">專案領域核心服務實體。</param>
[Route("api/projects")]
public sealed class ProjectsController(ProjectService service) : ScopedControllerBase
{
    /// <summary>
    /// 條件搜尋專案清單並分頁回傳，包含各狀態之筆數統計與合計金額。
    /// </summary>
    /// <param name="query">專案查詢參數物件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁專案資料列表與狀態聚合統計。</returns>
    [HttpGet]
    public async Task<ActionResult> Search([FromQuery] ProjectSearchQuery query, CancellationToken cancellationToken) =>
        TryCompany("projects", "read", out var scope) ? ToResult(await service.SearchAsync(scope, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 取得單一專案之完整明細主檔（含客戶快照、參與成員與狀態歷史）。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回專案詳細資訊物件。</returns>
    [HttpGet("{projectId:guid}")]
    public async Task<ActionResult> Get(Guid projectId, CancellationToken cancellationToken) =>
        TryCompany("projects", "read", out var scope) ? ToResult(await service.GetAsync(scope, projectId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 建立新專案。
    /// </summary>
    /// <param name="input">專案建立資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新建立之專案 ID 與單號。</returns>
    [HttpPost]
    public async Task<ActionResult> Create(ProjectInput input, CancellationToken cancellationToken) =>
        TryCompany("projects", "create", out var scope) ? ToResult(await service.CreateAsync(scope, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新專案主檔基本資料。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="input">專案更新內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPut("{projectId:guid}")]
    public async Task<ActionResult> Update(Guid projectId, ProjectInput input, CancellationToken cancellationToken) =>
        TryCompany("projects", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, projectId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 推進或變更專案業務狀態（例如草稿轉報價中、得標、完工或結案）。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="request">變更目標狀態與原因備註。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回狀態變更後之專案主檔。</returns>
    [HttpPost("{projectId:guid}/status")]
    public async Task<ActionResult> ChangeStatus(Guid projectId, ChangeStatusRequest request, CancellationToken cancellationToken) =>
        TryCompany("projects", "update", out var scope) ? ToResult(await service.ChangeStatusAsync(scope, projectId, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除指定專案（若已有報價單、訂單或發票關聯則拒絕刪除）。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("{projectId:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, CancellationToken cancellationToken) =>
        TryCompany("projects", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, projectId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 整批取代專案參與成員名單（專案負責人固定具備完整權限，不需於成員名單中重複設定）。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="members">成員清單與被指派角色。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回設定結果。</returns>
    [HttpPut("{projectId:guid}/members")]
    public async Task<ActionResult> ReplaceMembers(Guid projectId, ProjectMemberInput[] members, CancellationToken cancellationToken) =>
        TryCompany("projects", "update", out var scope, "assign_member") ? ToResult(await service.ReplaceMembersAsync(scope, projectId, members, cancellationToken)) : Forbidden();
}
