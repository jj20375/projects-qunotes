using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;
using QuotePlatform.Api.Modules.Quotes;

namespace QuotePlatform.Api.Modules.Administration;

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

public sealed record LogoUploadRequest(string? FileName, long Size);

/// <summary>
/// 公司參數（company scope，company.settings）：報價預設稅率、幣別、有效天數、預設條款、報價單號規則、
/// Dashboard 的會計年度起始月，以及報價單抬頭（英文名稱、電話、地址、Logo）。
/// 公司名稱與統編屬於公司身分，由平台管理（公司開通）維護。
/// </summary>
[Route("api/admin/settings")]
public sealed class CompanySettingsController(QuoteDbContext db, AuditService audit, CompanyLogoService logos) : ScopedControllerBase
{
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

    /// <summary>申請上傳 Logo；上傳完成後以 PUT 的 <c>logoFileId</c> 套用。</summary>
    [HttpPost("logo-upload")]
    public async Task<ActionResult> LogoUpload(LogoUploadRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.settings", "update", out var scope) ? ToResult(await logos.CreateUploadAsync(scope, request.FileName, request.Size, cancellationToken)) : Forbidden();

    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
