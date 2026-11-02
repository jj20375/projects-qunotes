using System.Text.RegularExpressions;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 單號規則：以代號組成，例如 <c>Q{yyyy}{MM}-{seq:4}</c> → <c>Q202604-0001</c>、<c>DB-{yyyy}{MM}{dd}-{seq:2}</c> → <c>DB-20240403-01</c>。
/// </summary>
/// <remarks>
/// 代號：<c>{yyyy}</c> 西元年、<c>{yy}</c> 年後兩碼、<c>{MM}</c> 月、<c>{dd}</c> 日、<c>{seq}</c> 或 <c>{seq:n}</c> 流水號（補零到 n 碼，1～6）。
/// 一定要有流水號，否則同一天第二張報價會重號。流水號依規則中最細的日期單位重新起算：有 {dd} 每天、有 {MM} 每月、有年份每年，都沒有則不歸零。
/// 其餘文字只允許英數字與 <c>- _ / .</c>，總長 40 字以內。
/// </remarks>
public static partial class DocumentNumberPattern
{
    public const string DefaultQuote = "Q{yyyy}{MM}-{seq:4}";

    [GeneratedRegex(@"\{(yyyy|yy|MM|dd|seq(?::([1-6]))?)\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"^[A-Za-z0-9\-_/.]*$")]
    private static partial Regex Literal();

    /// <summary>檢查規則；正確時回傳 null，否則回傳繁中說明。</summary>
    public static string? Validate(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "請輸入單號規則。";
        if (pattern.Length > 40) return "單號規則最多 40 字。";
        var tokens = Token().Matches(pattern);
        if (tokens.Count(token => token.Groups[1].Value.StartsWith("seq")) != 1) return "單號規則必須有一個流水號 {seq} 或 {seq:4}，否則會重號。";
        if (!Literal().IsMatch(Token().Replace(pattern, string.Empty))) return "單號規則的文字只能使用英數字與 - _ / .，代號限 {yyyy} {yy} {MM} {dd} {seq} {seq:n}（n 為 1～6）。";
        if (Format(pattern, new DateOnly(2099, 12, 31), 999999).Length > 50) return "依這個規則產生的單號太長（上限 50 字）。";
        return null;
    }

    /// <summary>流水號歸零的區間鍵：有日就每天、有月就每月、有年就每年，否則 <c>all</c>。</summary>
    public static string Period(string pattern, DateOnly date) =>
        pattern.Contains("{dd}") ? date.ToString("yyyyMMdd")
        : pattern.Contains("{MM}") ? date.ToString("yyyyMM")
        : pattern.Contains("{yyyy}") || pattern.Contains("{yy}") ? date.ToString("yyyy")
        : "all";

    public static string Format(string pattern, DateOnly date, long sequence) =>
        Token().Replace(pattern, match => match.Groups[1].Value switch
        {
            "yyyy" => date.ToString("yyyy"),
            "yy" => date.ToString("yy"),
            "MM" => date.ToString("MM"),
            "dd" => date.ToString("dd"),
            _ => sequence.ToString().PadLeft(match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1, '0'),
        });

    public static void AssertExamples()
    {
        var date = new DateOnly(2024, 4, 3);
        if (Format(DefaultQuote, date, 1) != "Q202404-0001" || Period(DefaultQuote, date) != "202404") throw new InvalidOperationException("單號規則：預設格式不正確");
        if (Format("DB-{yyyy}{MM}{dd}-{seq:2}", date, 3) != "DB-20240403-03" || Period("DB-{yyyy}{MM}{dd}-{seq:2}", date) != "20240403") throw new InvalidOperationException("單號規則：日期格式不正確");
        if (Period("QT{seq:5}", date) != "all" || Format("QT{seq:5}", date, 12) != "QT00012") throw new InvalidOperationException("單號規則：不歸零格式不正確");
        if (Validate(DefaultQuote) is not null || Validate("DB-{yyyy}{MM}{dd}-{seq:2}") is not null) throw new InvalidOperationException("單號規則：合法規則被拒絕");
        string?[] invalid = ["", "DB-{yyyy}{MM}{dd}", "Q{seq}{seq}", "Q 報價{seq}", "Q{seq:9}", "Q{hh}{seq}", new string('Q', 41)];
        if (invalid.Any(value => Validate(value) is null)) throw new InvalidOperationException("單號規則：不合法的規則沒有被擋下");
    }
}
