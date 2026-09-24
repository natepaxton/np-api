using NpApi.Api.Auth;

namespace NpApi.Api.Tests.Auth;

public class Auth0OptionsTests
{
    [Theory]
    [InlineData("tenant.auth0.com")]
    [InlineData("tenant.auth0.com/")]
    [InlineData("https://tenant.auth0.com")]
    [InlineData("https://tenant.auth0.com/")]
    [InlineData("HTTPS://tenant.auth0.com")]
    public void Authority_is_normalized_to_auth0_issuer_format(string domain)
    {
        var options = new Auth0Options { Domain = domain, Audience = "np-api" };

        Assert.Equal("https://tenant.auth0.com/", options.Authority);
    }
}
