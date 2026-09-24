using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpApi.Api.Data;
using NpApi.Api.Features.People;
using NpApi.Api.Features.Places;

namespace NpApi.Api.Features.Photos;

// An image stored in Cloudinary, plus what we know about it. Delivery URLs (thumbnail, medium,
// full) are derived from CloudinaryPublicId rather than stored, so transformations can change
// without a data migration.
public sealed class Photo
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    // Cloudinary public ID, e.g. "np-api/photos/0199..."; unique, so one asset maps to one row.
    public required string CloudinaryPublicId { get; init; }

    // The secure_url Cloudinary returned for the original upload.
    public required string Url { get; init; }

    public required string Filename { get; init; }

    // Whose camera took it (EXIF records the device, not who pressed the shutter). Null when unknown.
    public Guid? CameraOwnerId { get; set; }
    public Person? CameraOwner { get; set; }

    // People appearing in the photo.
    public List<PhotoPerson> TaggedPeople { get; } = [];

    // The named place the photo belongs to, if any. The photo's own point (below) is where it was
    // taken; the map falls back to Place's point when the photo has none.
    public Guid? PlaceId { get; set; }
    public Place? Place { get; set; }

    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public LocationSource? LocationSource { get; private set; }

    public DateTimeOffset? DateTaken { get; set; }
    public string? DateCategory { get; set; }
    public List<string> Tags { get; set; } = [];

    public int Width { get; init; }
    public int Height { get; init; }

    // Auth0 user id ("sub") of the uploader, who isn't necessarily the camera owner.
    public required string UploadedBy { get; init; }
    public DateTimeOffset UploadedAt { get; init; } = Timestamps.UtcNow();

    // Coordinates and their source are only ever set together (also enforced by a check constraint).
    public void SetLocation(double latitude, double longitude, LocationSource source)
    {
        Latitude = latitude;
        Longitude = longitude;
        LocationSource = source;
    }
}

// Where a photo's coordinates came from. The map can style non-EXIF points as approximate.
public enum LocationSource
{
    // GPS tags embedded in the image by the camera.
    Exif,

    // Estimated, e.g. from another photo taken nearby in time (np-web infer-photo-locations).
    Inferred,

    // Placed by a person.
    Manual,
}

internal sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.Property(p => p.CloudinaryPublicId).HasMaxLength(255);
        builder.HasIndex(p => p.CloudinaryPublicId).IsUnique();

        builder.Property(p => p.Url).HasMaxLength(1024);
        builder.Property(p => p.Filename).HasMaxLength(255);
        // A person can't be deleted while they own photos; reassign the photos first.
        builder.HasOne(p => p.CameraOwner)
            .WithMany()
            .HasForeignKey(p => p.CameraOwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.DateCategory).HasMaxLength(50);
        builder.Property(p => p.UploadedBy).HasMaxLength(128);
        builder.Property(p => p.LocationSource).HasConversion<string>().HasMaxLength(20);

        // text[] with a GIN index, so "photos tagged X" stays an index lookup as the table grows.
        builder.HasIndex(p => p.Tags).HasMethod("gin");
        builder.HasIndex(p => p.DateTaken);

        // Deleting a place unlinks its photos; the photos themselves stay.
        builder.HasOne(p => p.Place)
            .WithMany()
            .HasForeignKey(p => p.PlaceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_photos_latitude_range", "latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_photos_longitude_range", "longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint("ck_photos_location_complete",
                "(latitude IS NULL) = (longitude IS NULL) AND (latitude IS NULL) = (location_source IS NULL)");
        });
    }
}
