using NpApi.Api.Features.Photos;
using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Features.Photos;

public class PhotoMetadataReaderTests
{
    // PXL_20260824_031151688: Pixel filenames are UTC. Local camera time was 22:11:51 at UTC-5.
    private static readonly DateTime LocalTaken = new(2026, 8, 23, 22, 11, 51);
    private static readonly DateTime UtcTaken = new(2026, 8, 24, 3, 11, 51, DateTimeKind.Utc);

    private static PhotoMetadata Read(byte[] image) => PhotoMetadataReader.Read(new MemoryStream(image));

    [Fact]
    public void Uses_exif_offset_to_get_the_real_instant()
    {
        var metadata = Read(TestImages.Jpeg(dateTimeOriginal: LocalTaken, offsetTimeOriginal: "-05:00"));

        Assert.Equal(new DateTimeOffset(UtcTaken), metadata.DateTaken);
        Assert.Equal(TimeSpan.FromHours(-5), metadata.DateTaken!.Value.Offset);
    }

    [Fact]
    public void Falls_back_to_gps_timestamp_when_offset_is_missing()
    {
        var metadata = Read(TestImages.Jpeg(dateTimeOriginal: LocalTaken, gpsUtc: UtcTaken));

        Assert.Equal(new DateTimeOffset(UtcTaken), metadata.DateTaken);
    }

    [Fact]
    public void Ignores_a_malformed_offset()
    {
        var metadata = Read(TestImages.Jpeg(dateTimeOriginal: LocalTaken, offsetTimeOriginal: "bogus", gpsUtc: UtcTaken));

        Assert.Equal(new DateTimeOffset(UtcTaken), metadata.DateTaken);
    }

    [Fact]
    public void Reads_a_zoneless_date_as_utc_rather_than_the_server_time_zone()
    {
        var metadata = Read(TestImages.Jpeg(dateTimeOriginal: LocalTaken));

        Assert.Equal(new DateTimeOffset(LocalTaken, TimeSpan.Zero), metadata.DateTaken);
    }

    [Theory]
    [InlineData(37.1862, -86.1005)]   // Mammoth Cave: north, west
    [InlineData(-45.8788, 170.5028)]  // Dunedin: south, east
    public void Reads_gps_coordinates_with_hemisphere_signs(double latitude, double longitude)
    {
        var metadata = Read(TestImages.Jpeg(gps: (latitude, longitude)));

        Assert.Equal(latitude, metadata.Latitude!.Value, 4);
        Assert.Equal(longitude, metadata.Longitude!.Value, 4);
    }

    [Fact]
    public void Image_without_metadata_returns_empty()
    {
        Assert.Equal(PhotoMetadata.Empty, Read(TestImages.Jpeg()));
    }

    [Fact]
    public void Unreadable_file_returns_empty_instead_of_throwing()
    {
        Assert.Equal(PhotoMetadata.Empty, Read(TestImages.NotAnImage()));
    }
}
