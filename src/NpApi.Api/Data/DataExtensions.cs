using Dapper;
using Microsoft.EntityFrameworkCore;

namespace NpApi.Api.Data;

public static class DataExtensions
{
    public const string ConnectionName = "npdb";

    public static IHostApplicationBuilder AddData(this IHostApplicationBuilder builder)
    {
        // Aspire integration: registers AppDbContext (pooled), retries, tracing, metrics and a
        // database health check. Reads ConnectionStrings:npdb.
        builder.AddNpgsqlDbContext<AppDbContext>(ConnectionName,
            configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

        // Dapper (for SQL-first read queries): map snake_case columns (uploaded_at) to PascalCase
        // properties (UploadedAt). See docs/architecture.md before adding a Dapper query.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return builder;
    }

    // Production migrations run from CI (see docs/database.md), never on app startup.
    public static async Task ApplyMigrationsInDevelopmentAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
