using System.Text;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>報價單 PDF 的公司資訊（抬頭）。<paramref name="Logo"/> 為圖檔內容，沒有時不印 Logo。</summary>
public sealed record QuotePdfCompany(string Name, string? TaxId, string? Phone, string? Address, string? EnglishName = null, byte[]? Logo = null);

/// <summary>
/// 報價單 PDF（A4 直式）。
/// </summary>
/// <remarks>
/// 中文字型使用系統字型並依序 fallback：Docker 映像安裝 fonts-noto-cjk（Noto Sans CJK TC），
/// Mac 開發機為蘋方／黑體，Windows 為微軟正黑體；不把字型檔放進 repo。
/// QuestPDF 使用 Community 授權（年營收 100 萬美元以下免費），授權宣告在 Program.cs。
///
/// 分頁規則（避免切開內容、避免沒必要的分頁）：
/// - 公司抬頭與明細表頭每頁重複；頁尾印「單號、版次、第 x / y 頁」。
/// - 明細的每一列、總計區塊、每一條條款都不拆到兩頁（<c>PreventPageBreak</c>）。
/// - 段落標題至少帶著下一列一起換頁（<c>EnsureSpace</c>），不會單獨留在頁底。
/// - 簽章欄與它前面最後兩個區塊（例如最後兩條條款）綁在一起，新的一頁不會只有簽章欄。
/// - 先以一般版面產生；超過一頁時改用緊湊版面（字級、間距略小）再排一次，頁數變少才採用。
/// </remarks>
public static partial class QuotePdf
{
    /// <summary>中文字型候選（依優先順序）；實際使用的是其中在目前環境找得到的。</summary>
    private static readonly string[] FontCandidates = ["Noto Sans CJK TC", "Noto Sans TC", "PingFang TC", "Heiti TC", "Microsoft JhengHei"];
    private static string[] fonts = [];
    private const string Primary = "#1F4E79";

    /// <summary>版面尺寸；緊湊版只在一般版面超過一頁時嘗試。</summary>
    private sealed record Layout(float FontSize, float Margin, float CellPadding, float Spacing, float SignatureSpace)
    {
        public static readonly Layout Normal = new(9.5f, 32, 4, 10, 28);
        public static readonly Layout Compact = new(9f, 26, 2.5f, 7, 22);
    }

    [GeneratedRegex(@"/Type\s*/Page(?![a-zA-Z])")]
    private static partial Regex PageObject();

    private static TextBlockDescriptor BoldIf(this TextBlockDescriptor text, bool condition) => condition ? text.Bold() : text;

    /// <summary>
    /// 啟動時呼叫：註冊指定目錄下的字型檔（.ttf／.otf／.ttc），再從候選清單挑出目前環境實際存在的字型。
    /// QuestPDF 要求清單內每個字型都存在，所以不能直接把全部候選丟進 FontFamily。
    /// </summary>
    public static void ConfigureFonts(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory).Where(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = File.OpenRead(file);
                FontManager.RegisterFontFromStream(stream);
            }
        }
        var available = FontManager.GetRegisteredFonts()
            .Concat(QuestPDF.Settings.UseSystemFonts ? FontManager.GetSystemFonts() : [])
            .Select(value => value.FamilyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        fonts = FontCandidates.Where(available.Contains).ToArray();
    }

    public static byte[] Render(QuotePdfCompany company, Quote quote)
    {
        if (fonts.Length == 0) throw new InvalidOperationException("找不到中文字型，無法產生報價 PDF。請安裝 Noto Sans CJK TC（Debian／Ubuntu：fonts-noto-cjk），或以 Quote__PdfFontDirectory 指定字型檔目錄。");
        // Logo 壞檔不應讓整份報價失敗：先試著解碼，失敗就不印 Logo。
        if (company.Logo is not null && !CanDecode(company.Logo)) company = company with { Logo = null };
        var normal = Build(company, quote, Layout.Normal);
        var normalPages = CountPages(normal);
        if (normalPages < 2) return normal;
        var compact = Build(company, quote, Layout.Compact);
        return CountPages(compact) < normalPages ? compact : normal;
    }

    /// <summary>PDF 的頁數（計算頁面物件）。</summary>
    public static int CountPages(byte[] pdf) => PageObject().Matches(Encoding.Latin1.GetString(pdf)).Count;

    private static bool CanDecode(byte[] bytes)
    {
        try
        {
            QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static byte[] Build(QuotePdfCompany company, Quote quote, Layout layout)
    {
        var digits = QuoteCalculator.Digits(quote.Currency);
        string Money(decimal value) => value.ToString(digits == 0 ? "#,##0" : "#,##0.00");
        string Number(decimal value) => value.ToString("#,##0.####");
        var revision = quote.RevisionNo.ToString("00");
        var taxLabel = quote.TaxType switch
        {
            QuoteTaxTypes.Zero => "零稅率",
            QuoteTaxTypes.Exempt => "免稅",
            _ => $"營業稅 {quote.TaxRate:0.##}%{(quote.TaxIncluded ? "（內含）" : string.Empty)}",
        };
        var items = quote.Items.OrderBy(value => value.SortOrder).ToList();
        var showRemark = items.Any(item => item.LineType == QuoteLineTypes.Item && !string.IsNullOrWhiteSpace(item.Remark));
        var terms = QuoteTerms.Split(quote.Terms);

        // 明細之後的區塊依序排列（繪製方式、與上一個區塊的間距）；最後幾個與簽章欄綁在一起（見類別說明）。
        var tail = new List<(Action<IContainer> Draw, float Gap)>
        {
            (container => container.AlignRight().Width(230).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                void Line(string label, decimal value, bool strong = false)
                {
                    table.Cell().PaddingVertical(2).Text(label).BoldIf(strong);
                    table.Cell().PaddingVertical(2).AlignRight().Text($"{quote.Currency} {Money(value)}").BoldIf(strong).FontSize(strong ? layout.FontSize + 1.5f : layout.FontSize);
                }
                Line("小計", quote.Subtotal);
                if (quote.DiscountAmount != 0) Line(quote.DiscountType == QuoteDiscountTypes.Percent ? $"折扣 {quote.DiscountValue:0.##}%" : "折扣", -quote.DiscountAmount);
                Line("未稅金額", quote.NetAmount);
                Line(taxLabel, quote.TaxAmount);
                Line("總計", quote.Total, strong: true);
            }), 0),
        };
        foreach (var (label, value) in new[] { ("付款條件", quote.PaymentTerms), ("交貨條件", quote.DeliveryTerms) })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            tail.Add((container => container.Column(block =>
            {
                block.Item().Text(label).Bold();
                block.Item().Text(value);
            }), layout.Spacing));
        }
        void Term(IContainer container, int number, string text) => container.Row(row =>
        {
            // 編號固定一欄、內文自成一欄：換行時第二行對齊內文，不會跑到編號底下。
            row.ConstantItem(22).Text($"{number}.");
            row.RelativeItem().Text(text);
        });
        for (var i = 0; i < terms.Count; i++)
        {
            var number = i + 1;
            var text = terms[i];
            // 標題與第一條放在一起，標題不會單獨留在頁底。
            tail.Add(i == 0
                ? (container => container.Column(block =>
                {
                    block.Spacing(3);
                    block.Item().Text("備註條款").Bold();
                    block.Item().Element(item => Term(item, number, text));
                }), layout.Spacing)
                : (container => Term(container, number, text), 3));
        }
        void Signature(IContainer container) => container.PaddingTop(layout.Spacing * 1.5f).Row(row =>
        {
            row.RelativeItem().Column(sign =>
            {
                sign.Item().Text(company.Name).Bold();
                sign.Item().PaddingTop(layout.SignatureSpace).LineHorizontal(0.5f);
                sign.Item().PaddingTop(2).Text("Authorized Signature").FontSize(layout.FontSize - 1).FontColor(Colors.Grey.Darken1);
            });
            row.ConstantItem(40);
            row.RelativeItem().Column(sign =>
            {
                sign.Item().Text("客戶確認簽章").Bold();
                sign.Item().PaddingTop(layout.SignatureSpace).LineHorizontal(0.5f);
                sign.Item().PaddingTop(2).Text("Customer Signature").FontSize(layout.FontSize - 1).FontColor(Colors.Grey.Darken1);
            });
        });
        var keepWithSignature = Math.Min(2, tail.Count);

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(layout.Margin);
            page.DefaultTextStyle(style => style.FontFamily(fonts).FontSize(layout.FontSize));

            page.Header().Column(column =>
            {
                column.Item().Row(row =>
                {
                    if (company.Logo is not null) row.ConstantItem(60).Height(44).AlignMiddle().Image(company.Logo).FitArea();
                    if (company.Logo is not null) row.ConstantItem(10);
                    row.RelativeItem().Column(info =>
                    {
                        info.Item().Text(company.Name).FontSize(layout.FontSize + 6.5f).Bold().FontColor(Primary);
                        if (company.EnglishName is not null) info.Item().Text(company.EnglishName).FontSize(layout.FontSize + 0.5f).FontColor(Primary);
                        var line = string.Join("　", new[] { company.TaxId is null ? null : $"統一編號 {company.TaxId}", company.Phone is null ? null : $"電話 {company.Phone}" }.Where(value => value is not null));
                        if (line.Length > 0) info.Item().Text(line).FontColor(Colors.Grey.Darken1);
                        if (company.Address is not null) info.Item().Text(company.Address).FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(170).AlignRight().Column(title =>
                    {
                        title.Item().AlignRight().Text("報 價 單").FontSize(layout.FontSize + 10.5f).Bold();
                        title.Item().AlignRight().Text($"No. {quote.QuoteNo}");
                        title.Item().AlignRight().Text($"Rev. {revision}").FontColor(Colors.Grey.Darken1);
                    });
                });
                column.Item().PaddingVertical(layout.Spacing * 0.8f).LineHorizontal(1.5f).LineColor(Primary);
            });

            page.Content().Column(column =>
            {
                column.Spacing(layout.Spacing);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(60);
                        columns.RelativeColumn(3);
                        columns.ConstantColumn(60);
                        columns.RelativeColumn(2);
                    });
                    void Pair(string label, string? value)
                    {
                        table.Cell().PaddingVertical(layout.CellPadding / 2).Text(label).FontColor(Colors.Grey.Darken1);
                        table.Cell().PaddingVertical(layout.CellPadding / 2).Text(value ?? "—");
                    }
                    Pair("客戶", quote.ClientName);
                    Pair("報價日期", quote.QuoteDate.ToString("yyyy/MM/dd"));
                    Pair("統一編號", quote.ClientTaxId);
                    Pair("有效期限", quote.ValidUntil?.ToString("yyyy/MM/dd"));
                    Pair("聯絡人", quote.ContactName);
                    Pair("電話", quote.ContactPhone);
                    Pair("地址", quote.ClientAddress);
                    Pair("Email", quote.ContactEmail);
                    Pair("案名", quote.Title);
                    Pair("版次", revision);
                });

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(24);
                        columns.RelativeColumn(4);
                        columns.ConstantColumn(34);
                        columns.ConstantColumn(46);
                        columns.ConstantColumn(66);
                        columns.ConstantColumn(74);
                        if (showRemark) columns.RelativeColumn(2);
                    });
                    var headers = new List<(string Text, bool Right)> { ("#", false), ("品名／規格", false), ("單位", false), ("數量", true), ("單價", true), ("金額", true) };
                    if (showRemark) headers.Add(("備註", false));
                    var columnCount = headers.Count;
                    table.Header(header =>
                    {
                        foreach (var (text, right) in headers)
                        {
                            var cell = header.Cell().Background(Primary).PaddingVertical(layout.CellPadding + 1).PaddingHorizontal(4);
                            (right ? cell.AlignRight() : cell).Text(text).FontColor(Colors.White).Bold();
                        }
                    });

                    var index = 0;
                    foreach (var item in items)
                    {
                        if (item.LineType != QuoteLineTypes.Item)
                        {
                            // 段落標題至少要和下一列一起放得下，否則整個移到下一頁。
                            table.Cell().ColumnSpan((uint)columnCount).EnsureSpace(item.LineType == QuoteLineTypes.Section ? 48 : 20)
                                .Background(Colors.Grey.Lighten4).PaddingVertical(layout.CellPadding).PaddingHorizontal(4)
                                .Text(item.Name).BoldIf(item.LineType == QuoteLineTypes.Section).Italic(item.LineType == QuoteLineTypes.Note);
                            continue;
                        }
                        // 同一列的儲存格都不拆頁：放不下時整列移到下一頁，品名與規格說明不會分在兩頁。
                        IContainer Cell() => table.Cell().PreventPageBreak().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(layout.CellPadding).PaddingHorizontal(4);
                        Cell().Text((++index).ToString());
                        Cell().Column(name =>
                        {
                            name.Item().Text(item.ItemCode is null ? item.Name : $"{item.Name}（{item.ItemCode}）");
                            if (item.Spec is not null) name.Item().Text(item.Spec).FontSize(layout.FontSize - 1).FontColor(Colors.Grey.Darken1);
                            if (item.DiscountRate != 0) name.Item().Text($"折扣 {item.DiscountRate:0.##}%").FontSize(layout.FontSize - 1).FontColor(Colors.Grey.Darken1);
                            if (!showRemark && item.Remark is not null) name.Item().Text($"備註：{item.Remark}").FontSize(layout.FontSize - 1).FontColor(Colors.Grey.Darken1);
                        });
                        Cell().Text(item.Unit ?? string.Empty);
                        Cell().AlignRight().Text(Number(item.Quantity));
                        Cell().AlignRight().Text(Number(item.UnitPrice));
                        Cell().AlignRight().Text(Money(item.Amount));
                        if (showRemark) Cell().Text(item.Remark ?? string.Empty).FontSize(layout.FontSize - 1);
                    }
                });

                // 明細之後的區塊放在同一欄，各自控制間距（條款之間較緊）；每個區塊不拆頁。
                column.Item().Column(rest =>
                {
                    for (var i = 0; i < tail.Count - keepWithSignature; i++)
                    {
                        rest.Item().PaddingTop(tail[i].Gap).PreventPageBreak().Element(tail[i].Draw);
                    }
                    rest.Item().PreventPageBreak().Column(group =>
                    {
                        foreach (var (draw, gap) in tail.Skip(tail.Count - keepWithSignature)) group.Item().PaddingTop(gap).Element(draw);
                        group.Item().Element(Signature);
                    });
                });
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span($"{quote.QuoteNo}　Rev.{revision}　第 ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
                text.Span(" 頁");
            });
        })).GeneratePdf();
    }
}
