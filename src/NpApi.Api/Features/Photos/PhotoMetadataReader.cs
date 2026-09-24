using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace NpApi.Api.Features.Photos;

public sealed record PhotoMetadata(DateTimeOffset? DateTaken, double? Latitude, double? Longitude)
{
    public static readonly PhotoMetadata Empty = new(null, null, null);
}

// Reads capture time and GPS position from embedded EXIF (JPEG, HEIC, PNG, WebP).
public static class PhotoMetadataReader
{
    // EXIF 2.31 OffsetTimeOriginal ("+HH:MM"): the UTC offset for DateTimeOriginal. Not exposed as a
    // constant by MetadataExtractor.
    private const int TagOffsetTimeOriginal = 0x9011;

    public static PhotoMetadata Read(Stream image)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(image);
        }
        catch (Exception ex) when (ex is ImageProcessingException or IOException)
        {
            // Unreadable or missing metadata isn't an upload error; the image host validates the file.
            return PhotoMetadata.Empty;
        }

        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

        double? latitude = null, longitude = null;
        if (gps is not null && gps.TryGetGeoLocation(out var location) && !location.IsZero)
        {
            latitude = location.Latitude;
            longitude = location.Longitude;
        }

        return new PhotoMetadata(ReadDateTaken(exif, gps), latitude, longitude);
    }

    // EXIF DateTimeOriginal is local camera time with no zone. In order of preference:
    //   1. DateTimeOriginal + OffsetTimeOriginal (most modern phones, including Pixel)
    //   2. GPS date/time stamp, which is UTC by definition
    //   3. DateTimeOriginal read as UTC: wrong by the camera's UTC offset, but never shifted by the
    //      server's own time zone. Callers can override with an explicit value.
    private static DateTimeOffset? ReadDateTaken(ExifSubIfdDirectory? exif, GpsDirectory? gps)
    {
        DateTime local = default;
        var hasLocal = exif is not null && exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out local);

        if (hasLocal && TryParseOffset(exif!.GetString(TagOffsetTimeOriginal), out var offset))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
        }

        if (gps is not null && gps.TryGetGpsDate(out var gpsUtc))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(gpsUtc, DateTimeKind.Utc));
        }

        return hasLocal ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.Zero) : null;
    }

    private static bool TryParseOffset(string? value, out TimeSpan offset)
    {
        offset = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().TrimEnd('\0');
        var negative = text.StartsWith('-');
        if (!TimeSpan.TryParseExact(text.TrimStart('+', '-'), @"hh\:mm", CultureInfo.InvariantCulture, out offset))
        {
            return false;
        }

        if (negative)
        {
            offset = offset.Negate();
        }

        return offset >= TimeSpan.FromHours(-14) && offset <= TimeSpan.FromHours(14);
    }
}
