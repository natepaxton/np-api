using NpApi.Api.Features.Photos.Storage;

namespace NpApi.Api.Tests.Features.Photos;

public class PhotoUrlsTests
{
    private const string PublicId = "np-api/photos/0199b6c0-0000-7000-8000-000000000000";

    [Fact]
    public void Builds_delivery_urls_for_each_size()
    {
        Assert.Equal($"https://res.cloudinary.com/demo/image/upload/c_fill,w_200,h_200,f_auto,q_auto/{PublicId}",
            PhotoUrls.Thumbnail("demo", PublicId));
        Assert.Equal($"https://res.cloudinary.com/demo/image/upload/c_limit,w_800,f_auto,q_auto/{PublicId}",
            PhotoUrls.Medium("demo", PublicId));
        Assert.Equal($"https://res.cloudinary.com/demo/image/upload/f_auto,q_auto/{PublicId}",
            PhotoUrls.Full("demo", PublicId));
    }
}
