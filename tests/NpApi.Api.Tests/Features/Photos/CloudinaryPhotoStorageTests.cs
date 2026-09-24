using System.Net;
using System.Text;
using CloudinaryDotNet;
using Microsoft.Extensions.Logging.Abstractions;
using NpApi.Api.Features.Photos.Storage;

namespace NpApi.Api.Tests.Features.Photos;

// Exercises the Cloudinary adapter against canned HTTP responses (no network).
public class CloudinaryPhotoStorageTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request);
        }
    }

    private static (CloudinaryPhotoStorage Storage, StubHandler Handler) Create(HttpStatusCode status, string json)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        return Create(handler);
    }

    private static (CloudinaryPhotoStorage Storage, StubHandler Handler) Create(StubHandler handler)
    {
        var cloudinary = new Cloudinary(new Account("demo", "key", "secret"));
        cloudinary.Api.Client = new HttpClient(handler);
        return (new CloudinaryPhotoStorage(cloudinary, NullLogger<CloudinaryPhotoStorage>.Instance), handler);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upload_returns_what_cloudinary_stored()
    {
        var (storage, handler) = Create(HttpStatusCode.OK, """
            {"public_id":"np-api/photos/abc","secure_url":"https://res.cloudinary.com/demo/image/upload/v1/np-api/photos/abc.jpg","width":4032,"height":3024}
            """);

        var stored = await storage.UploadAsync(new MemoryStream([1, 2, 3]), "PXL_1.jpg", "np-api/photos/abc", Ct);

        Assert.Equal(new StoredPhoto("np-api/photos/abc", "https://res.cloudinary.com/demo/image/upload/v1/np-api/photos/abc.jpg", 4032, 3024), stored);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.EndsWith("/demo/image/upload", request.RequestUri!.AbsolutePath);
        Assert.Contains("np-api/photos/abc", body);
    }

    [Fact]
    public async Task Upload_rejected_as_invalid_is_flagged_as_invalid_image()
    {
        var (storage, _) = Create(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid image file"}}""");

        var ex = await Assert.ThrowsAsync<PhotoStorageException>(() =>
            storage.UploadAsync(new MemoryStream([1]), "x.jpg", "id", Ct));

        Assert.True(ex.IsInvalidImage);
        Assert.Contains("Invalid image file", ex.Message);
    }

    [Fact]
    public async Task Upload_server_error_is_not_an_invalid_image()
    {
        var (storage, _) = Create(HttpStatusCode.InternalServerError, """{"error":{"message":"General Error"}}""");

        var ex = await Assert.ThrowsAsync<PhotoStorageException>(() =>
            storage.UploadAsync(new MemoryStream([1]), "x.jpg", "id", Ct));

        Assert.False(ex.IsInvalidImage);
    }

    [Fact]
    public async Task Upload_network_failure_becomes_a_storage_exception()
    {
        var (storage, _) = Create(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        var ex = await Assert.ThrowsAsync<PhotoStorageException>(() =>
            storage.UploadAsync(new MemoryStream([1]), "x.jpg", "id", Ct));

        Assert.False(ex.IsInvalidImage);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("not found")]
    public async Task Delete_succeeds_when_asset_is_gone(string result)
    {
        var (storage, handler) = Create(HttpStatusCode.OK, $$"""{"result":"{{result}}"}""");

        await storage.DeleteAsync("np-api/photos/abc", Ct);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.EndsWith("/demo/image/destroy", request.RequestUri!.AbsolutePath);
        Assert.Contains("np-api/photos/abc", body);
    }

    [Fact]
    public async Task Delete_failure_throws()
    {
        var (storage, _) = Create(HttpStatusCode.InternalServerError, """{"error":{"message":"General Error"}}""");

        await Assert.ThrowsAsync<PhotoStorageException>(() => storage.DeleteAsync("np-api/photos/abc", Ct));
    }
}
