using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>Excel 解析出的報價（匯入預覽），欄位名稱與 <see cref="QuoteInput"/> 對齊。</summary>
public sealed record ParsedQuote(
    Dictionary<string, string?> Header,
    List<ParsedQuoteItem> Items,
    List<string> Warnings,
    Dictionary<string, string> DetectedColumns,
    string SheetName,
    int HeaderRow);

public sealed record ParsedQuoteItem(int Row, string LineType, string? ItemCode, string Name, string? Spec, string? Unit, decimal? Quantity, decimal? UnitPrice, decimal? DiscountRate, decimal? UnitCost, string? Remark, decimal? SheetAmount);

/// <summary>舊報價對照設定：欄位 → 欄字母、表頭欄位 → 儲存格。</summary>
public sealed record MappingProfileSpec(string? SheetName, int HeaderRow, int DataStartRow, Dictionary<string, string> Columns, Dictionary<string, string> Cells);

/// <summary>
/// 報價 Excel：匯出與匯入範本使用同一個版面，因此匯出的檔案可以直接再匯入成新報價。
/// </summary>
/// <remarks>
/// 解析策略：沒有對照設定時，以表頭文字（含常見同義詞）找出品名、數量、單價等欄位，
/// 表頭欄位（客戶名稱、報價日期…）則尋找標籤儲存格右側的值；這讓大多數「表格式」舊報價不用設定就能匯入。
/// 格式特殊的舊報價再以 <see cref="MappingProfileSpec"/> 明確指定欄字母與儲存格。
/// </remarks>
public static partial class QuoteExcel
{
    public const string SheetName = "報價單";
    private const int HeaderRowIndex = 11;

    /// <summary>明細欄位與可辨識的表頭文字（比對時忽略空白與大小寫）。</summary>
    private static readonly (string Field, string[] Labels)[] ColumnSynonyms =
    [
        ("ItemCode", ["品號", "料號", "編號", "代碼", "code", "partno"]),
        ("Name", ["品名", "品項", "項目", "項目名稱", "工作項目", "名稱", "品名規格", "description", "item"]),
        ("Spec", ["規格", "型號", "說明", "spec", "specification"]),
        ("Unit", ["單位", "unit"]),
        ("Quantity", ["數量", "qty", "quantity"]),
        ("UnitPrice", ["單價", "unitprice", "price"]),
        ("DiscountRate", ["折扣", "折扣%", "discount"]),
        ("Amount", ["金額", "複價", "小計", "amount"]),
        ("UnitCost", ["成本", "單位成本", "cost"]),
        ("Remark", ["備註", "remark", "note"]),
    ];

    /// <summary>表頭欄位與標籤文字。</summary>
    private static readonly (string Field, string[] Labels)[] HeaderSynonyms =
    [
        ("ClientName", ["客戶名稱", "客戶", "公司名稱", "公司名", "客戶公司", "致", "to", "customer"]),
        ("ClientTaxId", ["統一編號", "統編"]),
        ("ClientAddress", ["地址", "address"]),
        ("ContactName", ["聯絡人", "收件人", "attn"]),
        ("ContactPhone", ["電話", "tel", "phone"]),
        ("ContactEmail", ["email", "e-mail"]),
        ("QuoteDate", ["報價日期", "日期", "date"]),
        ("ValidUntil", ["有效期限", "報價有效期限", "validuntil"]),
        ("Title", ["案名", "工程名稱", "專案名稱", "專案", "主旨", "project", "projectname"]),
        ("PaymentTerms", ["付款條件", "付款方式"]),
        ("DeliveryTerms", ["交貨條件", "交期"]),
        ("Terms", ["備註條款", "條款", "說明事項"]),
        ("TaxType", ["稅別"]),
        ("Discount", ["整單折扣"]),
        ("SourceQuoteNo", ["報價單號", "報價編號", "單號", "ref.no.", "ref.no", "refno", "ref"]),
    ];

    private static readonly string[] StopLabels = ["小計", "合計", "總計", "未稅", "稅額", "營業稅", "subtotal", "total"];

    [GeneratedRegex(@"[\s:：]")]
    private static partial Regex LabelNoise();

    /// <summary>標籤裡括號內的補充說明，例如「單價(未稅)」「總計（含稅）」，比對時忽略。</summary>
    [GeneratedRegex(@"\([^)]*\)|（[^）]*）")]
    private static partial Regex LabelNote();

    [GeneratedRegex(@"^(\d{2,3})[/.-](\d{1,2})[/.-](\d{1,2})$")]
    private static partial Regex RocDate();

    // ---------------------------------------------------------------- 匯出

    /// <summary>產生報價 Excel；<paramref name="quote"/> 為 null 時輸出空白範本（含兩列示範）。</summary>
    public static byte[] Write(string companyName, Quote? quote, bool includeCost)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);
        sheet.Style.Font.FontName = "Microsoft JhengHei";
        sheet.Style.Font.FontSize = 11;

        sheet.Cell("A1").Value = companyName;
        sheet.Range("A1:J1").Merge().Style.Font.SetBold().Font.SetFontSize(16).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        sheet.Cell("A2").Value = "報 價 單";
        sheet.Range("A2:J2").Merge().Style.Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        string Tax(Quote q) => q.TaxType switch
        {
            QuoteTaxTypes.Zero => "零稅率",
            QuoteTaxTypes.Exempt => "免稅",
            _ => $"{(q.TaxIncluded ? "內含" : "外加")} {q.TaxRate:0.##}%",
        };
        string Discount(Quote q) => q.DiscountType switch
        {
            QuoteDiscountTypes.Percent => $"{q.DiscountValue:0.##}%",
            QuoteDiscountTypes.Amount => q.DiscountValue.ToString("0.##", CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
        var pairs = new (string Cell, string Label, object? Value)[]
        {
            ("A4", "報價單號", quote is null ? null : quote.RevisionNo > 0 ? $"{quote.QuoteNo}-R{quote.RevisionNo}" : quote.QuoteNo),
            ("D4", "報價日期", quote?.QuoteDate.ToDateTime(TimeOnly.MinValue)),
            ("A5", "客戶名稱", quote?.ClientName),
            ("D5", "有效期限", quote?.ValidUntil?.ToDateTime(TimeOnly.MinValue)),
            ("A6", "統一編號", quote?.ClientTaxId),
            ("D6", "聯絡人", quote?.ContactName),
            ("A7", "地址", quote?.ClientAddress),
            ("D7", "電話", quote?.ContactPhone),
            ("A8", "案名", quote?.Title),
            ("D8", "Email", quote?.ContactEmail),
            ("A9", "稅別", quote is null ? "外加 5%" : Tax(quote)),
            ("D9", "整單折扣", quote is null ? null : Discount(quote)),
        };
        foreach (var (address, label, value) in pairs)
        {
            var cell = sheet.Cell(address);
            cell.Value = label;
            cell.Style.Font.SetBold();
            var valueCell = cell.CellRight();
            SetValue(valueCell, value);
            if (value is DateTime) valueCell.Style.DateFormat.Format = "yyyy/MM/dd";
        }

        var headers = new List<string> { "項次", "品號", "品名", "規格", "單位", "數量", "單價", "折扣%", "金額", "備註" };
        if (includeCost) headers.Add("成本");
        for (var i = 0; i < headers.Count; i++) sheet.Cell(HeaderRowIndex, i + 1).Value = headers[i];
        var headerRange = sheet.Range(HeaderRowIndex, 1, HeaderRowIndex, headers.Count);
        headerRange.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#EAF0F7")).Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Thin);

        var items = quote?.Items.OrderBy(value => value.SortOrder).ToList() ??
        [
            new QuoteItem { LineType = QuoteLineTypes.Section, Name = "一、設備（範例列，可刪除）" },
            new QuoteItem { ItemCode = "SRV-01", Name = "伺服器", Spec = "2U 機架式", Unit = "台", Quantity = 2, UnitPrice = 85000, Amount = 170000 },
        ];
        var row = HeaderRowIndex + 1;
        var index = 0;
        foreach (var item in items)
        {
            var isItem = item.LineType == QuoteLineTypes.Item;
            if (isItem) sheet.Cell(row, 1).Value = ++index;
            SetValue(sheet.Cell(row, 2), item.ItemCode);
            sheet.Cell(row, 3).Value = item.Name;
            SetValue(sheet.Cell(row, 4), item.Spec);
            SetValue(sheet.Cell(row, 5), item.Unit);
            if (isItem)
            {
                sheet.Cell(row, 6).Value = item.Quantity;
                sheet.Cell(row, 7).Value = item.UnitPrice;
                if (item.DiscountRate != 0) sheet.Cell(row, 8).Value = item.DiscountRate;
                sheet.Cell(row, 9).Value = item.Amount;
                if (includeCost && item.UnitCost is decimal cost) sheet.Cell(row, 11).Value = cost;
            }
            else
            {
                sheet.Cell(row, 3).Style.Font.SetBold();
            }
            SetValue(sheet.Cell(row, 10), item.Remark);
            row++;
        }
        var bodyRange = sheet.Range(HeaderRowIndex + 1, 1, Math.Max(row - 1, HeaderRowIndex + 1), headers.Count);
        bodyRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Hair);
        sheet.Range(HeaderRowIndex + 1, 6, row, 9).Style.NumberFormat.Format = "#,##0.##";

        row++;
        if (quote is not null)
        {
            foreach (var (label, value) in new (string, decimal)[] { ("小計", quote.Subtotal), ("折扣", quote.DiscountAmount), ("未稅金額", quote.NetAmount), ("稅額", quote.TaxAmount), ("總計", quote.Total) })
            {
                sheet.Cell(row, 8).Value = label;
                sheet.Cell(row, 8).Style.Font.SetBold();
                sheet.Cell(row, 9).Value = value;
                sheet.Cell(row, 9).Style.NumberFormat.Format = "#,##0.##";
                row++;
            }
            row++;
        }
        foreach (var (label, value) in new[] { ("付款條件", quote?.PaymentTerms), ("交貨條件", quote?.DeliveryTerms), ("備註條款", QuoteTerms.Numbered(quote?.Terms)) })
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 1).Style.Font.SetBold();
            SetValue(sheet.Cell(row, 2), value);
            sheet.Range(row, 2, row, 10).Merge().Style.Alignment.SetWrapText();
            row++;
        }

        foreach (var (column, width) in new[] { (1, 10), (2, 14), (3, 34), (4, 24), (5, 8), (6, 10), (7, 12), (8, 10), (9, 14), (10, 18), (11, 12) }) sheet.Column(column).Width = width;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case DateTime date:
                cell.Value = date;
                break;
            case int number:
                cell.Value = number;
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }

    // ---------------------------------------------------------------- 匯入

    public static ParsedQuote Parse(Stream stream, MappingProfileSpec? profile)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = profile?.SheetName is { Length: > 0 } name
            ? workbook.Worksheets.FirstOrDefault(value => value.Name == name) ?? throw new InvalidDataException($"找不到工作表「{name}」。")
            : workbook.Worksheets.First();
        var warnings = new List<string>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0) throw new InvalidDataException("工作表是空的。");

        int headerRow;
        Dictionary<string, int> columns;
        if (profile is not null && profile.Columns.Count > 0)
        {
            headerRow = profile.HeaderRow;
            columns = profile.Columns.ToDictionary(value => value.Key, value => XLHelper.GetColumnNumberFromLetter(value.Value.Trim().ToUpperInvariant()));
        }
        else
        {
            (headerRow, columns) = DetectColumns(sheet, Math.Min(lastRow, 60), lastColumn);
            if (headerRow == 0) throw new InvalidDataException("找不到明細表頭（需要「品名」或「項目」等欄位），請改用對照設定指定欄位。");
        }
        if (!columns.ContainsKey("Name")) throw new InvalidDataException("對照設定缺少品名欄位。");
        if (!columns.ContainsKey("Quantity") || !columns.ContainsKey("UnitPrice")) warnings.Add("沒有找到數量或單價欄位，這些欄位會是空白，請在預覽中補上。");

        var items = new List<ParsedQuoteItem>();
        var startRow = profile?.DataStartRow > 0 ? profile.DataStartRow : headerRow + 1;
        var blankRun = 0;
        var endRow = startRow;
        for (var row = startRow; row <= lastRow; row++)
        {
            string? Text(string field) => columns.TryGetValue(field, out var column) ? CellText(sheet.Cell(row, column)) : null;
            decimal? Number(string field) => columns.TryGetValue(field, out var column) ? CellNumber(sheet.Cell(row, column)) : null;
            var itemName = Text("Name");
            endRow = row;
            if (IsStopRow(sheet, row, lastColumn)) break;
            if (string.IsNullOrWhiteSpace(itemName))
            {
                if (++blankRun >= 3) break;
                continue;
            }
            blankRun = 0;
            var quantity = Number("Quantity");
            var unitPrice = Number("UnitPrice");
            var discount = Number("DiscountRate");
            var amount = Number("Amount");
            var lineType = quantity is null && unitPrice is null ? QuoteLineTypes.Section : QuoteLineTypes.Item;
            if (lineType == QuoteLineTypes.Item && amount is decimal sheetAmount && quantity is decimal q && unitPrice is decimal p)
            {
                var computed = QuoteCalculator.Round(q * p * (1 - (discount ?? 0) / 100m), 2);
                if (Math.Abs(computed - sheetAmount) >= 1) warnings.Add($"第 {row} 列金額與數量 × 單價不符（Excel {sheetAmount:#,0.##}／計算 {computed:#,0.##}），匯入後以計算結果為準。");
            }
            if (quantity < 0 || unitPrice < 0) warnings.Add($"第 {row} 列有負數，請確認。");
            items.Add(new ParsedQuoteItem(row, lineType, Text("ItemCode"), itemName.Trim(), Text("Spec"), Text("Unit"), quantity, unitPrice, discount, Number("UnitCost"), Text("Remark"), amount));
        }
        if (items.Count == 0) warnings.Add("沒有讀到任何明細列。");

        var header = profile is not null && profile.Cells.Count > 0
            ? profile.Cells.ToDictionary(value => value.Key, value => CellText(sheet.Cell(value.Value.Trim().ToUpperInvariant())))
            : DetectHeader(sheet, headerRow, endRow, lastRow, lastColumn);
        // 沒有「備註條款」標籤時，從明細下方找編號條列（1. 2. …）當作條款。
        if (string.IsNullOrWhiteSpace(header.GetValueOrDefault("Terms")) && DetectTerms(sheet, endRow, lastRow, lastColumn) is string terms) header["Terms"] = terms;
        NormalizeHeader(header, warnings);

        var detected = columns.ToDictionary(value => value.Key, value => XLHelper.GetColumnLetterFromNumber(value.Value));
        return new ParsedQuote(header, items, warnings, detected, sheet.Name, headerRow);
    }

    private static (int Row, Dictionary<string, int> Columns) DetectColumns(IXLWorksheet sheet, int scanRows, int lastColumn)
    {
        for (var row = 1; row <= scanRows; row++)
        {
            var found = new Dictionary<string, int>();
            for (var column = 1; column <= lastColumn; column++)
            {
                var label = Normalize(CellText(sheet.Cell(row, column)));
                if (label.Length == 0) continue;
                var match = ColumnSynonyms.FirstOrDefault(value => !found.ContainsKey(value.Field) && value.Labels.Contains(label));
                if (match.Field is not null) found[match.Field] = column;
            }
            // 至少要有品名，再加上數量或單價其中之一，才視為明細表頭，避免把表頭區的「項目」誤判。
            if (found.ContainsKey("Name") && (found.ContainsKey("Quantity") || found.ContainsKey("UnitPrice"))) return (row, found);
        }
        return (0, []);
    }

    /// <summary>
    /// 明細下方的條款：找出「連續、沒有空白列隔開」且含編號行的區塊。只取區塊內的文字，
    /// 所以下方隔了空白列的簽章欄（公司名稱、客戶確認簽章）不會被當成條款。續行由 <see cref="QuoteTerms"/> 併回上一條。
    /// </summary>
    private static string? DetectTerms(IXLWorksheet sheet, int afterRow, int lastRow, int lastColumn)
    {
        var collected = new List<string>();
        var block = new List<string>();
        void Flush()
        {
            if (block.Any(QuoteTerms.StartsWithMarker)) collected.AddRange(block);
            block.Clear();
        }
        for (var row = afterRow + 1; row <= lastRow; row++)
        {
            var texts = Enumerable.Range(1, lastColumn).Select(column => CellText(sheet.Cell(row, column))).Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            if (texts.Count == 0)
            {
                Flush();
                continue;
            }
            // 保留行首空白：續行常以空白縮排，QuoteTerms 會再整理。
            var raw = Enumerable.Range(1, lastColumn).Select(column => sheet.Cell(row, column)).FirstOrDefault(cell => !cell.IsEmpty())?.GetString();
            block.Add(string.IsNullOrWhiteSpace(raw) ? string.Join(' ', texts) : raw);
        }
        Flush();
        return collected.Count == 0 ? null : string.Join('\n', collected);
    }

    private static Dictionary<string, string?> DetectHeader(IXLWorksheet sheet, int headerRow, int dataEndRow, int lastRow, int lastColumn)
    {
        var header = new Dictionary<string, string?>();
        var rows = Enumerable.Range(1, Math.Max(headerRow - 1, 0)).Concat(Enumerable.Range(dataEndRow, Math.Max(lastRow - dataEndRow + 1, 0)));
        foreach (var row in rows)
        {
            for (var column = 1; column <= lastColumn; column++)
            {
                var raw = CellText(sheet.Cell(row, column));
                if (string.IsNullOrWhiteSpace(raw)) continue;
                // 「客戶名稱：某某公司」寫在同一格時，冒號後就是值。
                var parts = raw.Split(['：', ':'], 2);
                var label = Normalize(parts[0]);
                var match = HeaderSynonyms.FirstOrDefault(value => !header.ContainsKey(value.Field) && value.Labels.Contains(label));
                if (match.Field is null) continue;
                var value = parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1])
                    ? parts[1].Trim()
                    : Enumerable.Range(column + 1, 3).Select(next => CellText(sheet.Cell(row, next))).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                if (value is not null) header[match.Field] = value;
            }
        }
        return header;
    }

    /// <summary>把稅別、整單折扣、日期轉成 QuoteInput 使用的格式。</summary>
    internal static void NormalizeHeader(Dictionary<string, string?> header, List<string> warnings)
    {
        if (header.TryGetValue("Terms", out var terms)) header["Terms"] = QuoteTerms.Normalize(terms);
        foreach (var field in new[] { "QuoteDate", "ValidUntil" })
        {
            if (!header.TryGetValue(field, out var text) || string.IsNullOrWhiteSpace(text)) continue;
            var date = ParseDate(text);
            header[field] = date?.ToString("yyyy-MM-dd");
            if (date is null) warnings.Add($"無法辨識日期「{text}」，已略過。");
        }
        if (header.TryGetValue("TaxType", out var tax) && !string.IsNullOrWhiteSpace(tax))
        {
            header["TaxType"] = tax.Contains("免稅") ? QuoteTaxTypes.Exempt : tax.Contains("零稅") ? QuoteTaxTypes.Zero : QuoteTaxTypes.Taxable;
            header["TaxIncluded"] = tax.Contains("內含") || tax.Contains("含稅") && !tax.Contains("未含") ? "true" : "false";
            var rate = Regex.Match(tax, @"(\d+(\.\d+)?)\s*%");
            if (rate.Success) header["TaxRate"] = rate.Groups[1].Value;
        }
        if (header.TryGetValue("Discount", out var discount) && !string.IsNullOrWhiteSpace(discount))
        {
            var clean = discount.Replace(",", string.Empty).Trim();
            if (clean.EndsWith('%') && decimal.TryParse(clean.TrimEnd('%'), CultureInfo.InvariantCulture, out var percent))
            {
                header["DiscountType"] = QuoteDiscountTypes.Percent;
                header["DiscountValue"] = percent.ToString(CultureInfo.InvariantCulture);
            }
            else if (decimal.TryParse(clean, CultureInfo.InvariantCulture, out var amount))
            {
                header["DiscountType"] = QuoteDiscountTypes.Amount;
                header["DiscountValue"] = amount.ToString(CultureInfo.InvariantCulture);
            }
        }
        header.Remove("Discount");
    }

    private static bool IsStopRow(IXLWorksheet sheet, int row, int lastColumn) =>
        Enumerable.Range(1, lastColumn).Any(column => StopLabels.Contains(Normalize(CellText(sheet.Cell(row, column)))));

    public static DateOnly? ParseDate(string text)
    {
        text = text.Trim();
        // 民國年：114/10/29
        var roc = RocDate().Match(text);
        if (roc.Success && int.Parse(roc.Groups[1].Value) < 1000)
        {
            try
            {
                return new DateOnly(int.Parse(roc.Groups[1].Value) + 1911, int.Parse(roc.Groups[2].Value), int.Parse(roc.Groups[3].Value));
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        return DateTime.TryParse(text, CultureInfo.GetCultureInfo("zh-TW"), DateTimeStyles.None, out var date) ? DateOnly.FromDateTime(date) : null;
    }

    private static string Normalize(string? text) => LabelNoise().Replace(LabelNote().Replace(text ?? string.Empty, string.Empty), string.Empty).ToLowerInvariant();

    private static string? CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        try
        {
            if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime().ToString("yyyy-MM-dd");
            var text = cell.GetFormattedString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception)
        {
            // 公式無法計算等情況，退回原始文字。
            return cell.CachedValue.ToString();
        }
    }

    private static decimal? CellNumber(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        try
        {
            if (cell.DataType == XLDataType.Number) return (decimal)cell.GetDouble();
        }
        catch (Exception)
        {
            // 公式計算失敗時改用文字解析。
        }
        var text = (CellText(cell) ?? string.Empty).Replace(",", string.Empty).Replace("NT$", string.Empty).Replace("$", string.Empty).Replace("%", string.Empty).Trim();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>
    /// 供 <c>--self-test</c> 驗證：使用者實際的舊報價版面（標籤與值同一格「公司名: …」、表頭「單價(未稅)」、
    /// 總計(未稅)／(含稅) 兩列、總計下方以編號條列且有手動斷行的條款、隔著空白列的簽章欄）能正確解析。
    /// </summary>
    public static void AssertLegacyLayout()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("義大汽車客運-官網建置");
        sheet.Cell("A2").Value = " 億威電子系統股份有限公司";
        sheet.Cell("A5").Value = "報價單";
        sheet.Cell("B9").Value = "公司名: 義大遊覽車股份有限公司";
        sheet.Cell("F9").Value = "日期   : 04/03/2024";
        sheet.Cell("B10").Value = "聯絡人: 黃奕章";
        sheet.Cell("F10").Value = "Ref.No.: DB-20240403";
        sheet.Cell("B11").Value = "電話  : 07-615-1111 #2236";
        sheet.Cell("F11").Value = "Revision : 00";
        sheet.Cell("B12").Value = "E-mail: yu64550@e-united.com.tw";
        sheet.Cell("B13").Value = "專案: 義大遊覽車-官網建置";
        string[] heads = ["項目", "數量", "單位", "單價(未稅)", "小計(未稅)", "備註"];
        for (var i = 0; i < heads.Length; i++) sheet.Cell(16, i + 2).Value = heads[i];
        var lines = new (string Name, int Price)[] { ("官網結構及畫面設計", 35000), ("前端網頁開發", 40000), ("資料庫搭建", 5000), ("原始碼(Source Code)授權交付", 10000) };
        for (var i = 0; i < lines.Length; i++)
        {
            var row = 17 + i;
            sheet.Cell(row, 2).Value = lines[i].Name;
            sheet.Cell(row, 3).Value = 1;
            sheet.Cell(row, 4).Value = "式";
            sheet.Cell(row, 5).Value = lines[i].Price;
            sheet.Cell(row, 6).FormulaA1 = $"E{row}*C{row}";
        }
        sheet.Cell("B21").Value = "總計(未稅)";
        sheet.Cell("F21").FormulaA1 = "SUM(F17:F20)";
        sheet.Cell("B22").Value = "總計(含稅)";
        sheet.Cell("F22").FormulaA1 = "F21*1.05";
        sheet.Cell("B26").Value = "1. 建置官網屬於RWD響應式設計";
        sheet.Cell("B27").Value = "2. 官網架構如附件所示。若製作內容超過規範書";
        sheet.Cell("B28").Value = "   ，億威會針對實際狀況與處理方式，另行報價。";
        sheet.Cell("B29").Value = "3. 付款條件:訂單成立後，軟體交付付款90%";
        sheet.Cell("B30").Value = "4.報價有效期限:60天";
        sheet.Cell("B34").Value = "億威電子系統股份有限公司";
        sheet.Cell("F34").Value = "客戶確認簽章";
        sheet.Cell("B38").Value = "Authorized Signature";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var parsed = Parse(stream, null);
        var problems = new List<string>();
        if (parsed.Items.Count != 4) problems.Add($"明細 {parsed.Items.Count} 列（應為 4，總計列不可匯入）");
        if (parsed.Items.Any(item => item.LineType != QuoteLineTypes.Item || item.UnitPrice is null || item.Quantity != 1)) problems.Add("單價(未稅) 欄沒有被辨識");
        if (parsed.Items.ElementAtOrDefault(3)?.Name != "原始碼(Source Code)授權交付") problems.Add("品名內的括號不可被移除");
        if (parsed.Header.GetValueOrDefault("ClientName") != "義大遊覽車股份有限公司") problems.Add("公司名");
        if (parsed.Header.GetValueOrDefault("Title") != "義大遊覽車-官網建置") problems.Add("專案");
        if (parsed.Header.GetValueOrDefault("SourceQuoteNo") != "DB-20240403") problems.Add("Ref.No.");
        if (parsed.Header.GetValueOrDefault("ContactPhone") != "07-615-1111 #2236") problems.Add("電話");
        var terms = QuoteTerms.Split(parsed.Header.GetValueOrDefault("Terms"));
        if (terms.Count != 4 || terms[1] != "官網架構如附件所示。若製作內容超過規範書，億威會針對實際狀況與處理方式，另行報價。" || terms[3] != "報價有效期限:60天")
        {
            problems.Add($"條款：[{string.Join(" | ", terms)}]");
        }
        if (parsed.Header.ContainsKey("PaymentTerms")) problems.Add("條款內的「付款條件:」不可被當成表頭欄位");
        if (parsed.Warnings.Count > 0) problems.Add($"提醒：{string.Join(" / ", parsed.Warnings)}");
        if (problems.Count > 0) throw new InvalidOperationException($"舊報價版面解析失敗：{string.Join("；", problems)}");
    }

    /// <summary>供 <c>--self-test</c> 驗證：匯出的報價可以被解析回相同明細與表頭。</summary>
    public static void AssertRoundTrip()
    {
        var quote = new Quote
        {
            QuoteNo = "Q202610-0001",
            ClientName = "測試客戶股份有限公司",
            ClientTaxId = "24536806",
            ContactName = "陳經理",
            Title = "機房工程",
            QuoteDate = new DateOnly(2026, 10, 29),
            ValidUntil = new DateOnly(2026, 11, 28),
            TaxIncluded = false,
            TaxRate = 5,
            DiscountType = QuoteDiscountTypes.Percent,
            DiscountValue = 10,
            PaymentTerms = "月結 30 天",
            Terms = "驗收後保固一年\n報價有效期限 30 天",
            Items =
            [
                new QuoteItem { SortOrder = 0, LineType = QuoteLineTypes.Section, Name = "一、設備" },
                new QuoteItem { SortOrder = 1, ItemCode = "A1", Name = "伺服器", Unit = "台", Quantity = 2, UnitPrice = 1500, UnitCost = 1000 },
                new QuoteItem { SortOrder = 2, Name = "安裝", Unit = "式", Quantity = 1, UnitPrice = 999.5m, DiscountRate = 10 },
            ],
        };
        QuoteCalculator.Apply(quote);
        using var stream = new MemoryStream(Write("示範公司", quote, includeCost: true));
        var parsed = Parse(stream, null);
        var ok = parsed.Items.Count == 3
            && parsed.Items[0].LineType == QuoteLineTypes.Section
            && parsed.Items[1] is { ItemCode: "A1", Name: "伺服器", Quantity: 2, UnitPrice: 1500, UnitCost: 1000 }
            && parsed.Items[2].DiscountRate == 10
            && parsed.Header.GetValueOrDefault("ClientName") == "測試客戶股份有限公司"
            && parsed.Header.GetValueOrDefault("QuoteDate") == "2026-10-29"
            && parsed.Header.GetValueOrDefault("ValidUntil") == "2026-11-28"
            && parsed.Header.GetValueOrDefault("TaxType") == QuoteTaxTypes.Taxable
            && parsed.Header.GetValueOrDefault("TaxIncluded") == "false"
            && parsed.Header.GetValueOrDefault("DiscountType") == QuoteDiscountTypes.Percent
            && parsed.Header.GetValueOrDefault("PaymentTerms") == "月結 30 天"
            && parsed.Header.GetValueOrDefault("Terms") == "驗收後保固一年\n報價有效期限 30 天"
            && parsed.Warnings.Count == 0;
        if (!ok) throw new InvalidOperationException($"報價 Excel 往返驗證失敗：{JsonSerializer.Serialize(parsed)}");
        if (ParseDate("114/10/29") != new DateOnly(2025, 10, 29)) throw new InvalidOperationException("民國日期解析失敗。");
    }
}
