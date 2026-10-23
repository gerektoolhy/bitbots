using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenWhen.Api.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> to pick the Postgres provider. Runtime reads DATABASE_URL.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDb>()
            .UseNpgsql("Host=localhost;Database=openwhen;Username=openwhen;Password=openwhen")
            .Options;
        return new AppDb(options);
    }
}
