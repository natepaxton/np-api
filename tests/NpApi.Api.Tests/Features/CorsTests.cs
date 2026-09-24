using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Features;

public class CorsTests(ApiFactory factory)
{
    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/me");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        return await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("http://localhost:4300")] // sandbox
    [InlineData("http://localhost:4301")] // yellowstone
    [InlineData("http://localhost:4302")] // roadie
    public async Task Preflight_from_frontend_origin_is_allowed(string origin)
    {
        var response = await PreflightAsync(origin);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed));
        Assert.Equal(origin, Assert.Single(allowed));
    }

    [Fact]
    public async Task Preflight_from_unknown_origin_is_not_allowed()
    {
        var response = await PreflightAsync("https://evil.example");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
