using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Health;

public class HealthCheckTests(ApiFactory factory)
{
    [Fact]
    public async Task Alive_is_anonymous_and_excludes_the_database()
    {
        var response = await factory.CreateClient().GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("checks").TryGetProperty("AppDbContext", out _));
    }

    [Fact]
    public async Task Health_is_anonymous_and_includes_a_healthy_database()
    {
        var response = await factory.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("AppDbContext").GetProperty("status").GetString());
    }
}
