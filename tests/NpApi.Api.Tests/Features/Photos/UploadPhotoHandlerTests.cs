using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NpApi.Api.Data;
using NpApi.Api.Features.Photos;
using NpApi.Api.Features.Photos.Storage;
using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Features.Photos;

// The handler without HTTP: how an import or any other non-endpoint caller would use it.
public class UploadPhotoHandlerTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A stream that can only be read forwards, like a network body.
    private sealed class ForwardOnlyStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Position { get => base.Position; set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }

    private (UploadPhotoHandler Handler, AppDbContext Db, IServiceScope Scope) Create(FakePhotoStorage storage)
    {
        var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<CloudinaryOptions>>();
        return (new UploadPhotoHandler(db, storage, options, NullLogger<UploadPhotoHandler>.Instance), db, scope);
    }

    private static UploadPhotoCommand Command(Stream content, Guid? cameraOwnerId = null) =>
        new(content, "PXL_1.jpg", TestAuth.NewUserId(), cameraOwnerId);

    [Fact]
    public async Task Reads_metadata_and_uploads_from_a_forward_only_stream_without_closing_it()
    {
        var storage = new FakePhotoStorage();
        var (handler, _, scope) = Create(storage);
        using var _ = scope;
        var image = TestImages.Jpeg(new DateTime(2026, 9, 1, 8, 30, 0), "-06:00", gps: (44.4605, -110.8281));
        using var source = new ForwardOnlyStream(image);

        var result = await handler.HandleAsync(Command(source), Ct);

        var photo = Assert.IsType<UploadPhotoResult.Uploaded>(result).Photo;
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 14, 30, 0, TimeSpan.Zero), photo.DateTaken);
        Assert.Equal(LocationSource.Exif, photo.LocationSource);
        Assert.Equal(image, storage.Uploaded[photo.CloudinaryPublicId]);
        Assert.True(source.CanRead, "The caller's stream must not be disposed by the handler.");
    }

    [Fact]
    public async Task Normalizes_fields_and_saves_the_row()
    {
        var travis = await factory.CreatePersonAsync("Travis");
        var (handler, db, scope) = Create(new FakePhotoStorage());
        using var _ = scope;
        var command = Command(new MemoryStream(TestImages.Jpeg()), travis.Id) with { DateCategory = "   " };

        var photo = Assert.IsType<UploadPhotoResult.Uploaded>(await handler.HandleAsync(command, Ct)).Photo;

        var saved = await db.Photos.AsNoTracking().SingleAsync(p => p.Id == photo.Id, Ct);
        Assert.Equal(travis.Id, saved.CameraOwnerId);
        Assert.Null(saved.DateCategory);
        Assert.Equal(command.UploadedBy, saved.UploadedBy);
        Assert.Null(saved.LocationSource);
    }

    [Fact]
    public async Task Invalid_command_is_rejected_before_anything_is_stored()
    {
        var storage = new FakePhotoStorage();
        var (handler, _, scope) = Create(storage);
        using var _ = scope;
        var command = Command(new MemoryStream(TestImages.Jpeg())) with { Lat = 95, DateCategory = new string('x', 51) };

        var result = await handler.HandleAsync(command, Ct);

        var errors = Assert.IsType<UploadPhotoResult.Invalid>(result).Errors;
        Assert.Equal(["DateCategory", "Lat"], errors.Keys.Order());
        Assert.Empty(storage.Uploaded);
    }

    [Fact]
    public async Task Unknown_camera_owner_is_rejected_before_anything_is_stored()
    {
        var storage = new FakePhotoStorage();
        var (handler, _, scope) = Create(storage);
        using var _ = scope;

        var result = await handler.HandleAsync(Command(new MemoryStream(TestImages.Jpeg()), Guid.CreateVersion7()), Ct);

        var errors = Assert.IsType<UploadPhotoResult.Invalid>(result).Errors;
        Assert.Equal(["CameraOwnerId"], errors.Keys);
        Assert.Empty(storage.Uploaded);
    }

    [Fact]
    public async Task Unknown_camera_owner_and_place_are_both_reported()
    {
        var storage = new FakePhotoStorage();
        var (handler, _, scope) = Create(storage);
        using var _ = scope;
        var command = Command(new MemoryStream(TestImages.Jpeg()), Guid.CreateVersion7()) with { PlaceId = Guid.CreateVersion7() };

        var result = await handler.HandleAsync(command, Ct);

        var errors = Assert.IsType<UploadPhotoResult.Invalid>(result).Errors;
        Assert.Equal(["CameraOwnerId", "PlaceId"], errors.Keys.Order());
        Assert.Empty(storage.Uploaded);
    }

    [Theory]
    [InlineData(FakePhotoStorage.Failure.InvalidImage, typeof(UploadPhotoResult.InvalidImage))]
    [InlineData(FakePhotoStorage.Failure.Unavailable, typeof(UploadPhotoResult.StorageUnavailable))]
    public async Task Storage_failures_become_results_and_save_nothing(FakePhotoStorage.Failure failure, Type expected)
    {
        var (handler, db, scope) = Create(new FakePhotoStorage { FailWith = failure });
        using var _ = scope;
        var owner = await factory.CreatePersonAsync();

        var result = await handler.HandleAsync(Command(new MemoryStream(TestImages.Jpeg()), owner.Id), Ct);

        Assert.IsType(expected, result);
        Assert.False(await db.Photos.AnyAsync(p => p.CameraOwnerId == owner.Id, Ct));
    }
}
