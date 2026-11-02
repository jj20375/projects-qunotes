using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Clients;

/// <summary>客戶主檔與聯絡人。</summary>
public sealed partial class ClientService(QuoteDbContext db, NumberSequenceService numbers, AuditService audit)
{
    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex TaxIdPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public async Task<object> SearchAsync(RequestScope scope, ClientSearchQuery query, CancellationToken cancellationToken)
    {
        var clients = db.Clients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            clients = clients.Where(value => value.Name.Contains(keyword) || value.Code.Contains(keyword) || (value.ShortName != null && value.ShortName.Contains(keyword)) || value.TaxId == keyword);
        }
        if (query.Status is "active" or "inactive") clients = clients.Where(value => value.Status == query.Status);
        if (query.OwnerUserId is Guid ownerUserId) clients = clients.Where(value => value.OwnerUserId == ownerUserId);

        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 200);
        var page = Math.Max(query.Page ?? 1, 1);
        var total = await clients.CountAsync(cancellationToken);
        var rows = await clients
            .OrderBy(value => value.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(value => new
            {
                value.Id,
                value.Code,
                value.Name,
                value.ShortName,
                value.TaxId,
                value.Phone,
                value.Status,
                value.OwnerUserId,
                ownerName = db.Users.Where(user => user.Id == value.OwnerUserId).Select(user => user.DisplayName).FirstOrDefault(),
                primaryContact = db.ClientContacts.Where(contact => contact.ClientId == value.Id).OrderByDescending(contact => contact.IsPrimary).ThenBy(contact => contact.SortOrder).Select(contact => contact.Name).FirstOrDefault(),
                projectCount = db.Projects.Count(project => project.ClientId == value.Id),
            })
            .ToListAsync(cancellationToken);
        var editable = await EditableOwnerFilterAsync(scope, cancellationToken);
        return new { total, page, pageSize, items = rows.Select(value => new { value.Id, value.Code, value.Name, value.ShortName, value.TaxId, value.Phone, value.Status, value.OwnerUserId, value.ownerName, value.primaryContact, value.projectCount, canEdit = editable(value.OwnerUserId) }) };
    }

    public async Task<ServiceResult<object>> GetAsync(RequestScope scope, Guid clientId, CancellationToken cancellationToken)
    {
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(value => value.Id == clientId, cancellationToken);
        if (client is null) return ServiceResult<object>.NotFound("找不到客戶。");
        var contacts = await db.ClientContacts.AsNoTracking().Where(value => value.ClientId == clientId).OrderByDescending(value => value.IsPrimary).ThenBy(value => value.SortOrder).ToListAsync(cancellationToken);
        var ownerName = client.OwnerUserId is Guid ownerId ? await db.Users.Where(value => value.Id == ownerId).Select(value => value.DisplayName).SingleOrDefaultAsync(cancellationToken) : null;
        var parentName = client.ParentClientId is Guid parentId ? await db.Clients.Where(value => value.Id == parentId).Select(value => value.Name).SingleOrDefaultAsync(cancellationToken) : null;
        var canEdit = scope.Has("clients", "update") && (await EditableOwnerFilterAsync(scope, cancellationToken))(client.OwnerUserId);
        return ServiceResult<object>.Ok(new
        {
            client.Id,
            client.Code,
            client.Name,
            client.ShortName,
            client.TaxId,
            client.ParentClientId,
            parentName,
            client.Industry,
            client.Phone,
            client.Fax,
            client.Email,
            client.Address,
            client.InvoiceTitle,
            client.InvoiceAddress,
            client.PaymentTerms,
            client.OwnerUserId,
            ownerName,
            client.Status,
            client.Note,
            client.CreatedAt,
            client.UpdatedAt,
            canEdit,
            canDelete = canEdit && scope.Has("clients", "delete"),
            contacts = contacts.Select(value => new { value.Id, value.Name, value.Title, value.Phone, value.Mobile, value.Email, value.IsPrimary, value.Note }),
        });
    }

    public async Task<ServiceResult<object>> CreateAsync(RequestScope scope, ClientInput input, CancellationToken cancellationToken)
    {
        // 新客戶預設由建立者負責；指定其他負責人時，仍必須是自己可管理範圍內的人，避免建完就失去修改權。
        var ownerUserId = input.OwnerUserId ?? scope.UserId;
        if (await ValidateAsync(input, null, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);
        if (!(await EditableOwnerFilterAsync(scope, cancellationToken))(ownerUserId)) return ServiceResult<object>.BadRequest("負責人必須是自己或可管理範圍內的成員。");

        var code = string.IsNullOrWhiteSpace(input.Code) ? await numbers.NextClientCodeAsync(scope.CompanyId!.Value, cancellationToken) : input.Code.Trim();
        if (await db.Clients.IgnoreQueryFilters().AnyAsync(value => value.CompanyId == scope.CompanyId && value.Code == code, cancellationToken)) return ServiceResult<object>.Conflict("客戶代碼已被使用。");

        var client = new Client { Code = code, CreatedBy = scope.UserId };
        Apply(client, input, ownerUserId);
        db.Clients.Add(client);
        ReplaceContacts(client.Id, [], input.Contacts ?? []);
        audit.Add(scope.CompanyId, scope.UserId, "client", client.Id, "create", after: new { client.Code, client.Name, client.TaxId, client.OwnerUserId });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new { client.Id, client.Code });
    }

    public async Task<ServiceResult<object>> UpdateAsync(RequestScope scope, Guid clientId, ClientInput input, CancellationToken cancellationToken)
    {
        var client = await db.Clients.SingleOrDefaultAsync(value => value.Id == clientId, cancellationToken);
        if (client is null) return ServiceResult<object>.NotFound("找不到客戶。");
        var editable = await EditableOwnerFilterAsync(scope, cancellationToken);
        if (!editable(client.OwnerUserId)) return ServiceResult<object>.Forbidden("只能修改自己或可管理範圍內成員負責的客戶。");
        if (await ValidateAsync(input, clientId, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);
        var ownerUserId = input.OwnerUserId ?? client.OwnerUserId;
        if (ownerUserId != client.OwnerUserId && !editable(ownerUserId)) return ServiceResult<object>.BadRequest("負責人必須是自己或可管理範圍內的成員。");

        var code = string.IsNullOrWhiteSpace(input.Code) ? client.Code : input.Code.Trim();
        if (code != client.Code && await db.Clients.IgnoreQueryFilters().AnyAsync(value => value.CompanyId == client.CompanyId && value.Code == code, cancellationToken)) return ServiceResult<object>.Conflict("客戶代碼已被使用。");

        var before = new { client.Code, client.Name, client.TaxId, client.OwnerUserId, client.Status };
        client.Code = code;
        Apply(client, input, ownerUserId);
        client.UpdatedBy = scope.UserId;
        client.UpdatedAt = DateTimeOffset.UtcNow;
        if (input.Contacts is not null)
        {
            var existing = await db.ClientContacts.Where(value => value.ClientId == clientId).ToListAsync(cancellationToken);
            ReplaceContacts(clientId, existing, input.Contacts);
        }
        audit.Add(scope.CompanyId, scope.UserId, "client", client.Id, "update", before, new { client.Code, client.Name, client.TaxId, client.OwnerUserId, client.Status });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { client.Id });
    }

    /// <summary>軟刪除；已有專案的客戶不可刪，請改為停用，避免歷史報價失去對象。</summary>
    public async Task<ServiceResult<Empty>> DeleteAsync(RequestScope scope, Guid clientId, CancellationToken cancellationToken)
    {
        var client = await db.Clients.SingleOrDefaultAsync(value => value.Id == clientId, cancellationToken);
        if (client is null) return ServiceResult<Empty>.NotFound("找不到客戶。");
        if (!(await EditableOwnerFilterAsync(scope, cancellationToken))(client.OwnerUserId)) return ServiceResult<Empty>.Forbidden("只能刪除自己或可管理範圍內成員負責的客戶。");
        if (await db.Projects.IgnoreQueryFilters().AnyAsync(value => value.ClientId == clientId && !value.IsDeleted, cancellationToken)) return ServiceResult<Empty>.BadRequest("此客戶已有專案，請改為停用。");
        client.IsDeleted = true;
        client.DeletedAt = DateTimeOffset.UtcNow;
        client.UpdatedBy = scope.UserId;
        audit.Add(scope.CompanyId, scope.UserId, "client", client.Id, "delete", new { client.Code, client.Name });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    /// <summary>
    /// 依資料範圍判斷「負責人為某人」的客戶能否修改：company 全部；department／department_tree 為自己與可見部門的成員；own 只有自己。
    /// 沒有負責人的客戶只有全公司範圍可以改。
    /// </summary>
    public async Task<Func<Guid?, bool>> EditableOwnerFilterAsync(RequestScope scope, CancellationToken cancellationToken)
    {
        if (scope.DataScope == DataScopes.Company) return _ => true;
        if (scope.DataScope == DataScopes.Own || scope.DepartmentId is not Guid departmentId) return owner => owner == scope.UserId;

        var departmentIds = scope.DataScope == DataScopes.DepartmentTree
            ? await db.DepartmentClosures.Where(value => value.AncestorId == departmentId).Select(value => value.DescendantId).ToListAsync(cancellationToken)
            : [departmentId];
        var userIds = (await db.CompanyMemberships.Where(value => value.IsActive && value.DepartmentId != null && departmentIds.Contains(value.DepartmentId.Value)).Select(value => value.UserId).ToListAsync(cancellationToken)).ToHashSet();
        return owner => owner == scope.UserId || (owner is Guid id && userIds.Contains(id));
    }

    private async Task<string?> ValidateAsync(ClientInput input, Guid? clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "請輸入客戶名稱。";
        if (!string.IsNullOrWhiteSpace(input.TaxId) && !TaxIdPattern().IsMatch(input.TaxId.Trim())) return "統一編號必須是 8 碼數字。";
        if (!string.IsNullOrWhiteSpace(input.Email) && !EmailPattern().IsMatch(input.Email.Trim())) return "Email 格式不正確。";
        if (input.Status is not (null or "active" or "inactive")) return "狀態只能是 active 或 inactive。";
        if (input.ParentClientId is Guid parentId && (parentId == clientId || !await db.Clients.AnyAsync(value => value.Id == parentId, cancellationToken))) return "上層客戶不正確。";
        if (input.OwnerUserId is Guid ownerId && !await db.CompanyMemberships.AnyAsync(value => value.UserId == ownerId && value.IsActive, cancellationToken)) return "負責人必須是公司內啟用中的成員。";
        foreach (var contact in input.Contacts ?? [])
        {
            if (string.IsNullOrWhiteSpace(contact.Name)) return "聯絡人姓名不可空白。";
            if (!string.IsNullOrWhiteSpace(contact.Email) && !EmailPattern().IsMatch(contact.Email.Trim())) return $"聯絡人 {contact.Name} 的 Email 格式不正確。";
        }
        if ((input.Contacts ?? []).Count(value => value.IsPrimary) > 1) return "主要聯絡人只能有一位。";
        return null;
    }

    private static void Apply(Client client, ClientInput input, Guid? ownerUserId)
    {
        client.Name = input.Name!.Trim();
        client.ShortName = Clean(input.ShortName);
        client.TaxId = Clean(input.TaxId);
        client.ParentClientId = input.ParentClientId;
        client.Industry = Clean(input.Industry);
        client.Phone = Clean(input.Phone);
        client.Fax = Clean(input.Fax);
        client.Email = Clean(input.Email);
        client.Address = Clean(input.Address);
        client.InvoiceTitle = Clean(input.InvoiceTitle);
        client.InvoiceAddress = Clean(input.InvoiceAddress);
        client.PaymentTerms = Clean(input.PaymentTerms);
        client.OwnerUserId = ownerUserId;
        client.Status = input.Status ?? client.Status;
        client.Note = Clean(input.Note);
    }

    /// <summary>以輸入清單取代聯絡人：有 Id 的更新、沒有的新增、未列出的刪除；只有一位聯絡人時自動設為主要。</summary>
    private void ReplaceContacts(Guid clientId, List<ClientContact> existing, IReadOnlyList<ContactInput> inputs)
    {
        var byId = existing.ToDictionary(value => value.Id);
        var keep = new HashSet<Guid>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var contact = input.Id is Guid id && byId.TryGetValue(id, out var found) ? found : null;
            if (contact is null)
            {
                contact = new ClientContact { ClientId = clientId };
                db.ClientContacts.Add(contact);
            }
            keep.Add(contact.Id);
            contact.Name = input.Name!.Trim();
            contact.Title = Clean(input.Title);
            contact.Phone = Clean(input.Phone);
            contact.Mobile = Clean(input.Mobile);
            contact.Email = Clean(input.Email);
            contact.Note = Clean(input.Note);
            contact.IsPrimary = input.IsPrimary || inputs.Count == 1;
            contact.SortOrder = index;
            contact.UpdatedAt = DateTimeOffset.UtcNow;
        }
        db.ClientContacts.RemoveRange(existing.Where(value => !keep.Contains(value.Id)));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
