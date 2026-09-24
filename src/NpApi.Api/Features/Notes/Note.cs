using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpApi.Api.Data;

namespace NpApi.Api.Features.Notes;

public sealed class Note
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string OwnerId { get; init; }
    public required string Title { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = Timestamps.UtcNow();
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
