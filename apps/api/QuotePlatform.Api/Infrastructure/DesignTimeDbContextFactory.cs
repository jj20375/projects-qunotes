using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 供 <c>dotnet ef migrations add</c> 建立 context；產生 migration 不需要連線，
/// 因此固定伺服器版本，不讀取任何連線字串。
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<QuoteDbContext>
{
    public QuoteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QuoteDbContext>()
            .UseMySql("Server=design-time;Database=quote_platform", new MySqlServerVersion(new Version(11, 4, 0)))
            .Options;
        return new QuoteDbContext(options);
    }
}
