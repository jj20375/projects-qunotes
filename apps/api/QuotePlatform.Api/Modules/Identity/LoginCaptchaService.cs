using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace QuotePlatform.Api.Modules.Identity;

/// <summary>
/// 登入頁的圖形驗證碼服務。
///
/// 圖片直接組 SVG 字串（純文字格式，UTF-8 編碼後轉 base64 data URL），不透過任何點陣圖繪圖
/// 函式庫：點陣圖函式庫（GDI+、Skia 等）都是原生函式庫，只在建置機的作業系統上有對應的二進位
/// 檔，換一個部署平台（例如從 Mac 開發、部署到 Linux 容器）就可能因為缺少該平台的原生函式庫
/// 而在執行期丟例外。SVG 屬於文字內容，.NET 產生它不依賴任何平台相依元件，瀏覽器原生支援
/// &lt;img&gt; 直接吃 SVG 的 data URL，前端不需要因此改動。
/// </summary>
public sealed class LoginCaptchaService(IMemoryCache cache)
{
    private const int CodeLength = 5;
    private const int Width = 200;
    private const int Height = 70;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>產生一組新的驗證碼，回傳識別碼與可直接放進 &lt;img src&gt; 的 data URL。</summary>
    public (string CaptchaId, string ImageDataUrl) Generate()
    {
        var code = GenerateCode();
        var captchaId = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(captchaId), code, Ttl);
        return (captchaId, RenderImage(code));
    }

    /// <summary>
    /// 驗證輸入是否與快取的答案相符。
    /// 不論成功或失敗都會立即移除快取項目（一次性使用），避免同一張圖被反覆嘗試。
    /// </summary>
    public bool Validate(string? captchaId, string? inputCode)
    {
        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(inputCode)) return false;
        var key = CacheKey(captchaId);
        if (!cache.TryGetValue(key, out string? expected) || expected is null) return false;
        cache.Remove(key);
        return string.Equals(expected, inputCode.Trim(), StringComparison.Ordinal);
    }

    private static string CacheKey(string captchaId) => $"login-captcha:{captchaId}";

    private static string GenerateCode()
    {
        Span<byte> buffer = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(buffer);
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++) chars[i] = (char)('0' + buffer[i] % 10);
        return new string(chars);
    }

    private static string RenderImage(string code)
    {
        var random = Random.Shared;
        var svg = new StringBuilder();
        svg.Append(FormattableString.Invariant(
            $@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{Width}"" height=""{Height}"" viewBox=""0 0 {Width} {Height}"">"));
        svg.Append(FormattableString.Invariant($@"<rect width=""{Width}"" height=""{Height}"" fill=""#{ColorHex(245, 247, 250)}""/>"));

        // 干擾線：3 條隨機直線，顏色與驗證碼數字明顯有落差，避免蓋住數字本身。
        for (var i = 0; i < 3; i++)
        {
            var (lr, lg, lb) = RandomMutedColor(random);
            svg.Append(FormattableString.Invariant(
                $@"<line x1=""{random.Next(Width)}"" y1=""{random.Next(Height)}"" x2=""{random.Next(Width)}"" y2=""{random.Next(Height)}"" stroke=""#{ColorHex(lr, lg, lb)}"" stroke-width=""1""/>"));
        }

        var cellWidth = Width / (double)code.Length;
        for (var i = 0; i < code.Length; i++)
        {
            var (dr, dg, db) = RandomDarkColor(random);
            DrawDigit(svg, code[i], i * cellWidth + cellWidth / 2, dr, dg, db, random);
        }

        // 干擾點：24 個隨機半透明小圓點。
        for (var i = 0; i < 24; i++)
        {
            var (pr, pg, pb) = RandomMutedColor(random);
            svg.Append(FormattableString.Invariant(
                $@"<circle cx=""{random.Next(Width)}"" cy=""{random.Next(Height)}"" r=""1"" fill=""#{ColorHex(pr, pg, pb)}"" fill-opacity=""0.35""/>"));
        }

        svg.Append("</svg>");
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString()))}";
    }

    /// <summary>用 5x7 點陣（仿傳統 LED 顯示器）畫一個數字，每個點是一個 SVG 圓。</summary>
    private static void DrawDigit(StringBuilder svg, char digit, double centerX, int r, int g, int b, Random random)
    {
        var pattern = DigitPatterns[digit - '0'];
        const double dotSize = 5.8;
        const double gap = 1.4;
        var rotation = random.Next(-8, 9);
        var jitterY = random.Next(-2, 3);
        var centerY = Height / 2.0 + jitterY;

        svg.Append(FormattableString.Invariant(
            $@"<g transform=""translate({centerX:0.##},{centerY:0.##}) rotate({rotation})"">"));
        for (var row = 0; row < 7; row++)
        {
            for (var col = 0; col < 5; col++)
            {
                if (pattern[row][col] != '1') continue;
                var x = (col - 2) * (dotSize + gap);
                var y = (row - 3) * (dotSize + gap);
                svg.Append(FormattableString.Invariant(
                    $@"<circle cx=""{x:0.##}"" cy=""{y:0.##}"" r=""{dotSize / 2:0.##}"" fill=""#{ColorHex(r, g, b)}""/>"));
            }
        }
        svg.Append("</g>");
    }

    private static string ColorHex(int r, int g, int b) => $"{r:x2}{g:x2}{b:x2}";

    private static (int R, int G, int B) RandomDarkColor(Random random) => (random.Next(20, 90), random.Next(20, 90), random.Next(40, 120));

    private static (int R, int G, int B) RandomMutedColor(Random random) => (random.Next(160, 210), random.Next(160, 210), random.Next(160, 210));

    /// <summary>0-9 的 5x7 點陣字形，仿傳統 LED 顯示器樣式。</summary>
    private static readonly string[][] DigitPatterns =
    [
        ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ["11111", "00010", "00100", "00010", "00001", "10001", "01110"],
        ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
    ];
}
