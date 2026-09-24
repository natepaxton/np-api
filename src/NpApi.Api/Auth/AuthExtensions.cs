using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace NpApi.Api.Auth;

public static class AuthExtensions
{
    public static IHostApplicationBuilder AddAuth0(this IHostApplicationBuilder builder)
    {
        var auth0 = builder.Configuration.GetSection(Auth0Options.SectionName).Get<Auth0Options>() ?? new();

        builder.Services.AddOptions<Auth0Options>()
            .BindConfiguration(Auth0Options.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Signing keys are fetched from https://{Domain}/.well-known/openid-configuration.
                options.Authority = auth0.Authority;
                options.Audience = auth0.Audience;

                // Keep Auth0's claim names as-is ("sub", "permissions") instead of legacy SOAP URIs.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "sub";
            });

        var authorization = builder.Services.AddAuthorizationBuilder()
            // Secure by default: every endpoint requires a valid token unless it opts out with
            // AllowAnonymous() (health checks, OpenAPI document).
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        // One policy per Auth0 permission. Auth0 puts them in the token's "permissions" array
        // (RBAC with "Add Permissions in the Access Token").
        foreach (var permission in Permissions.All)
        {
            authorization.AddPolicy(permission, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("permissions", permission));
        }

        return builder;
    }

    // Auth0 user id, e.g. "auth0|65f..." or "google-oauth2|123...".
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("Token has no 'sub' claim.");
}
