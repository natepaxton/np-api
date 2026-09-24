using Microsoft.Extensions.Options;
using NpApi.Api.Data;
using NpApi.Api.Features.People;
using NpApi.Api.Features.Photos.Storage;
using NpApi.Api.Features.Places;

namespace NpApi.Api.Features.Photos;

// Everything needed to add a photo, with no HTTP types, so the handler can serve the API and
// non-HTTP callers (e.g. an import of existing photos). Lat/Lng and DateTaken override EXIF.
public sealed record UploadPhotoCommand(
    Stream Content,
    string FileName,
    string UploadedBy,
    Guid? CameraOwnerId = null,
    Guid? PlaceId = null,
    string? DateCategory = null,
    double? Lat = null,
    double? Lng = null,
    DateTimeOffset? DateTaken = null);

public abstract record UploadPhotoResult
{
    private UploadPhotoResult() { }

    public sealed record Uploaded(Photo Photo) : UploadPhotoResult;

    // Field errors keyed by command property name (these match the API's form field names).
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UploadPhotoResult;

    // The image host rejected the file as not an image it can process.
    public sealed record InvalidImage : UploadPhotoResult;

    // The image host is unavailable; nothing was saved.
    public sealed record StorageUnavailable : UploadPhotoResult;
}

// Adds a photo: validates, reads EXIF, stores the image, saves the row, and removes the stored
// image again if the row can't be saved.
public sealed class UploadPhotoHandler(
    AppDbContext db,
    IPhotoStorage storage,
    IOptions<CloudinaryOptions> cloudinary,
    ILogger<UploadPhotoHandler> logger)
{
    public async Task<UploadPhotoResult> HandleAsync(UploadPhotoCommand command, CancellationToken cancellationToken)
    {
        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return new UploadPhotoResult.Invalid(errors);
        }

        // Referenced rows are checked before uploading, so an unknown id never leaves an orphaned
        // image behind.
        var cameraOwner = command.CameraOwnerId is { } cameraOwnerId
            ? await db.People.FindAsync([cameraOwnerId], cancellationToken)
            : null;
        var place = command.PlaceId is { } placeId
            ? await db.Places.FindAsync([placeId], cancellationToken)
            : null;

        var missing = new Dictionary<string, string[]>();
        if (command.CameraOwnerId is not null && cameraOwner is null)
        {
            missing[nameof(command.CameraOwnerId)] = ["No person with this id."];
        }
        if (command.PlaceId is not null && place is null)
        {
            missing[nameof(command.PlaceId)] = ["No place with this id."];
        }
        if (missing.Count > 0)
        {
            return new UploadPhotoResult.Invalid(missing);
        }

        // Read twice (metadata, then upload), so work on our own seekable copy and leave the
        // caller's stream alone. Uploads are capped at 10 MB, so buffering in memory is fine.
        await using var content = await BufferAsync(command.Content, cancellationToken);
        var metadata = PhotoMetadataReader.Read(content);

        var id = Guid.CreateVersion7();
        var publicId = $"{cloudinary.Value.Folder.TrimEnd('/')}/{id}";

        StoredPhoto stored;
        try
        {
            content.Position = 0;
            stored = await storage.UploadAsync(content, command.FileName, publicId, cancellationToken);
        }
        catch (PhotoStorageException ex)
        {
            logger.LogWarning(ex, "Upload of {FileName} to photo storage failed", command.FileName);
            return ex.IsInvalidImage ? new UploadPhotoResult.InvalidImage() : new UploadPhotoResult.StorageUnavailable();
        }

        var photo = new Photo
        {
            Id = id,
            CloudinaryPublicId = stored.PublicId,
            Url = stored.Url,
            Filename = Path.GetFileName(command.FileName),
            CameraOwnerId = cameraOwner?.Id,
            CameraOwner = cameraOwner,
            PlaceId = place?.Id,
            Place = place,
            DateCategory = string.IsNullOrWhiteSpace(command.DateCategory) ? null : command.DateCategory.Trim(),
            DateTaken = (command.DateTaken ?? metadata.DateTaken)?.ToUniversalTime(),
            Width = stored.Width,
            Height = stored.Height,
            UploadedBy = command.UploadedBy,
        };

        if (command.Lat is { } lat && command.Lng is { } lng)
        {
            photo.SetLocation(lat, lng, LocationSource.Manual);
        }
        else if (metadata.Latitude is { } exifLat && metadata.Longitude is { } exifLng)
        {
            photo.SetLocation(exifLat, exifLng, LocationSource.Exif);
        }

        db.Photos.Add(photo);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Don't leave an orphaned image in storage when the row can't be saved.
            await TryDeleteAsync(stored.PublicId);
            throw;
        }

        return new UploadPhotoResult.Uploaded(photo);
    }

    // Field rules for a command, keyed by property name. HandleAsync applies them too; the endpoint
    // calls this first so file and field errors come back together in one response.
    public static Dictionary<string, string[]> Validate(UploadPhotoCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (command.DateCategory?.Trim().Length > 50)
        {
            errors[nameof(command.DateCategory)] = ["Date category must be 50 characters or fewer."];
        }

        if (command.Lat.HasValue != command.Lng.HasValue)
        {
            errors[nameof(command.Lat)] = ["Provide both lat and lng, or neither."];
        }
        else if (command.Lat is < -90 or > 90 || command.Lng is < -180 or > 180)
        {
            errors[nameof(command.Lat)] = ["Lat must be between -90 and 90 and lng between -180 and 180."];
        }

        return errors;
    }

    private static async Task<MemoryStream> BufferAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    private async Task TryDeleteAsync(string publicId)
    {
        try
        {
            await storage.DeleteAsync(publicId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Photo {PublicId} was uploaded but not saved, and could not be deleted from storage", publicId);
        }
    }
}
