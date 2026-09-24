using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Auth;

public class ConfigurationValidationTests(ApiFactory factory)
{
    [Fact]
    public void App_refuses_to_start_without_auth0_settings()
    {
        using var misconfigured = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:npdb", factory.ConnectionString);
            builder.UseSetting("Auth0:Domain", "");
            builder.UseSetting("Auth0:Audience", "");
        });

        var exception = Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());

        var validation = exception as OptionsValidationException ?? exception.InnerException as OptionsValidationException;
        Assert.NotNull(validation);
        Assert.Contains(validation.Failures, f => f.Contains("Domain"));
        Assert.Contains(validation.Failures, f => f.Contains("Audience"));
    }
}
