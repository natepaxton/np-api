using Dapper;
using Microsoft.EntityFrameworkCore;
using NpApi.Api.Data;

namespace NpApi.Api.Features.Notes;

// Dapper row types use init properties, not positional records: constructor mapping needs exact
// column names and types, so snake_case columns and timestamptz -> DateTimeOffset won't bind.
public sealed record NoteSummary
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}

// Read-side queries written as SQL with Dapper. Writes go through AppDbContext.
// Dapper borrows EF's connection (and so any open EF transaction) instead of owning one; EF opens,
// closes and disposes it. Never register that connection in DI: the container would dispose it at
// the end of the request and break the pooled DbContext for the next request.
public sealed class NoteQueries(AppDbContext db)
{
    public async Task<IReadOnlyList<NoteSummary>> ListForOwnerAsync(string ownerId, CancellationToken ct)
    {
        const string sql = """
            select id, title, created_at
            from notes
            where owner_id = @ownerId
            order by created_at desc
            """;

        var rows = await db.Database.GetDbConnection().QueryAsync<NoteSummary>(
            new CommandDefinition(sql, new { ownerId }, cancellationToken: ct));

        return rows.AsList();
    }
}
