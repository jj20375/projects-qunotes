using System.Text;
using System.Text.RegularExpressions;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>
/// 報價條款（備註條款、公司預設條款）的條列格式。
/// </summary>
/// <remarks>
/// 儲存格式：一條一行、不含編號（編號由畫面與 PDF 自動產生，刪除或排序後不會亂）。
/// 使用者貼上的文字、Excel 匯入、圖像識別的結果都經過 <see cref="Normalize"/> 轉成這個格式：
/// - 辨識行首編號：<c>1.</c>、<c>1、</c>、<c>1)</c>、<c>(1)</c>、<c>一、</c>、<c>(一)</c>、項目符號（•、●、-、* …），並移除。
/// - 有編號時，沒有編號的行視為上一條的續行（Excel 為了換行把一條拆成兩列的情況），併回上一條。
/// - 完全沒有編號時，每個非空白行是一條。
/// - 第一條之前的「備註條款」「說明事項」這類標題行會略過。
/// </remarks>
public static partial class QuoteTerms
{
    public const int MaxTerms = 100;

    [GeneratedRegex(@"^\s*(?:[（(]\s*(?:[0-9０-９]{1,3}|[一二三四五六七八九十]{1,3})\s*[)）]|(?:[0-9０-９]{1,3})\s*[.．、:：)）](?![0-9０-９])|[一二三四五六七八九十]{1,3}\s*[、.．]|[•●○◆◇■□▪‧・*]|[-–—](?=\s))\s*")]
    private static partial Regex Marker();

    [GeneratedRegex(@"^[\s:：]*(備註條款|備註|附註|條款|說明事項|注意事項|報價說明|terms(\s*(and|&)\s*conditions)?|notes?)[\s:：]*$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingLine();

    /// <summary>這一行是否以條列編號或項目符號開頭（Excel 匯入用來找條款區塊）。</summary>
    public static bool StartsWithMarker(string? line) => line is not null && Marker().IsMatch(line);

    /// <summary>把任意條列文字轉成「一條一行、無編號」；沒有內容時回傳 null。</summary>
    public static string? Normalize(string? text)
    {
        var items = Split(text);
        return items.Count == 0 ? null : string.Join('\n', items);
    }

    /// <summary>拆成條款清單（不含編號）。</summary>
    public static List<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        var hasMarkers = lines.Any(line => Marker().IsMatch(line));
        var items = new List<StringBuilder>();
        foreach (var line in lines)
        {
            var match = Marker().Match(line);
            if (match.Success)
            {
                var content = line[match.Length..].Trim();
                if (content.Length > 0) items.Add(new StringBuilder(content));
                continue;
            }
            var trimmed = line.Trim();
            if (items.Count == 0 && HeadingLine().IsMatch(trimmed)) continue;
            if (hasMarkers && items.Count > 0)
            {
                Append(items[^1], trimmed);
                continue;
            }
            items.Add(new StringBuilder(trimmed));
        }
        return items.Select(item => item.ToString().Trim()).Where(item => item.Length > 0).Take(MaxTerms).ToList();
    }

    /// <summary>加上編號的文字（匯出 Excel 用）：<c>1. …</c> 一行一條。</summary>
    public static string? Numbered(string? normalized)
    {
        var items = Split(normalized);
        return items.Count == 0 ? null : string.Join('\n', items.Select((item, index) => $"{index + 1}. {item}"));
    }

    /// <summary>續行接回上一條：兩邊都是英數字時補一個空白，中文直接相連。</summary>
    private static void Append(StringBuilder previous, string continuation)
    {
        if (previous.Length > 0 && char.IsAsciiLetterOrDigit(previous[^1]) && char.IsAsciiLetterOrDigit(continuation[0])) previous.Append(' ');
        previous.Append(continuation);
    }

    /// <summary>自我測試：使用者 Excel 的條款寫法（含手動斷行的續行）、各種編號、純文字行。</summary>
    public static void AssertExamples()
    {
        void Expect(string? input, params string[] expected)
        {
            var actual = Split(input);
            if (!actual.SequenceEqual(expected)) throw new InvalidOperationException($"條款解析不正確：「{input}」→ [{string.Join(" | ", actual)}]，預期 [{string.Join(" | ", expected)}]");
        }

        // Excel 實例：第 5、7 條被拆成兩列，續行以全形逗號或空白開頭；11、12 條編號後沒有空白。
        Expect("""
            備註條款
            5. 官網架構如附件所示。若製作內容超過規範書
               ，億威會針對實際狀況另行報價。
            7. 產品保固: 驗收日起算保固一年。保固期內若有問題時，
               億威應負維修責任（人爲因素除外）。
            11.貨款未付清之前，貨物所有權歸本公司
            12.報價有效期限:60天
            """,
            "官網架構如附件所示。若製作內容超過規範書，億威會針對實際狀況另行報價。",
            "產品保固: 驗收日起算保固一年。保固期內若有問題時，億威應負維修責任（人爲因素除外）。",
            "貨款未付清之前，貨物所有權歸本公司",
            "報價有效期限:60天");
        Expect("(1) 甲\n（２）乙\n一、丙\n(三) 丁\n• 戊\n- 己", "甲", "乙", "丙", "丁", "戊", "己");
        Expect("1、付款月結 30 天\n2) 交期 60 天", "付款月結 30 天", "交期 60 天");
        // 沒有編號：每行一條；小數與年份不是編號。
        Expect("報價含運費\n\n不含安裝", "報價含運費", "不含安裝");
        Expect("1. 尺寸 2.5 公尺\n2024年起適用", "尺寸 2.5 公尺2024年起適用");
        Expect("1. Includes source code\ndelivery and training", "Includes source code delivery and training");
        Expect("   \n  ");
        if (Normalize("2. 乙\n1. 甲") != "乙\n甲") throw new InvalidOperationException("條款應保留原本順序、重新編號。");
        if (Numbered("甲\n乙") != "1. 甲\n2. 乙") throw new InvalidOperationException("條款編號輸出不正確。");
    }
}
