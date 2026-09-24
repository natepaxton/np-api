using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpApi.Api.Data;

namespace NpApi.Api.Features.People;

// Someone who appears in photos (via PhotoPerson tags) or whose camera took them (Photo.CameraOwner).
// Identified by id, never by name: two people can share a name, and names change.
public sealed class Person
{
    public const int MaxNameLength = 100;

    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = Timestamps.UtcNow();

    // "Nate", "Laura Ann Paxton". Computed, not stored.
    public string DisplayName => string.Join(' ', new[] { FirstName, MiddleName, LastName }.Where(n => !string.IsNullOrEmpty(n)));
}

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people", table =>
            table.HasCheckConstraint("ck_people_first_name_not_blank", "btrim(first_name) <> ''"));

        builder.Property(p => p.FirstName).HasMaxLength(Person.MaxNameLength);
        builder.Property(p => p.MiddleName).HasMaxLength(Person.MaxNameLength);
        builder.Property(p => p.LastName).HasMaxLength(Person.MaxNameLength);
        builder.Ignore(p => p.DisplayName);

        // Lists are sorted by name; no unique index, since different people can share a name.
        builder.HasIndex(p => new { p.FirstName, p.LastName });
    }
}
