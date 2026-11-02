using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Clients;

/// <summary>客戶匯入每一列的處理結果。</summary>
public sealed record ClientImportRow(int Row, string Action, string? Code, string? Name, string? Message);

/// <summary>
/// 客戶 Excel 匯出與匯入。匯入先以 dryRun 預覽每列會「新增／更新／錯誤」，確認後才寫入（同一交易）。
/// </summary>
/// <remarks>
/// 比對既有客戶：有代碼用代碼，否則用統一編號；都沒有就新增。更新仍受資料範圍限制（只能改自己可管理的客戶），
/// 無權修改的列標為錯誤而不是略過，讓使用者知道哪些資料沒有進去。
/// </remarks>
public sealed partial class ClientTransferService(QuoteDbContext db, ClientService clients, NumberSequenceService numbers, AuditService audit)
{
    private static readonly (string Field, string Header)[] Columns =
    [
        ("Code", "客戶代碼"), ("Name", "客戶名稱"), ("ShortName", "簡稱"), ("TaxId", "統一編號"), ("Phone", "電話"), ("Fax", "傳真"),
        ("Email", "Email"), ("Address", "地址"), ("InvoiceTitle", "發票抬頭"), ("InvoiceAddress", "發票地址"), ("PaymentTerms", "付款條件"),
        ("Industry", "產業"), ("Status", "狀態"), ("ContactName", "主要聯絡人"), ("ContactTitle", "聯絡人職稱"), ("ContactPhone", "聯絡人電話"),
        ("ContactMobile", "聯絡人手機"), ("ContactEmail", "聯絡人Email"), ("Note", "備註"),
    ];

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex TaxIdPattern();

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Clients.AsNoTracking().OrderBy(value => value.Code).ToListAsync(cancellationToken);
        var contacts = (await db.ClientContacts.AsNoTracking().OrderByDescending(value => value.IsPrimary).ThenBy(value => value.SortOrder).ToListAsync(cancellationToken))
            .GroupBy(value => value.ClientId)
            .ToDictionary(group => group.Key, group => group.First());

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("客戶");
        for (var i = 0; i < Columns.Length; i++) sheet.Cell(1, i + 1).Value = Columns[i].Header;
        sheet.Row(1).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#EAF0F7"));
        var rowIndex = 2;
        foreach (var client in rows)
        {
            var contact = contacts.GetValueOrDefault(client.Id);
            var values = new Dictionary<string, string?>
            {
                ["Code"] = client.Code, ["Name"] = client.Name, ["ShortName"] = client.ShortName, ["TaxId"] = client.TaxId, ["Phone"] = client.Phone,
                ["Fax"] = client.Fax, ["Email"] = client.Email, ["Address"] = client.Address, ["InvoiceTitle"] = client.InvoiceTitle,
                ["InvoiceAddress"] = client.InvoiceAddress, ["PaymentTerms"] = client.PaymentTerms, ["Industry"] = client.Industry,
                ["Status"] = client.Status == "active" ? "往來中" : "停用", ["ContactName"] = contact?.Name, ["ContactTitle"] = contact?.Title,
                ["ContactPhone"] = contact?.Phone, ["ContactMobile"] = contact?.Mobile, ["ContactEmail"] = contact?.Email, ["Note"] = client.Note,
            };
            for (var i = 0; i < Columns.Length; i++)
            {
                // 統一編號、電話以文字寫入，避免 Excel 吃掉開頭的 0。
                if (values[Columns[i].Field] is string text) sheet.Cell(rowIndex, i + 1).SetValue(text);
            }
            rowIndex++;
        }
        sheet.Columns().AdjustToContents(8, 40);
        sheet.SheetView.FreezeRows(1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<ServiceResult<object>> ImportAsync(RequestScope scope, Stream stream, bool dryRun, CancellationToken cancellationToken)
    {
        List<Dictionary<string, string?>> rows;
        try
        {
            rows = ReadRows(stream);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException or InvalidOperationException)
        {
            return ServiceResult<object>.BadRequest(exception is InvalidDataException ? exception.Message : "無法讀取 Excel 檔案，請確認格式是否為 .xlsx。");
        }
        if (rows.Count > 2000) return ServiceResult<object>.BadRequest("一次最多匯入 2000 筆客戶。");

        var editable = await clients.EditableOwnerFilterAsync(scope, cancellationToken);
        var existing = await db.Clients.ToListAsync(cancellationToken);
        var byCode = existing.ToDictionary(value => value.Code, StringComparer.OrdinalIgnoreCase);
        var byTaxId = existing.Where(value => value.TaxId is not null).GroupBy(value => value.TaxId!).ToDictionary(group => group.Key, group => group.First());
        var canCreate = scope.Has("clients", "create");
        var canUpdate = scope.Has("clients", "update");
        var results = new List<ClientImportRow>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var transaction = dryRun ? null : await db.Database.BeginTransactionAsync(cancellationToken);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowNumber = index + 2;
            string? Get(string field) => row.GetValueOrDefault(field);
            var name = Get("Name");
            var code = Get("Code");
            var taxId = Get("TaxId");
            string? Fail(string message) { results.Add(new ClientImportRow(rowNumber, "error", code, name, message)); return null; }

            if (string.IsNullOrWhiteSpace(name)) { Fail("缺少客戶名稱。"); continue; }
            if (taxId is not null && !TaxIdPattern().IsMatch(taxId)) { Fail("統一編號必須是 8 碼數字。"); continue; }
            if (code is not null && !seenCodes.Add(code)) { Fail("檔案內客戶代碼重複。"); continue; }

            var client = code is not null ? byCode.GetValueOrDefault(code) : taxId is not null ? byTaxId.GetValueOrDefault(taxId) : null;
            if (client is not null && (!canUpdate || !editable(client.OwnerUserId))) { Fail("沒有修改此客戶的權限。"); continue; }
            if (client is null && !canCreate) { Fail("沒有新增客戶的權限。"); continue; }
            results.Add(new ClientImportRow(rowNumber, client is null ? "create" : "update", client?.Code ?? code, name, null));
            if (dryRun) continue;

            if (client is null)
            {
                client = new Client { Code = code ?? await numbers.NextClientCodeAsync(scope.CompanyId!.Value, cancellationToken), OwnerUserId = scope.UserId, CreatedBy = scope.UserId };
                db.Clients.Add(client);
                byCode[client.Code] = client;
            }
            client.Name = name.Trim();
            client.ShortName = Get("ShortName") ?? client.ShortName;
            client.TaxId = taxId ?? client.TaxId;
            client.Phone = Get("Phone") ?? client.Phone;
            client.Fax = Get("Fax") ?? client.Fax;
            client.Email = Get("Email") ?? client.Email;
            client.Address = Get("Address") ?? client.Address;
            client.InvoiceTitle = Get("InvoiceTitle") ?? client.InvoiceTitle;
            client.InvoiceAddress = Get("InvoiceAddress") ?? client.InvoiceAddress;
            client.PaymentTerms = Get("PaymentTerms") ?? client.PaymentTerms;
            client.Industry = Get("Industry") ?? client.Industry;
            client.Note = Get("Note") ?? client.Note;
            if (Get("Status") is string status) client.Status = status is "停用" or "inactive" ? "inactive" : "active";
            client.UpdatedBy = scope.UserId;
            client.UpdatedAt = DateTimeOffset.UtcNow;

            if (Get("ContactName") is string contactName)
            {
                var contacts = await db.ClientContacts.Where(value => value.ClientId == client.Id).ToListAsync(cancellationToken);
                var contact = contacts.FirstOrDefault(value => value.Name == contactName);
                if (contact is null)
                {
                    contact = new ClientContact { ClientId = client.Id, Name = contactName, IsPrimary = !contacts.Any(value => value.IsPrimary), SortOrder = contacts.Count };
                    db.ClientContacts.Add(contact);
                }
                contact.Title = Get("ContactTitle") ?? contact.Title;
                contact.Phone = Get("ContactPhone") ?? contact.Phone;
                contact.Mobile = Get("ContactMobile") ?? contact.Mobile;
                contact.Email = Get("ContactEmail") ?? contact.Email;
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        var summary = new { created = results.Count(value => value.Action == "create"), updated = results.Count(value => value.Action == "update"), errors = results.Count(value => value.Action == "error") };
        if (!dryRun)
        {
            audit.Add(scope.CompanyId, scope.UserId, "client", null, "import", after: summary);
            await db.SaveChangesAsync(cancellationToken);
            await transaction!.CommitAsync(cancellationToken);
        }
        return ServiceResult<object>.Ok(new { dryRun, summary, rows = results });
    }

    /// <summary>以第一列的表頭文字對應欄位（順序不拘、可省略欄位），空白列略過。</summary>
    private static List<Dictionary<string, string?>> ReadRows(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        using var workbook = new XLWorkbook(buffer);
        var sheet = workbook.Worksheets.First();
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var map = new Dictionary<int, string>();
        for (var column = 1; column <= lastColumn; column++)
        {
            var header = sheet.Cell(1, column).GetFormattedString().Trim();
            var match = Columns.FirstOrDefault(value => value.Header.Equals(header, StringComparison.OrdinalIgnoreCase));
            if (match.Field is not null) map[column] = match.Field;
        }
        if (!map.ContainsValue("Name")) throw new InvalidDataException("第一列必須有「客戶名稱」欄；可先匯出一份客戶清單當作範本。");

        var rows = new List<Dictionary<string, string?>>();
        for (var row = 2; row <= lastRow; row++)
        {
            var values = map.ToDictionary(value => value.Value, value => sheet.Cell(row, value.Key).GetFormattedString().Trim() is { Length: > 0 } text ? text : null);
            if (values.Values.All(value => value is null)) continue;
            rows.Add(values);
        }
        return rows;
    }
}
