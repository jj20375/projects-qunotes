using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>
/// 報價單：建立、編輯（只限草稿）、搜尋與狀態流程。可見性與寫入權完全繼承所屬專案（ProjectAccessService）。
/// </summary>
/// <remarks>
/// 流程：草稿 → 送審 → 核准（或退回草稿）→ 已送出 → 客戶接受／拒絕。送審後內容鎖定，要修改只能改版。
/// 改版時舊版 IsLatest=false 但狀態不變；新版送審時舊版才標為 superseded，刪除尚未送審的新版會把舊版恢復為最新。
/// 客戶接受時：同專案其他進行中的報價改為 superseded，成交金額回寫專案，草稿／報價中的專案改為得標。
/// 每個專案只能有一份定案（已接受）報價；訂單必須掛在這份定案報價下，出貨單、發票、保固單再掛在訂單下。
/// </remarks>
public sealed partial class QuoteService(QuoteDbContext db, ProjectAccessService access, ClientAccessService clientAccess, NumberSequenceService numbers, AuditService audit)
{
    [GeneratedRegex(@"-R(\d{1,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex RevisionSuffixPattern();

    /// <summary>進行中的報價；用在 EF 查詢內，必須是 List（陣列在 C# 14 會綁到 ReadOnlySpan 多載而無法轉譯）。</summary>
    private static readonly List<string> OpenStatuses = [QuoteStatuses.PendingApproval, QuoteStatuses.Approved, QuoteStatuses.Sent];

    /// <summary>專案底下的報價：看不到專案時回 404，與訂單、保固、發票等子資料一致（不透露專案是否存在）。</summary>
    public async Task<ServiceResult<object>> ListByProjectAsync(RequestScope scope, Guid projectId, QuoteSearchQuery query, CancellationToken cancellationToken) =>
        await access.GetAsync(scope, projectId, track: false, cancellationToken) is null
            ? ServiceResult<object>.NotFound("找不到專案。")
            : await SearchAsync(scope, query, cancellationToken);

    public async Task<ServiceResult<object>> SearchAsync(RequestScope scope, QuoteSearchQuery query, CancellationToken cancellationToken)
    {
        var visibleProjectIds = access.VisibleProjects(scope).Select(value => value.Id);
        var quotes = db.Quotes.AsNoTracking().Where(value => visibleProjectIds.Contains(value.ProjectId));
        // List：C# 14 的 array.Contains 會綁到 ReadOnlySpan，EF Core 9 無法轉譯。
        var statuses = (query.Status ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
        if (statuses.Any(value => !QuoteStatuses.All.Contains(value))) return ServiceResult<object>.BadRequest("報價狀態不正確。");
        if (query.LatestOnly ?? true) quotes = quotes.Where(value => value.IsLatest);
        if (statuses.Count > 0) quotes = quotes.Where(value => statuses.Contains(value.Status));
        if (query.ProjectId is Guid projectId) quotes = quotes.Where(value => value.ProjectId == projectId);
        if (query.ClientId is Guid clientId) quotes = quotes.Where(value => value.ClientId == clientId);
        if (query.From is DateOnly from) quotes = quotes.Where(value => value.QuoteDate >= from);
        if (query.To is DateOnly to) quotes = quotes.Where(value => value.QuoteDate <= to);
        if (query.AmountMin is decimal min) quotes = quotes.Where(value => value.Total >= min);
        if (query.AmountMax is decimal max) quotes = quotes.Where(value => value.Total <= max);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            // 畫面上的單號含版次（例：Q202611-0012-R1），版次另存在 RevisionNo，搜尋時拆開比對。
            var revision = RevisionSuffixPattern().Match(keyword);
            if (revision.Success)
            {
                var quoteNo = keyword[..revision.Index];
                var revisionNo = int.Parse(revision.Groups[1].Value);
                quotes = quotes.Where(value => value.RevisionNo == revisionNo && (quoteNo == string.Empty || value.QuoteNo.Contains(quoteNo)));
            }
            else
            {
                // 客戶名稱以報價快照比對（客戶改名後仍找得到舊報價），簡稱則看目前的客戶資料。
                quotes = quotes.Where(value => value.QuoteNo.Contains(keyword) || value.ClientName.Contains(keyword) || (value.Title != null && value.Title.Contains(keyword))
                    || db.Clients.Any(client => client.Id == value.ClientId && client.ShortName != null && client.ShortName.Contains(keyword)));
            }
        }

        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 200);
        var page = Math.Max(query.Page ?? 1, 1);
        var total = await quotes.CountAsync(cancellationToken);
        var ordered = query.Sort == "quoteNo"
            ? quotes.OrderByDescending(value => value.QuoteNo).ThenByDescending(value => value.RevisionNo)
            : quotes.OrderByDescending(value => value.QuoteDate).ThenByDescending(value => value.QuoteNo).ThenByDescending(value => value.RevisionNo);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(value => new
            {
                value.Id,
                value.QuoteNo,
                value.RevisionNo,
                value.IsLatest,
                value.Status,
                value.Title,
                value.QuoteDate,
                value.ValidUntil,
                value.ClientName,
                value.ProjectId,
                projectNo = db.Projects.Where(project => project.Id == value.ProjectId).Select(project => project.ProjectNo).FirstOrDefault(),
                projectName = db.Projects.Where(project => project.Id == value.ProjectId).Select(project => project.Name).FirstOrDefault(),
                value.Currency,
                value.NetAmount,
                value.Total,
                value.CreatedAt,
            })
            .ToListAsync(cancellationToken);
        var today = BusinessTime.Today;
        return ServiceResult<object>.Ok(new { total, page, pageSize, items = items.Select(value => new { value.Id, value.QuoteNo, value.RevisionNo, value.IsLatest, value.Status, value.Title, value.QuoteDate, value.ValidUntil, value.ClientName, value.ProjectId, value.projectNo, value.projectName, value.Currency, value.NetAmount, value.Total, value.CreatedAt, isExpired = IsExpired(value.Status, value.ValidUntil, today) }) });
    }

    public async Task<ServiceResult<object>> GetAsync(RequestScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        var context = await LoadAsync(scope, quoteId, track: false, cancellationToken);
        if (context is null) return ServiceResult<object>.NotFound("找不到報價單。");
        var revisions = await db.Quotes.AsNoTracking()
            .Where(value => value.QuoteNo == context.Quote.QuoteNo)
            .OrderBy(value => value.RevisionNo)
            .Select(value => new { value.Id, value.RevisionNo, value.Status, value.Total, value.IsLatest })
            .ToListAsync(cancellationToken);
        var people = await db.Users.AsNoTracking()
            .Where(value => value.Id == context.Quote.CreatedBy || value.Id == context.Quote.ApprovedBy || value.Id == context.Quote.SubmittedBy)
            .ToDictionaryAsync(value => value.Id, value => value.DisplayName, cancellationToken);
        return ServiceResult<object>.Ok(ToDto(scope, context, revisions, people));
    }

    public async Task<ServiceResult<QuoteCreated>> CreateAsync(RequestScope scope, Guid projectId, QuoteInput input, string source, Guid? importJobId, CancellationToken cancellationToken)
    {
        var projectAccess = await access.GetAsync(scope, projectId, track: true, cancellationToken);
        if (projectAccess is null) return ServiceResult<QuoteCreated>.NotFound("找不到專案。");
        if (!projectAccess.CanWrite) return ServiceResult<QuoteCreated>.Forbidden("您對此專案只有檢視權限。");
        var project = projectAccess.Project;
        if (project.Status is ProjectStatuses.Closed or ProjectStatuses.Cancelled) return ServiceResult<QuoteCreated>.BadRequest("已結案或已取消的專案不能新增報價。");

        var clientId = input.ClientId ?? project.ClientId;
        // 專案本身的客戶不受部門可見範圍限制（跨部門合作的專案成員仍能報價）；改選其他客戶時只能選自己看得到的。
        var client = await (clientId == project.ClientId ? db.Clients : clientAccess.VisibleClients(scope)).AsNoTracking().SingleOrDefaultAsync(value => value.Id == clientId, cancellationToken);
        if (client is null) return ServiceResult<QuoteCreated>.BadRequest("客戶不存在。");
        var setting = await db.CompanySettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new CompanySetting();
        var primary = await db.ClientContacts.AsNoTracking().Where(value => value.ClientId == clientId).OrderByDescending(value => value.IsPrimary).ThenBy(value => value.SortOrder).FirstOrDefaultAsync(cancellationToken);

        var quoteDate = input.QuoteDate ?? BusinessTime.Today;
        var quote = new Quote
        {
            ProjectId = projectId,
            ClientId = clientId,
            QuoteDate = quoteDate,
            ValidUntil = input.ValidUntil ?? quoteDate.AddDays(setting.QuoteValidDays),
            Currency = setting.DefaultCurrency,
            TaxRate = setting.DefaultTaxRate,
            Terms = QuoteTerms.Normalize(setting.DefaultTerms),
            PaymentTerms = client.PaymentTerms,
            ClientName = client.InvoiceTitle ?? client.Name,
            ClientTaxId = client.TaxId,
            ClientAddress = client.InvoiceAddress ?? client.Address,
            ContactName = primary?.Name,
            ContactPhone = primary?.Mobile ?? primary?.Phone ?? client.Phone,
            ContactEmail = primary?.Email ?? client.Email,
            Source = source,
            ImportJobId = importJobId,
            CreatedBy = scope.UserId,
        };
        if (Apply(quote, input, scope) is string error) return ServiceResult<QuoteCreated>.BadRequest(error);
        quote.QuoteNo = await numbers.NextByPatternAsync(
            scope.CompanyId!.Value,
            "quote",
            setting.QuoteNumberPattern,
            quoteDate,
            candidate => db.Quotes.IgnoreQueryFilters().AnyAsync(value => value.CompanyId == scope.CompanyId && value.QuoteNo == candidate, cancellationToken),
            cancellationToken);
        db.Quotes.Add(quote);

        // 專案開始報價：草稿自動進入「報價中」；未得標的專案重新報價也回到「報價中」。
        if (project.Status == ProjectStatuses.Draft) ChangeProjectStatus(project, ProjectStatuses.Quoting, $"建立報價單 {quote.QuoteNo}", scope.UserId);
        else if (project.Status == ProjectStatuses.Lost) ChangeProjectStatus(project, ProjectStatuses.Quoting, $"重新報價：建立報價單 {quote.QuoteNo}", scope.UserId);
        audit.Add(scope.CompanyId, scope.UserId, "quote", quote.Id, "create", after: new { quote.QuoteNo, quote.ProjectId, quote.Total, source });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<QuoteCreated>.Created(new QuoteCreated(quote.Id, quote.QuoteNo));
    }

    public async Task<ServiceResult<object>> UpdateAsync(RequestScope scope, Guid quoteId, QuoteInput input, CancellationToken cancellationToken)
    {
        var context = await LoadAsync(scope, quoteId, track: true, cancellationToken);
        if (context is null) return ServiceResult<object>.NotFound("找不到報價單。");
        if (!context.CanWrite) return ServiceResult<object>.Forbidden("您對此專案只有檢視權限。");
        var quote = context.Quote;
        if (quote.Status != QuoteStatuses.Draft) return ServiceResult<object>.BadRequest("只有草稿可以修改；已送審的報價請改版。");

        if (input.ClientId is Guid clientId && clientId != quote.ClientId)
        {
            if (!await clientAccess.VisibleClients(scope).AnyAsync(value => value.Id == clientId, cancellationToken)) return ServiceResult<object>.BadRequest("客戶不存在。");
            quote.ClientId = clientId;
        }
        var before = new { quote.Total, items = quote.Items.Count };
        // 明細整批取代：先刪舊列再加新列，順序即 SortOrder。
        db.QuoteItems.RemoveRange(quote.Items);
        quote.Items.Clear();
        if (Apply(quote, input, scope) is string error) return ServiceResult<object>.BadRequest(error);
        // 新明細的 Id 由程式產生，若只靠導覽屬性被 EF 發現會被當成既有資料；必須明確標為新增。
        db.QuoteItems.AddRange(quote.Items);
        quote.UpdatedBy = scope.UserId;
        quote.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "quote", quote.Id, "update", before, new { quote.Total, items = quote.Items.Count });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { quote.Id });
    }

    /// <summary>狀態動作：submit／approve／return／send／accept／reject。</summary>
    public async Task<ServiceResult<object>> TransitionAsync(RequestScope scope, Guid quoteId, string action, string? reason, CancellationToken cancellationToken)
    {
        var context = await LoadAsync(scope, quoteId, track: true, cancellationToken);
        if (context is null) return ServiceResult<object>.NotFound("找不到報價單。");
        var quote = context.Quote;
        var canApprove = scope.Has("quotes", "update") && scope.HasOption("quotes", "approve");
        if (action is "approve" or "return" ? !canApprove : !context.CanWrite) return ServiceResult<object>.Forbidden(action is "approve" or "return" ? "沒有核准報價的權限。" : "您對此專案只有檢視權限。");
        if (!quote.IsLatest) return ServiceResult<object>.BadRequest("這不是最新版本的報價。");

        var now = DateTimeOffset.UtcNow;
        var (from, to) = action switch
        {
            "submit" => (QuoteStatuses.Draft, QuoteStatuses.PendingApproval),
            "approve" => (QuoteStatuses.PendingApproval, QuoteStatuses.Approved),
            "return" => (QuoteStatuses.PendingApproval, QuoteStatuses.Draft),
            "send" => (QuoteStatuses.Approved, QuoteStatuses.Sent),
            "accept" => (QuoteStatuses.Sent, QuoteStatuses.Accepted),
            "reject" => (QuoteStatuses.Sent, QuoteStatuses.Rejected),
            _ => (string.Empty, string.Empty),
        };
        if (from == string.Empty) return ServiceResult<object>.BadRequest("不支援的動作。");
        if (quote.Status != from) return ServiceResult<object>.BadRequest("目前狀態不能執行此動作。");
        if (action is "return" or "reject" && string.IsNullOrWhiteSpace(reason)) return ServiceResult<object>.BadRequest("請填寫原因。");
        if (action == "submit" && !quote.Items.Any(value => value.LineType == QuoteLineTypes.Item)) return ServiceResult<object>.BadRequest("報價單至少要有一筆品項。");
        if (action == "accept")
        {
            var accepted = await db.Quotes.AsNoTracking()
                .Where(value => value.ProjectId == quote.ProjectId && value.Id != quote.Id && value.Status == QuoteStatuses.Accepted)
                .Select(value => new { value.QuoteNo, value.RevisionNo })
                .FirstOrDefaultAsync(cancellationToken);
            if (accepted is not null) return ServiceResult<object>.BadRequest($"此專案已有定案報價 {(accepted.RevisionNo > 0 ? $"{accepted.QuoteNo}-R{accepted.RevisionNo}" : accepted.QuoteNo)}，不能再接受其他報價。");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        quote.Status = to;
        quote.StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        switch (action)
        {
            case "submit":
                quote.SubmittedBy = scope.UserId;
                quote.SubmittedAt = now;
                // 專案未得標前就建好的草稿，之後送審代表重新報價。
                if (context.Project.Status == ProjectStatuses.Lost) ChangeProjectStatus(context.Project, ProjectStatuses.Quoting, $"重新報價：送審 {quote.QuoteNo}-R{quote.RevisionNo}", scope.UserId);
                // 新版正式送出後，被改版的舊版才標示為已被取代。
                if (quote.RevisionOfId is Guid previousId)
                {
                    var previous = await db.Quotes.SingleAsync(value => value.Id == previousId, cancellationToken);
                    if (previous.Status != QuoteStatuses.Accepted) previous.Status = QuoteStatuses.Superseded;
                }
                break;
            case "approve":
                quote.ApprovedBy = scope.UserId;
                quote.ApprovedAt = now;
                break;
            case "send":
                quote.SentAt = now;
                break;
            case "reject":
                quote.RejectedAt = now;
                break;
            case "accept":
                quote.AcceptedAt = now;
                await AcceptAsync(scope, quote, context.Project, cancellationToken);
                break;
        }
        quote.UpdatedBy = scope.UserId;
        quote.UpdatedAt = now;
        audit.Add(scope.CompanyId, scope.UserId, "quote", quote.Id, action, after: new { from, to, reason });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { quote.Id, quote.Status });
    }

    /// <summary>改版：複製為同單號的新草稿（RevisionNo + 1）。</summary>
    public async Task<ServiceResult<object>> ReviseAsync(RequestScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        var context = await LoadAsync(scope, quoteId, track: true, cancellationToken);
        if (context is null) return ServiceResult<object>.NotFound("找不到報價單。");
        if (!context.CanWrite) return ServiceResult<object>.Forbidden("您對此專案只有檢視權限。");
        var source = context.Quote;
        if (!source.IsLatest || !QuoteStatuses.Revisable.Contains(source.Status)) return ServiceResult<object>.BadRequest("只有已送審、已核准、已送出或客戶拒絕的最新版本可以改版。");

        var revision = Copy(source, scope.UserId);
        revision.QuoteNo = source.QuoteNo;
        // 已刪除的改版草稿仍佔用版次（軟刪除且有唯一索引），版次取曾經用過的最大值 + 1。
        revision.RevisionNo = await db.Quotes.IgnoreQueryFilters()
            .Where(value => value.CompanyId == source.CompanyId && value.QuoteNo == source.QuoteNo)
            .MaxAsync(value => value.RevisionNo, cancellationToken) + 1;
        revision.RevisionOfId = source.Id;
        revision.Source = QuoteSources.Copy;
        source.IsLatest = false;
        db.Quotes.Add(revision);
        audit.Add(scope.CompanyId, scope.UserId, "quote", revision.Id, "revise", after: new { source.QuoteNo, revision.RevisionNo });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new { revision.Id, revision.RevisionNo });
    }

    /// <summary>只有草稿可刪除；刪除改版草稿時把上一版恢復為最新。</summary>
    public async Task<ServiceResult<Empty>> DeleteAsync(RequestScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        var context = await LoadAsync(scope, quoteId, track: true, cancellationToken);
        if (context is null) return ServiceResult<Empty>.NotFound("找不到報價單。");
        if (!context.CanWrite) return ServiceResult<Empty>.Forbidden("您對此專案只有檢視權限。");
        var quote = context.Quote;
        if (quote.Status != QuoteStatuses.Draft) return ServiceResult<Empty>.BadRequest("只有草稿可以刪除。");
        if (quote.RevisionOfId is Guid previousId)
        {
            var previous = await db.Quotes.SingleAsync(value => value.Id == previousId, cancellationToken);
            previous.IsLatest = true;
        }
        quote.IsDeleted = true;
        quote.DeletedAt = DateTimeOffset.UtcNow;
        quote.IsLatest = false;
        audit.Add(scope.CompanyId, scope.UserId, "quote", quote.Id, "delete", new { quote.QuoteNo, quote.RevisionNo });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    /// <summary>取得報價與所屬專案的存取判斷；專案看不到就當作報價不存在。</summary>
    public async Task<QuoteContext?> LoadAsync(RequestScope scope, Guid quoteId, bool track, CancellationToken cancellationToken)
    {
        var query = db.Quotes.Include(value => value.Items.OrderBy(item => item.SortOrder)).Where(value => value.Id == quoteId);
        var quote = await (track ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
        if (quote is null) return null;
        var projectAccess = await access.GetAsync(scope, quote.ProjectId, track, cancellationToken);
        return projectAccess is null ? null : new QuoteContext(quote, projectAccess.Project, projectAccess.CanWrite && scope.Has("quotes", "update"));
    }

    private async Task AcceptAsync(RequestScope scope, Quote quote, Project project, CancellationToken cancellationToken)
    {
        var others = await db.Quotes
            .Where(value => value.ProjectId == quote.ProjectId && value.Id != quote.Id && OpenStatuses.Contains(value.Status))
            .ToListAsync(cancellationToken);
        foreach (var other in others) other.Status = QuoteStatuses.Superseded;
        project.ContractAmount = quote.NetAmount;
        project.ContractTotal = quote.Total;
        project.UpdatedBy = scope.UserId;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        if (project.Status is ProjectStatuses.Draft or ProjectStatuses.Quoting or ProjectStatuses.Lost)
        {
            ChangeProjectStatus(project, ProjectStatuses.Won, $"客戶接受報價 {quote.QuoteNo}-R{quote.RevisionNo}", scope.UserId);
        }
    }

    private void ChangeProjectStatus(Project project, string status, string reason, Guid userId)
    {
        db.ProjectStatusHistories.Add(new ProjectStatusHistory { ProjectId = project.Id, FromStatus = project.Status, ToStatus = status, Reason = reason, ChangedBy = userId });
        project.Status = status;
        if (status == ProjectStatuses.Won) project.WonAt = DateTimeOffset.UtcNow;
    }

    /// <summary>套用輸入並重算金額；回傳錯誤訊息或 null。沒有 view_cost 的人送來的成本會被忽略，避免覆寫看不到的資料。</summary>
    private static string? Apply(Quote quote, QuoteInput input, RequestScope scope)
    {
        if (input.DiscountType is not null && !QuoteDiscountTypes.All.Contains(input.DiscountType)) return "折扣類型不正確。";
        if (input.TaxType is not null && !QuoteTaxTypes.All.Contains(input.TaxType)) return "稅別不正確。";
        if (input.TaxRate is < 0 or > 100) return "稅率必須介於 0 到 100。";
        if (input.DiscountValue < 0 || (input.DiscountType == QuoteDiscountTypes.Percent && input.DiscountValue > 100)) return "折扣不正確。";
        if (input.Currency is not null && input.Currency.Length != 3) return "幣別必須是 3 碼代碼，例如 TWD。";
        var quoteDate = input.QuoteDate ?? quote.QuoteDate;
        if (input.ValidUntil is DateOnly validUntil && validUntil < quoteDate) return "有效期限不可早於報價日期。";

        var items = input.Items ?? [];
        if (items.Length > 500) return "明細最多 500 列。";
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var lineType = item.LineType ?? QuoteLineTypes.Item;
            if (!QuoteLineTypes.All.Contains(lineType)) return $"第 {index + 1} 列類型不正確。";
            if (string.IsNullOrWhiteSpace(item.Name)) return $"第 {index + 1} 列缺少品名。";
            if (item.Quantity < 0 || item.UnitPrice < 0 || item.UnitCost < 0) return $"第 {index + 1} 列的數量、單價或成本不可為負數。";
            if (item.DiscountRate is < 0 or > 100) return $"第 {index + 1} 列的折扣必須介於 0 到 100。";
        }

        quote.Title = Clean(input.Title) ?? quote.Title;
        quote.QuoteDate = quoteDate;
        quote.ValidUntil = input.ValidUntil ?? quote.ValidUntil;
        quote.ClientName = Clean(input.ClientName) ?? quote.ClientName;
        quote.ClientTaxId = input.ClientTaxId is null ? quote.ClientTaxId : Clean(input.ClientTaxId);
        quote.ClientAddress = input.ClientAddress is null ? quote.ClientAddress : Clean(input.ClientAddress);
        quote.ContactName = input.ContactName is null ? quote.ContactName : Clean(input.ContactName);
        quote.ContactPhone = input.ContactPhone is null ? quote.ContactPhone : Clean(input.ContactPhone);
        quote.ContactEmail = input.ContactEmail is null ? quote.ContactEmail : Clean(input.ContactEmail);
        quote.Currency = input.Currency?.ToUpperInvariant() ?? quote.Currency;
        quote.DiscountType = input.DiscountType ?? quote.DiscountType;
        quote.DiscountValue = quote.DiscountType == QuoteDiscountTypes.None ? 0 : input.DiscountValue ?? quote.DiscountValue;
        quote.TaxType = input.TaxType ?? quote.TaxType;
        quote.TaxIncluded = input.TaxIncluded ?? quote.TaxIncluded;
        quote.TaxRate = input.TaxRate ?? quote.TaxRate;
        quote.PaymentTerms = input.PaymentTerms is null ? quote.PaymentTerms : Clean(input.PaymentTerms);
        quote.DeliveryTerms = input.DeliveryTerms is null ? quote.DeliveryTerms : Clean(input.DeliveryTerms);
        // 條款一律轉成「一條一行、無編號」：使用者整段貼上的條列文字儲存後就是可逐條編輯的清單。
        quote.Terms = input.Terms is null ? quote.Terms : QuoteTerms.Normalize(input.Terms);
        quote.Note = input.Note is null ? quote.Note : Clean(input.Note);
        if (string.IsNullOrWhiteSpace(quote.ClientName)) return "請填寫客戶名稱。";

        var canSeeCost = scope.HasOption("quotes", "view_cost");
        quote.Items = items.Select((item, index) => new QuoteItem
        {
            QuoteId = quote.Id,
            SortOrder = index,
            LineType = item.LineType ?? QuoteLineTypes.Item,
            ProductId = item.ProductId,
            ItemCode = Clean(item.ItemCode),
            Name = item.Name!.Trim(),
            Spec = Clean(item.Spec),
            Unit = Clean(item.Unit),
            Quantity = item.Quantity ?? 0,
            UnitPrice = item.UnitPrice ?? 0,
            DiscountRate = item.DiscountRate ?? 0,
            UnitCost = canSeeCost ? item.UnitCost : null,
            Remark = Clean(item.Remark),
        }).ToList();
        QuoteCalculator.Apply(quote);
        return null;
    }

    private static Quote Copy(Quote source, Guid userId)
    {
        var copy = new Quote
        {
            ProjectId = source.ProjectId,
            ClientId = source.ClientId,
            Title = source.Title,
            QuoteDate = BusinessTime.Today,
            ValidUntil = source.ValidUntil is DateOnly valid ? BusinessTime.Today.AddDays(valid.DayNumber - source.QuoteDate.DayNumber) : null,
            ClientName = source.ClientName,
            ClientTaxId = source.ClientTaxId,
            ClientAddress = source.ClientAddress,
            ContactName = source.ContactName,
            ContactPhone = source.ContactPhone,
            ContactEmail = source.ContactEmail,
            Currency = source.Currency,
            ExchangeRate = source.ExchangeRate,
            DiscountType = source.DiscountType,
            DiscountValue = source.DiscountValue,
            TaxType = source.TaxType,
            TaxIncluded = source.TaxIncluded,
            TaxRate = source.TaxRate,
            PaymentTerms = source.PaymentTerms,
            DeliveryTerms = source.DeliveryTerms,
            Terms = source.Terms,
            Note = source.Note,
            CreatedBy = userId,
        };
        copy.Items = source.Items.Select(item => new QuoteItem
        {
            QuoteId = copy.Id,
            SortOrder = item.SortOrder,
            LineType = item.LineType,
            ProductId = item.ProductId,
            ItemCode = item.ItemCode,
            Name = item.Name,
            Spec = item.Spec,
            Unit = item.Unit,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            DiscountRate = item.DiscountRate,
            UnitCost = item.UnitCost,
            Remark = item.Remark,
        }).ToList();
        QuoteCalculator.Apply(copy);
        return copy;
    }

    private static object ToDto(RequestScope scope, QuoteContext context, object revisions, IReadOnlyDictionary<Guid, string> people)
    {
        var quote = context.Quote;
        var canSeeCost = scope.HasOption("quotes", "view_cost");
        var canApprove = scope.Has("quotes", "update") && scope.HasOption("quotes", "approve");
        var editable = context.CanWrite && quote.IsLatest;
        var actions = new List<string>();
        if (editable && quote.Status == QuoteStatuses.Draft) actions.AddRange(["save", "submit", "delete"]);
        if (canApprove && quote.IsLatest && quote.Status == QuoteStatuses.PendingApproval) actions.AddRange(["approve", "return"]);
        if (editable && quote.Status == QuoteStatuses.Approved) actions.Add("send");
        if (editable && quote.Status == QuoteStatuses.Sent) actions.AddRange(["accept", "reject"]);
        if (editable && QuoteStatuses.Revisable.Contains(quote.Status)) actions.Add("revise");

        return new
        {
            quote.Id,
            quote.QuoteNo,
            quote.RevisionNo,
            quote.RevisionOfId,
            quote.IsLatest,
            quote.Status,
            quote.StatusReason,
            isExpired = IsExpired(quote.Status, quote.ValidUntil, BusinessTime.Today),
            project = new { context.Project.Id, context.Project.ProjectNo, context.Project.Name, context.Project.Status },
            quote.ClientId,
            quote.Title,
            quote.QuoteDate,
            quote.ValidUntil,
            quote.ClientName,
            quote.ClientTaxId,
            quote.ClientAddress,
            quote.ContactName,
            quote.ContactPhone,
            quote.ContactEmail,
            quote.Currency,
            quote.Subtotal,
            quote.DiscountType,
            quote.DiscountValue,
            quote.DiscountAmount,
            quote.TaxType,
            quote.TaxIncluded,
            quote.TaxRate,
            quote.NetAmount,
            quote.TaxAmount,
            quote.Total,
            costTotal = canSeeCost ? quote.CostTotal : null,
            grossMargin = canSeeCost ? quote.GrossMargin : null,
            quote.PaymentTerms,
            quote.DeliveryTerms,
            quote.Terms,
            quote.Note,
            quote.Source,
            createdBy = people.GetValueOrDefault(quote.CreatedBy),
            quote.CreatedAt,
            submittedBy = quote.SubmittedBy is Guid submitter ? people.GetValueOrDefault(submitter) : null,
            quote.SubmittedAt,
            approvedBy = quote.ApprovedBy is Guid approver ? people.GetValueOrDefault(approver) : null,
            quote.ApprovedAt,
            quote.SentAt,
            quote.AcceptedAt,
            quote.RejectedAt,
            canSeeCost,
            actions,
            revisions,
            items = quote.Items.OrderBy(value => value.SortOrder).Select(value => new
            {
                value.Id,
                value.LineType,
                value.ProductId,
                value.ItemCode,
                value.Name,
                value.Spec,
                value.Unit,
                value.Quantity,
                value.UnitPrice,
                value.DiscountRate,
                value.Amount,
                unitCost = canSeeCost ? value.UnitCost : null,
                value.Remark,
            }),
        };
    }

    private static bool IsExpired(string status, DateOnly? validUntil, DateOnly today) => status == QuoteStatuses.Sent && validUntil < today;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>建立報價的結果。</summary>
public sealed record QuoteCreated(Guid Id, string QuoteNo);

/// <summary>報價與其專案、寫入權。</summary>
public sealed record QuoteContext(Quote Quote, Project Project, bool CanWrite);
