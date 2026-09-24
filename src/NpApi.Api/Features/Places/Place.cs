using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpApi.Api.Data;

namespace NpApi.Api.Features.Places;

// A named location a photo can belong to ("Gardiner, Montana, US"). Country is an ISO code
// (CountryCode) backed by the countries lookup table. Its optional point is a
// representative position (e.g. a town or park centre). A photo's own EXIF point stays on the photo;
// the map falls back to the place's point for photos without one. Place types come later.
public sealed class Place
{
    public const int MaxNameLength = 100;

    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string? City { get; set; }
    public string? StateProvince { get; set; }
    public CountryCode? CountryCode { get; set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public DateTimeOffset CreatedAt { get; init; } = Timestamps.UtcNow();

    // "Gardiner, Montana, US"; falls back to the coordinates for a place that only has a point.
    public string DisplayName
    {
        get
        {
            var names = string.Join(", ", new[] { City, StateProvince, CountryCode?.ToString() }.Where(n => !string.IsNullOrEmpty(n)));
            if (names.Length > 0 || Latitude is null || Longitude is null)
            {
                return names;
            }
            return string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.#####}, {Longitude:0.#####}");
        }
    }

    // The point and its two halves are only ever set together (also enforced by a check constraint).
    public void SetPoint(double? latitude, double? longitude)
    {
        if (latitude.HasValue != longitude.HasValue)
        {
            throw new ArgumentException("Latitude and longitude must both be set or both be null.");
        }
        Latitude = latitude;
        Longitude = longitude;
    }
}

internal sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        builder.ToTable("places", table =>
        {
            table.HasCheckConstraint("ck_places_latitude_range", "latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_places_longitude_range", "longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint("ck_places_point_complete", "(latitude IS NULL) = (longitude IS NULL)");
            table.HasCheckConstraint("ck_places_not_empty",
                "city IS NOT NULL OR state_province IS NOT NULL OR country_code IS NOT NULL OR latitude IS NOT NULL");
        });

        builder.Property(p => p.City).HasMaxLength(Place.MaxNameLength);
        builder.Property(p => p.StateProvince).HasMaxLength(Place.MaxNameLength);
        builder.Property(p => p.CountryCode).HasConversion<string>().HasMaxLength(Country.CodeLength).IsFixedLength();
        builder.HasOne<Country>()
            .WithMany()
            .HasForeignKey(p => p.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(p => p.DisplayName);

        builder.HasIndex(p => new { p.CountryCode, p.StateProvince, p.City });
    }
}
