using System.Net;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace NpApi.Api.Features.Photos.Storage;

internal sealed class CloudinaryPhotoStorage(Cloudinary cloudinary, ILogger<CloudinaryPhotoStorage> logger) : IPhotoStorage
{
    public async Task<StoredPhoto> UploadAsync(Stream content, string fileName, string publicId, CancellationToken cancellationToken)
    {
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, content),
            // The full path is in the public ID, so it works in both fixed- and dynamic-folder accounts.
            PublicId = publicId,
            Overwrite = false,
            UseFilename = false,
            UniqueFilename = false,
        };

        ImageUploadResult result;
        try
        {
            result = await cloudinary.UploadAsync(uploadParams, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PhotoStorageException("Could not reach Cloudinary.", isInvalidImage: false, ex);
        }

        if (result.Error is not null)
        {
            // Cloudinary answers 400 for files it can't process ("Invalid image file").
            var invalid = result.StatusCode == HttpStatusCode.BadRequest;
            throw new PhotoStorageException($"Cloudinary upload failed: {result.Error.Message}", invalid);
        }

        return new StoredPhoto(result.PublicId, result.SecureUrl.ToString(), result.Width, result.Height);
    }

    public async Task DeleteAsync(string publicId, CancellationToken cancellationToken)
    {
        var result = await cloudinary.DestroyAsync(new DeletionParams(publicId) { Invalidate = true });

        if (result.Error is not null || result.Result is not ("ok" or "not found"))
        {
            logger.LogWarning("Cloudinary delete of {PublicId} failed: {Result} {Error}",
                publicId, result.Result, result.Error?.Message);
            throw new PhotoStorageException($"Cloudinary delete failed for {publicId}.", isInvalidImage: false);
        }
    }
}
