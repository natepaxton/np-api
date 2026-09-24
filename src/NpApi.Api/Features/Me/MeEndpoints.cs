using NpApi.Api.Auth;

namespace NpApi.Api.Features.Me;

public static class MeEndpoints
{
    // Echoes the caller's identity — handy for checking an Auth0 token from Postman.
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me", (HttpContext http) => TypedResults.Ok(new
        {
            userId = http.User.GetUserId(),
            permissions = http.User.FindAll("permissions").Select(c => c.Value),
            scopes = http.User.FindFirst("scope")?.Value?.Split(' ') ?? []
        })).WithTags("Me");

        return app;
    }
}
