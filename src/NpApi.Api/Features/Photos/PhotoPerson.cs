using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpApi.Api.Data;
using NpApi.Api.Features.People;

namespace NpApi.Api.Features.Photos;

// A person tagged as appearing in a photo. People are linked here, not as free-text Tags, so a
// rename or two people with the same name can't split or merge anyone.
public sealed class PhotoPerson
{
    public Guid PhotoId { get; init; }
    public Photo Photo { get; init; } = null!;

    public Guid PersonId { get; init; }
    public Person Person { get; init; } = null!;

    // Auth0 user id of whoever added the tag.
    public required string TaggedBy { get; init; }
    public DateTimeOffset TaggedAt { get; init; } = Timestamps.UtcNow();
}

internal sealed class PhotoPersonConfiguration : IEntityTypeConfiguration<PhotoPerson>
{
    public void Configure(EntityTypeBuilder<PhotoPerson> builder)
    {
        builder.ToTable("photo_people");

        // One tag per person per photo.
        builder.HasKey(pp => new { pp.PhotoId, pp.PersonId });

        builder.Property(pp => pp.TaggedBy).HasMaxLength(128);

        builder.HasOne(pp => pp.Photo)
            .WithMany(p => p.TaggedPeople)
            .HasForeignKey(pp => pp.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a person removes their tags; the key leads with photo_id, so person_id gets its
        // own index for "all photos of this person".
        builder.HasOne(pp => pp.Person)
            .WithMany()
            .HasForeignKey(pp => pp.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(pp => pp.PersonId);
    }
}
