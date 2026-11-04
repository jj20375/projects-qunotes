using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.FollowUps;

/// <summary>
/// 專案跟進紀錄控制器（公司範圍）。紀錄的可見範圍完全繼承所屬專案，看不到專案一律回 404。
/// </summary>
/// <param name="service">跟進紀錄業務服務。</param>
[Route("api")]
public sealed class FollowUpsController(FollowUpService service) : ScopedControllerBase
{
    /// <summary>
    /// 跨專案跟進紀錄總表（目前資料範圍內的所有專案）。
    /// </summary>
    /// <param name="query">關鍵字、類型、撰寫人、部門、專案、日期區間、排序與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 { total, page, pageSize, items }。</returns>
    [HttpGet("followups")]
    public async Task<ActionResult> Search([FromQuery] FollowUpSearchQuery query, CancellationToken cancellationToken) =>
        TryCompany("followups", "read", out var scope) ? ToResult(await service.SearchAsync(scope, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 查詢專案內的跟進紀錄（最新一筆在前）。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="type">類型篩選（call、visit、meeting、email、message、other）。</param>
    /// <param name="query">關鍵字與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>分頁結果、寫入旗標與專案目前的跟進狀態。</returns>
    [HttpGet("projects/{projectId:guid}/followups")]
    public async Task<ActionResult> ListByProject(Guid projectId, [FromQuery] string? type, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("followups", "read", out var scope) ? ToResult(await service.ListByProjectAsync(scope, projectId, type, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 專案客戶的聯絡人（表單下拉選單用）。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="keyword">比對姓名與職稱。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>最多 20 筆聯絡人。</returns>
    [HttpGet("projects/{projectId:guid}/followups/contacts")]
    public async Task<ActionResult> Contacts(Guid projectId, [FromQuery] string? keyword, CancellationToken cancellationToken) =>
        TryCompany("followups", "read", out var scope) ? ToResult(await service.ContactsAsync(scope, projectId, keyword, cancellationToken)) : Forbidden();

    /// <summary>
    /// 新增跟進紀錄。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="input">紀錄內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>201 與新紀錄、專案重算後的跟進狀態。</returns>
    [HttpPost("projects/{projectId:guid}/followups")]
    public async Task<ActionResult> Create(Guid projectId, FollowUpInput input, CancellationToken cancellationToken) =>
        TryCompany("followups", "create", out var scope) ? ToResult(await service.CreateAsync(scope, projectId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 修改跟進紀錄。
    /// </summary>
    /// <param name="id">跟進紀錄識別碼。</param>
    /// <param name="input">紀錄內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>修改後的紀錄與專案重算後的跟進狀態。</returns>
    [HttpPut("followups/{id:guid}")]
    public async Task<ActionResult> Update(Guid id, FollowUpInput input, CancellationToken cancellationToken) =>
        TryCompany("followups", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, id, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除跟進紀錄。
    /// </summary>
    /// <param name="id">跟進紀錄識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>專案重算後的跟進狀態。</returns>
    [HttpDelete("followups/{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        TryCompany("followups", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, id, cancellationToken)) : Forbidden();

    /// <summary>
    /// 不用跟進：清掉專案目前的下次聯絡日。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>專案重算後的跟進狀態。</returns>
    [HttpPost("projects/{projectId:guid}/followups/dismiss-next")]
    public async Task<ActionResult> DismissNext(Guid projectId, CancellationToken cancellationToken) =>
        TryCompany("followups", "update", out var scope) ? ToResult(await service.DismissNextAsync(scope, projectId, cancellationToken)) : Forbidden();
}
