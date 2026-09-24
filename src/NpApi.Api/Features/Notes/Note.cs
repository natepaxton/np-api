using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NpApi.Api.Features.Notes;

public sealed class Note
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string OwnerId { get; init; }
    public required string Title { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = TruncateToMicroseconds(DateTimeOffset.UtcNow);

    // Postgres timestamptz stores microseconds; .NET ticks are 100ns. Truncating up front keeps the
    // value returned by POST identical to what later reads return from the database.
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.Property(n => n.OwnerId).HasMaxLength(128);
        builder.Property(n => n.Title).HasMaxLength(200);
        builder.HasIndex(n => n.OwnerId);
    }
}
