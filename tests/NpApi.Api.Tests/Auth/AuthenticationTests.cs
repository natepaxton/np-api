using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Auth;

public class AuthenticationTests(ApiFactory factory)
{
    private sealed record MeResponse(string UserId, string[] Permissions, string[] Scopes);

    [Fact]
    public async Task Me_without_token_returns_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_valid_token_returns_caller_identity_and_permissions()
    {
        var userId = TestAuth.NewUserId();
        var client = factory.CreateClientFor(userId, "read:photos", "write:photos");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.NotNull(me);
        Assert.Equal(userId, me.UserId);
        Assert.Equal(["read:photos", "write:photos"], me.Permissions);
        Assert.Contains("openid", me.Scopes);
    }

    public static TheoryData<string, string> InvalidTokens => new()
    {
        { "wrong audience", TestAuth.CreateToken(TestAuth.NewUserId(), audience: "some-other-api") },
        { "expired", TestAuth.CreateToken(TestAuth.NewUserId(), expires: DateTime.UtcNow.AddHours(-1)) },
        {
            "signed with another key",
            TestAuth.CreateToken(TestAuth.NewUserId(), signingKey: new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)))
        },
        { "not a jwt", "not-a-token" },
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Me_with_invalid_token_returns_401(string reason, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"Expected 401 for token that is {reason}.");
    }

    [Fact]
    public async Task Routes_are_served_under_api_v1_only()
    {
        var client = factory.CreateClientFor(TestAuth.NewUserId());

        var response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
