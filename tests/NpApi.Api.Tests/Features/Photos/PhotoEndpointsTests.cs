using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NpApi.Api.Auth;
using NpApi.Api.Features.Photos;
using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Features.Photos;

public class PhotoEndpointsTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly DateTime LocalTaken = new(2026, 8, 23, 22, 11, 51);
    private static readonly DateTimeOffset UtcTaken = new(2026, 8, 24, 3, 11, 51, TimeSpan.Zero);
    private const double MammothLat = 37.1862, MammothLng = -86.1005;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Writer() => factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPhotos, Permissions.WritePhotos);

    private static MultipartFormDataContent Form(
        byte[]? image,
        string fileName = "PXL_20260824_031151688.NIGHT.jpg",
        string contentType = "image/jpeg",
        Guid? cameraOwnerId = null,
        params (string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        if (image is not null)
        {
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "file", fileName);
        }
        if (cameraOwnerId is not null)
        {
            form.Add(new StringContent(cameraOwnerId.Value.ToString()), "cameraOwnerId");
        }
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }
        return form;
    }

    private static byte[] PixelPhoto() =>
        TestImages.Jpeg(dateTimeOriginal: LocalTaken, offsetTimeOriginal: "-05:00", gps: (MammothLat, MammothLng));

    private static async Task<PhotoResponse> ReadPhotoAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PhotoResponse>(Json, Ct))!;

    [Fact]
    public async Task Upload_requires_authentication()
    {
        var response = await factory.CreateClient().PostAsync("/api/v1/photos", Form(PixelPhoto()), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_requires_write_permission()
    {
        var reader = factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPhotos);

        var response = await reader.PostAsync("/api/v1/photos", Form(PixelPhoto()), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_stores_image_and_saves_exif_date_and_location()
    {
        var image = PixelPhoto();
        var laura = await factory.CreatePersonAsync("Laura");

        var response = await Writer().PostAsync("/api/v1/photos",
            Form(image, cameraOwnerId: laura.Id, fields: ("dateCategory", "trip-out")), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photo = await ReadPhotoAsync(response);

        Assert.Equal("PXL_20260824_031151688.NIGHT.jpg", photo.Filename);
        Assert.Equal(laura.Id, photo.CameraOwnerId);
        Assert.Equal("Laura", photo.CameraOwner);
        Assert.Equal("trip-out", photo.DateCategory);
        Assert.Equal(UtcTaken, photo.DateTaken);
        Assert.Equal(MammothLat, photo.Lat!.Value, 4);
        Assert.Equal(MammothLng, photo.Lng!.Value, 4);
        Assert.Equal(LocationSource.Exif, photo.LocationSource);
        Assert.Empty(photo.Tags);
        Assert.Equal((4032, 3024), (photo.Width, photo.Height));

        // Stored under the Development folder with the photo's own id, never the filename.
        var publicId = $"np-api/dev/photos/{photo.Id}";
        Assert.Equal(image, factory.Storage.Uploaded[publicId]);
        Assert.Equal($"https://res.cloudinary.com/{ApiFactory.CloudName}/image/upload/c_fill,w_200,h_200,f_auto,q_auto/{publicId}", photo.Thumbnail);
        Assert.Equal($"https://res.cloudinary.com/{ApiFactory.CloudName}/image/upload/c_limit,w_800,f_auto,q_auto/{publicId}", photo.Medium);
        Assert.Equal($"https://res.cloudinary.com/{ApiFactory.CloudName}/image/upload/f_auto,q_auto/{publicId}", photo.Full);

        var fetched = await ReadPhotoAsync(await Writer().GetAsync(response.Headers.Location, Ct));
        Assert.Equal(photo with { Tags = [] }, fetched with { Tags = [] });
    }

    [Fact]
    public async Task Form_location_and_date_override_exif()
    {
        var response = await Writer().PostAsync("/api/v1/photos", Form(PixelPhoto(),
            fields: [("lat", "44.4605"), ("lng", "-110.8281"), ("dateTaken", "2026-09-01T08:30:00-06:00")]), Ct);

        var photo = await ReadPhotoAsync(response);
        Assert.Equal((44.4605, -110.8281), (photo.Lat, photo.Lng));
        Assert.Equal(LocationSource.Manual, photo.LocationSource);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 14, 30, 0, TimeSpan.Zero), photo.DateTaken);
    }

    [Fact]
    public async Task Upload_can_link_a_place_and_keeps_its_own_point()
    {
        var place = await factory.CreatePlaceAsync();

        var response = await Writer().PostAsync("/api/v1/photos",
            Form(PixelPhoto(), fields: ("placeId", place.Id.ToString())), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photo = await ReadPhotoAsync(response);
        Assert.Equal(place.Id, photo.Place!.Id);
        Assert.Equal("Gardiner, Montana, US", photo.Place.DisplayName);
        Assert.Equal((45.0319, -110.7057), (photo.Place.Lat, photo.Place.Lng));
        // The photo's own EXIF point is kept separately from the place's point.
        Assert.Equal(MammothLat, photo.Lat!.Value, 4);
        Assert.Equal(LocationSource.Exif, photo.LocationSource);

        var fetched = await ReadPhotoAsync(await Writer().GetAsync(response.Headers.Location, Ct));
        Assert.Equal(photo.Place, fetched.Place);
    }

    [Fact]
    public async Task Malformed_place_id_is_a_bad_request()
    {
        var response = await Writer().PostAsync("/api/v1/photos",
            Form(PixelPhoto(), fields: ("placeId", "not-a-guid")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Photo_without_gps_has_no_location()
    {
        var response = await Writer().PostAsync("/api/v1/photos",
            Form(TestImages.Jpeg(dateTimeOriginal: LocalTaken, offsetTimeOriginal: "-05:00")), Ct);

        var photo = await ReadPhotoAsync(response);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(photo.Lat);
        Assert.Null(photo.Lng);
        Assert.Null(photo.LocationSource);
    }

    public static TheoryData<string, Func<MultipartFormDataContent>> InvalidUploads => new()
    {
        { "File", () => Form(image: null, fields: ("dateCategory", "trip-out")) },
        { "File", () => Form([]) },
        { "File", () => Form(new byte[PhotoEndpoints.MaxUploadBytes + 1]) },
        { "File", () => Form(PixelPhoto(), fileName: "notes.pdf", contentType: "application/pdf") },
        { "CameraOwnerId", () => Form(PixelPhoto(), cameraOwnerId: Guid.CreateVersion7()) },
        { "PlaceId", () => Form(PixelPhoto(), fields: ("placeId", Guid.CreateVersion7().ToString())) },
        { "DateCategory", () => Form(PixelPhoto(), fields: ("dateCategory", new string('x', 51))) },
        { "Lat", () => Form(PixelPhoto(), fields: ("lat", "44.46")) },
        { "Lat", () => Form(PixelPhoto(), fields: [("lat", "91"), ("lng", "0")]) },
        { "Lat", () => Form(PixelPhoto(), fields: [("lat", "0"), ("lng", "-181")]) },
    };

    [Theory]
    [MemberData(nameof(InvalidUploads))]
    public async Task Invalid_upload_returns_validation_problem_and_stores_nothing(string field, Func<MultipartFormDataContent> form)
    {
        var storage = new FakePhotoStorage();
        await using var app = factory.WithStorage(storage);
        var client = ApiFactory.Authorize(app.CreateClient(), TestAuth.NewUserId(), Permissions.WritePhotos);

        var response = await client.PostAsync("/api/v1/photos", form(), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"Expected an error for {field}: {problem}");
        Assert.Empty(storage.Uploaded);
    }

    // Unreadable input is the client's fault: 400, never a 500.
    [Fact]
    public async Task Malformed_multipart_body_is_a_bad_request()
    {
        // An empty MultipartFormDataContent serializes a section with no headers, which isn't valid.
        var response = await Writer().PostAsync("/api/v1/photos", new MultipartFormDataContent(), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_camera_owner_id_is_a_bad_request()
    {
        var response = await Writer().PostAsync("/api/v1/photos",
            Form(PixelPhoto(), fields: ("cameraOwnerId", "not-a-guid")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task File_and_field_errors_are_reported_together()
    {
        var response = await Writer().PostAsync("/api/v1/photos",
            Form(image: null, fields: [("lat", "44.46"), ("dateCategory", new string('x', 51))]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.Equal(["DateCategory", "File", "Lat"], errors.EnumerateObject().Select(e => e.Name).Order());
    }

    [Theory]
    [InlineData(FakePhotoStorage.Failure.InvalidImage, HttpStatusCode.BadRequest)]
    [InlineData(FakePhotoStorage.Failure.Unavailable, HttpStatusCode.BadGateway)]
    public async Task Storage_failure_is_reported_and_nothing_is_saved(FakePhotoStorage.Failure failure, HttpStatusCode expected)
    {
        var owner = await factory.CreatePersonAsync();
        await using var app = factory.WithStorage(new FakePhotoStorage { FailWith = failure });
        var client = ApiFactory.Authorize(app.CreateClient(), TestAuth.NewUserId(), Permissions.ReadPhotos, Permissions.WritePhotos);

        var response = await client.PostAsync("/api/v1/photos", Form(PixelPhoto(), cameraOwnerId: owner.Id), Ct);

        Assert.Equal(expected, response.StatusCode);
        var photos = await client.GetFromJsonAsync<PhotoResponse[]>("/api/v1/photos", Json, Ct);
        Assert.DoesNotContain(photos!, p => p.CameraOwnerId == owner.Id);
    }

    [Fact]
    public async Task Image_is_deleted_from_storage_when_the_row_cannot_be_saved()
    {
        // Every upload reports the same public ID, so the second insert violates the unique index.
        var storage = new FakePhotoStorage { FixedPublicId = $"np-api/test/duplicate-{Guid.NewGuid()}" };
        await using var app = factory.WithStorage(storage);
        var client = ApiFactory.Authorize(app.CreateClient(), TestAuth.NewUserId(), Permissions.WritePhotos);

        var first = await client.PostAsync("/api/v1/photos", Form(PixelPhoto()), Ct);
        var second = await client.PostAsync("/api/v1/photos", Form(PixelPhoto()), Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, second.StatusCode);
        Assert.Equal([storage.FixedPublicId], storage.Deleted);
    }

    [Theory]
    [InlineData("/api/v1/photos")]
    [InlineData("/api/v1/photos/0199b6c0-0000-7000-8000-000000000000")]
    public async Task Reading_requires_read_permission(string url)
    {
        var writerOnly = factory.CreateClientFor(TestAuth.NewUserId(), Permissions.WritePhotos);

        var response = await writerOnly.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_is_ordered_by_date_taken_with_undated_photos_last()
    {
        var owner = (await factory.CreatePersonAsync()).Id;
        var client = Writer();
        async Task<Guid> Upload(byte[] image) =>
            (await ReadPhotoAsync(await client.PostAsync("/api/v1/photos", Form(image, cameraOwnerId: owner), Ct))).Id;

        var undated = await Upload(TestImages.Jpeg());
        var later = await Upload(TestImages.Jpeg(dateTimeOriginal: LocalTaken.AddDays(2), offsetTimeOriginal: "+00:00"));
        var earlier = await Upload(TestImages.Jpeg(dateTimeOriginal: LocalTaken, offsetTimeOriginal: "+00:00"));

        var photos = await client.GetFromJsonAsync<PhotoResponse[]>("/api/v1/photos", Json, Ct);

        Assert.Equal([earlier, later, undated], photos!.Where(p => p.CameraOwnerId == owner).Select(p => p.Id));
    }

    [Fact]
    public async Task Get_unknown_photo_returns_404()
    {
        var response = await Writer().GetAsync($"/api/v1/photos/{Guid.CreateVersion7()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
