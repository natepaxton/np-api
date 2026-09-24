using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NpApi.Api.Data;

// Used only by `dotnet ef` so migrations can be created without the Aspire AppHost running.
// Commands that touch a real database (update, bundle) should pass --connection explicitly.
[ExcludeFromCodeCoverage(Justification = "Only invoked by dotnet-ef tooling.")]
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=npdb;Username=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
