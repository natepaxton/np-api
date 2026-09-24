using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NpApi.Api.Tests.Infrastructure;

// Issues tokens shaped like Auth0 access tokens, signed with a key that exists only in the test
// process. ApiFactory points the API's JWT validation at this issuer and key instead of Auth0.
public static class TestAuth
{
    public const string Domain = "test.auth0.local";
    public const string Issuer = $"https://{Domain}/";
    public const string Audience = "np-api";

    public static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32));

    public static string CreateToken(
        string userId,
        string[]? permissions = null,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(10);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            NotBefore = expiresAt.AddHours(-1),
            Expires = expiresAt,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = userId,
                ["permissions"] = permissions ?? [],
                ["scope"] = "openid profile email",
            },
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // A unique Auth0-style user id per call, so tests never see each other's data.
    public static string NewUserId() => $"auth0|test-{Guid.NewGuid():N}";
}
