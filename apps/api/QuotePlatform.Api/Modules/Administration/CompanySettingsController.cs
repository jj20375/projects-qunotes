using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;
using QuotePlatform.Api.Modules.Quotes;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>
/// 公司系統參數與單據抬頭設定輸入契約。
/// </summary>
/// <param name="DefaultTaxRate">預設營業稅率百分比（0~100）。</param>
/// <param name="DefaultCurrency">預設報價幣別（3 碼英文如 TWD）。</param>
/// <param name="QuoteValidDays">預設報價有效天數（1~365）。</param>
/// <param name="DefaultTerms">預設報價條款文字。</param>
/// <param name="FiscalYearStartMonth">會計年度起始月份（1~12）。</param>
/// <param name="QuoteNumberPattern">報價單號命名規則範本字串。</param>
/// <param name="EnglishName">公司英文名稱抬頭。</param>
/// <param name="Phone">代表聯絡電話。</param>
/// <param name="Address">公司地址。</param>
/// <param name="LogoFileId">新上傳之 Logo 檔案識別碼。</param>
/// <param name="RemoveLogo">是否移除現有 Logo 圖片。</param>
public sealed record CompanySettingInput(
    decimal? DefaultTaxRate,
    string? DefaultCurrency,
    int? QuoteValidDays,
    string? DefaultTerms,
    int? FiscalYearStartMonth,
    string? QuoteNumberPattern,
    string? EnglishName,
    string? Phone,
    string? Address,
    Guid? LogoFileId,
    bool? RemoveLogo);

/// <summary>
/// 申請公司 Logo 上傳工作階段之請求契約。
/// </summary>
/// <param name="FileName">Logo 圖片檔案名稱。</param>
/// <param name="Size">檔案大小（位元組）。</param>
public sealed record LogoUploadRequest(string? FileName, long Size);

/// <summary>
/// 公司系統參數與單據抬頭設定管理控制器（公司租戶範圍，需 company.settings 權限）。
/// </summary>
/// <param name="db">資料庫上下文實體。</param>
/// <param name="audit">稽核紀錄服務實體。</param>
/// <param name="logos">公司 Logo 管理服務實體。</param>
[Route("api/admin/settings")]
public sealed class CompanySettingsController(QuoteDbContext db, AuditService audit, CompanyLogoService logos) : ScopedControllerBase
{
    /// <summary>
    /// 取得當前公司之系統參數、單號規則預覽與單據抬頭設定。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回公司設定與預覽物件。</returns>
    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryCompany("company.settings", "read", out var scope)) return Forbidden();
        var company = await db.Companies.AsNoTracking().SingleAsync(value => value.Id == scope.CompanyId, cancellationToken);
        var setting = await db.CompanySettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new CompanySetting();
        return Ok(new
        {
            company = new
            {
                company.Name,
                company.TaxId,
                company.Phone,
                company.Address,
                company.EnglishName,
                logoFileId = company.LogoFileId,
                logoUrl = await logos.PreviewUrlAsync(company.LogoFileId, cancellationToken),
            },
            setting.DefaultTaxRate,
            setting.DefaultCurrency,
            setting.QuoteValidDays,
            setting.DefaultTerms,
            setting.FiscalYearStartMonth,
            setting.QuoteNumberPattern,
            quoteNumberPreview = DocumentNumberPattern.Format(setting.QuoteNumberPattern, BusinessTime.Today, 1),
        });
    }

    /// <summary>
    /// 更新當前公司之系統參數與單據抬頭。
    /// </summary>
    /// <param name="input">設定輸入資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>成功傳回 204 NoContent。</returns>
    [HttpPut]
    public async Task<ActionResult> Update(CompanySettingInput input, CancellationToken cancellationToken)
    {
        if (!TryCompany("company.settings", "update", out var scope)) return Forbidden();
        if (input.DefaultTaxRate is < 0 or > 100) return BadRequest(new { message = "稅率必須介於 0 到 100。" });
        if (input.DefaultCurrency is not null && (input.DefaultCurrency.Length != 3 || !input.DefaultCurrency.All(char.IsLetter))) return BadRequest(new { message = "幣別必須是 3 碼英文代碼，例如 TWD。" });
        if (input.QuoteValidDays is < 1 or > 365) return BadRequest(new { message = "報價有效天數必須介於 1 到 365。" });
        if (input.FiscalYearStartMonth is < 1 or > 12) return BadRequest(new { message = "會計年度起始月必須介於 1 到 12。" });
        if (input.DefaultTerms?.Length > 4000) return BadRequest(new { message = "預設條款最多 4000 字。" });
        if (input.QuoteNumberPattern is not null && DocumentNumberPattern.Validate(input.QuoteNumberPattern.Trim()) is string patternError) return BadRequest(new { message = patternError });
        if (input.EnglishName?.Length > 200 || input.Phone?.Length > 50 || input.Address?.Length > 500) return BadRequest(new { message = "英文名稱最多 200 字、電話 50 字、地址 500 字。" });
        if (input.LogoFileId is Guid logoFileId && await logos.ValidateAsync(logoFileId, cancellationToken) is string logoError) return BadRequest(new { message = logoError });

        var setting = await db.CompanySettings.SingleOrDefaultAsync(cancellationToken);
        if (setting is null)
        {
            setting = new CompanySetting();
            db.CompanySettings.Add(setting);
        }
        var company = await db.Companies.SingleAsync(value => value.Id == scope.CompanyId, cancellationToken);
        var before = new { setting.DefaultTaxRate, setting.DefaultCurrency, setting.QuoteValidDays, setting.FiscalYearStartMonth, setting.QuoteNumberPattern, company.EnglishName, company.Phone, company.Address, company.LogoFileId };
        setting.DefaultTaxRate = input.DefaultTaxRate ?? setting.DefaultTaxRate;
        setting.DefaultCurrency = input.DefaultCurrency?.ToUpperInvariant() ?? setting.DefaultCurrency;
        setting.QuoteValidDays = input.QuoteValidDays ?? setting.QuoteValidDays;
        setting.DefaultTerms = input.DefaultTerms is null ? setting.DefaultTerms : QuoteTerms.Normalize(input.DefaultTerms);
        setting.FiscalYearStartMonth = input.FiscalYearStartMonth ?? setting.FiscalYearStartMonth;
        setting.QuoteNumberPattern = input.QuoteNumberPattern?.Trim() ?? setting.QuoteNumberPattern;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        company.EnglishName = input.EnglishName is null ? company.EnglishName : Clean(input.EnglishName);
        company.Phone = input.Phone is null ? company.Phone : Clean(input.Phone);
        company.Address = input.Address is null ? company.Address : Clean(input.Address);
        if (input.RemoveLogo == true) company.LogoFileId = null;
        else if (input.LogoFileId is Guid newLogo) company.LogoFileId = newLogo;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "company_setting", scope.CompanyId, "update", before,
            new { setting.DefaultTaxRate, setting.DefaultCurrency, setting.QuoteValidDays, setting.FiscalYearStartMonth, setting.QuoteNumberPattern, company.EnglishName, company.Phone, company.Address, company.LogoFileId });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// 申請上傳公司 Logo 圖片工作階段。
    /// </summary>
    /// <param name="request">Logo 檔案大小與名稱請求。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回直接上傳 file-server 網址與 Session 資訊。</returns>
    [HttpPost("logo-upload")]
    public async Task<ActionResult> LogoUpload(LogoUploadRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.settings", "update", out var scope) ? ToResult(await logos.CreateUploadAsync(scope, request.FileName, request.Size, cancellationToken)) : Forbidden();

    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
