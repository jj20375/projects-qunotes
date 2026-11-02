// ============================================================
// QuotePlatform.Api — 專案報價管理系統 ASP.NET Core 10 modular monolith
//
// 啟動模式：
//   --self-test ：執行權限目錄與權限計算規則的 AssertExamples 後結束；不需資料庫。
//   --seed-only ：套用 migration 與 Seeder 後結束。
//   （無旗標）  ：啟動 HTTP 服務。
//
// 資料隔離：QuoteDbContext 以 JWT company_id 做 Global Query Filter；
// 業務程式只透過 RequestScope 取得範圍，不信任前端傳入的 companyId。
// ============================================================
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Administration;
using QuotePlatform.Api.Modules.Clients;
using QuotePlatform.Api.Modules.Dashboard;
using QuotePlatform.Api.Modules.Files;
using QuotePlatform.Api.Modules.Identity;
using QuotePlatform.Api.Modules.ProjectDocuments;
using QuotePlatform.Api.Modules.Products;
using QuotePlatform.Api.Modules.Projects;
using QuotePlatform.Api.Modules.Quotes;

// QuestPDF Community 授權：年營收 100 萬美元以下免費，超過需購買商業授權（見 docs/system-architecture.md）。
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
// QuestPDF 2026.9 起預設不讀系統字型。中文字型目前來自系統（Docker 映像安裝 fonts-noto-cjk，Mac 為黑體／蘋方），
// 也可用 Quote__PdfFontDirectory 指定放字型檔的目錄；缺字型時維持拋出例外，不輸出缺字的 PDF。
QuestPDF.Settings.UseSystemFonts = true;
QuotePdf.ConfigureFonts(Environment.GetEnvironmentVariable("Quote__PdfFontDirectory"));

if (args.SequenceEqual(["--self-test"]))
{
    PermissionCatalog.AssertCatalog();
    PermissionGrantRules.AssertExamples();
    QuoteCalculator.AssertExamples();
    QuoteExcel.AssertRoundTrip();
    QuoteExcel.AssertLegacyLayout();
    QuoteDocumentService.AssertPdfRenders();
    FileTokenService.AssertExamples();
    QuoteOcrService.AssertExamples();
    QuoteTerms.AssertExamples();
    DocumentNumberPattern.AssertExamples();
    AiSettings.AssertExamples();
    Console.WriteLine("self-test passed");
    return;
}

var seedOnly = args.SequenceEqual(["--seed-only"]);
var builder = WebApplication.CreateBuilder(args.Where(value => value != "--seed-only").ToArray());
var quoteOptions = builder.Configuration.GetSection("Quote").Get<QuoteOptions>() ?? new QuoteOptions();
builder.Services.Configure<QuoteOptions>(builder.Configuration.GetSection("Quote"));
// AI 設定檔（供應商、模型、強度）：存檔即重新載入；之後再加一次環境變數，讓 Ai__Ocr__... 仍可覆寫設定檔。
builder.Configuration.AddJsonFile(string.IsNullOrWhiteSpace(quoteOptions.AiSettingsPath) ? "ai-settings.json" : quoteOptions.AiSettingsPath, optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddSingleton<AiSettings>();

if (Encoding.UTF8.GetByteCount(quoteOptions.JwtKey) < 32) throw new InvalidOperationException("Quote__JwtKey 未設定或長度不足 32 bytes。");
// 檔案 token 與 file-server 共用；缺少時附件功能整個無法使用，因此啟動即失敗而不是等到上傳才發現。
if (Encoding.UTF8.GetByteCount(quoteOptions.FileTokenKey) < 32) throw new InvalidOperationException("Quote__FileTokenKey 未設定或長度不足 32 bytes（需與 file-server 的 FILE_TOKEN_KEY 相同）。");
if (quoteOptions.FileCallbackSecret.Length < 16) throw new InvalidOperationException("Quote__FileCallbackSecret 未設定或少於 16 字元（需與 file-server 的 FILE_CALLBACK_SECRET 相同）。");
// 不提供預設連線字串：避免沒帶環境變數時誤連 Mac 上 3306 的 metat-vps-copy-db。
var connectionString = builder.Configuration.GetConnectionString("Quote");
if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("ConnectionStrings__Quote 未設定。");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CompanyContext>();
builder.Services.AddDbContext<QuoteDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(11, 4, 0))));

// 正式環境 API 只經同一個 compose network 上的 nginx 轉發，信任這一跳的 X-Forwarded-For 以取得登入節流用的真實 IP。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddMemoryCache();
builder.Services.AddScoped<CompanyLogoService>();
builder.Services.AddHttpClient(CompanyLogoService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<LoginCaptchaService>();
builder.Services.AddSingleton<LoginAttemptGuard>();
builder.Services.AddScoped<PermissionGrantService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<CompanyProvisioner>();
builder.Services.AddScoped<QuoteSeeder>();
builder.Services.AddScoped<IdentityService>();
builder.Services.AddScoped<CompanyService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<SystemUserService>();
builder.Services.AddScoped<NumberSequenceService>();
builder.Services.AddScoped<ProjectAccessService>();
builder.Services.AddScoped<ClientAccessService>();
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<ClientTransferService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<QuoteService>();
builder.Services.AddScoped<QuoteDocumentService>();
builder.Services.AddScoped<QuoteImportService>();
builder.Services.AddSingleton<IQuoteRecognizer, ClaudeQuoteRecognizer>();
builder.Services.AddSingleton<IQuoteRecognizer, GeminiQuoteRecognizer>();
builder.Services.AddSingleton<QuoteOcrService>();
builder.Services.AddHttpClient(GeminiQuoteRecognizer.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(150));
builder.Services.AddSingleton<FileTokenService>();
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<ProjectDocumentService>();
builder.Services.AddScoped<ShipmentService>();
builder.Services.AddScoped<DashboardService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = quoteOptions.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = quoteOptions.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(quoteOptions.JwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
        };
        options.Events = new JwtBearerEvents
        {
            // 每個請求都比對 AuthVersion 與公司狀態：權限、身分或密碼異動後，舊 access token 立即失效。
            OnTokenValidated = async context =>
            {
                if (context.Principal is null || !RequestScope.TryRead(context.Principal, out var scope))
                {
                    context.Fail("登入主體無效。");
                    return;
                }
                var db = context.HttpContext.RequestServices.GetRequiredService<QuoteDbContext>();
                var user = await db.Users.AsNoTracking()
                    .Where(value => value.Id == scope.UserId)
                    .Select(value => new { value.Status, value.AuthVersion })
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                if (user is null || user.Status != "active" || user.AuthVersion != scope.AuthVersion)
                {
                    context.Fail("登入權限已更新，請重新登入。");
                    return;
                }
                if (scope.CompanyId is Guid companyId
                    && !await db.Companies.AsNoTracking().AnyAsync(value => value.Id == companyId && value.Status == "active", context.HttpContext.RequestAborted))
                {
                    context.Fail("公司已停用。");
                }
            },
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();
// 啟動時就檢查 AI 設定，寫錯直接失敗，不要等到第一次圖像識別才發現。
app.Services.GetRequiredService<AiSettings>();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapControllers();

// migration／Seeder 只看明確的 opt-in 旗標，不綁 ASPNETCORE_ENVIRONMENT（沿用美達特的教訓）。
if (quoteOptions.MigrateOnStart || quoteOptions.BootstrapOnStart || seedOnly)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<QuoteDbContext>();
    await db.Database.MigrateAsync();
    if (quoteOptions.BootstrapOnStart || seedOnly) await scope.ServiceProvider.GetRequiredService<QuoteSeeder>().SeedAsync(CancellationToken.None);
}

if (seedOnly)
{
    Console.WriteLine("初始化完成。");
    return;
}

app.Run();
