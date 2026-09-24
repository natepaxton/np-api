using System.Collections.Concurrent;
using NpApi.Api.Features.Photos.Storage;

namespace NpApi.Api.Tests.Infrastructure;

// Stands in for Cloudinary. Records uploads and deletes; a failure mode makes every call fail.
public sealed class FakePhotoStorage : IPhotoStorage
{
    public enum Failure { None, InvalidImage, Unavailable }

    public Failure FailWith { get; init; } = Failure.None;

    // Forces every upload to report this public ID (to provoke a duplicate-key failure on save).
    public string? FixedPublicId { get; init; }

    public ConcurrentDictionary<string, byte[]> Uploaded { get; } = new();
    public ConcurrentBag<string> Deleted { get; } = [];

    public async Task<StoredPhoto> UploadAsync(Stream content, string fileName, string publicId, CancellationToken cancellationToken)
    {
        switch (FailWith)
        {
            case Failure.InvalidImage:
                throw new PhotoStorageException("Invalid image file", isInvalidImage: true);
            case Failure.Unavailable:
                throw new PhotoStorageException("Cloudinary is down", isInvalidImage: false);
        }

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);

        var storedId = FixedPublicId ?? publicId;
        Uploaded[storedId] = copy.ToArray();
        return new StoredPhoto(storedId, $"https://res.cloudinary.com/test-cloud/image/upload/v1/{storedId}.jpg", 4032, 3024);
    }

    public Task DeleteAsync(string publicId, CancellationToken cancellationToken)
    {
        Deleted.Add(publicId);
        return Task.CompletedTask;
    }
}
