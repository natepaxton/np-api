namespace NpApi.Api.Features.Photos.Storage;

// The image host (Cloudinary). An interface because it's an external service: tests replace it
// with a fake instead of uploading.
public interface IPhotoStorage
{
    Task<StoredPhoto> UploadAsync(Stream content, string fileName, string publicId, CancellationToken cancellationToken);

    Task DeleteAsync(string publicId, CancellationToken cancellationToken);
}

public sealed record StoredPhoto(string PublicId, string Url, int Width, int Height);

public sealed class PhotoStorageException(string message, bool isInvalidImage, Exception? inner = null)
    : Exception(message, inner)
{
    // The host rejected the file itself (not an image, unsupported format), as opposed to being
    // unavailable. Maps to 400 instead of 502.
    public bool IsInvalidImage { get; } = isInvalidImage;
}
