using System.ComponentModel.DataAnnotations;

namespace NpApi.Api.Auth;

public sealed class Auth0Options
{
    public const string SectionName = "Auth0";

    // Tenant domain without scheme, e.g. "my-tenant.us.auth0.com" (or a custom domain).
    [Required]
    public string Domain { get; init; } = "";

    // The API Identifier configured in Auth0 > Applications > APIs, e.g. "https://api.example.com".
    [Required]
    public string Audience { get; init; } = "";

    // Accepts "tenant.auth0.com" or "https://tenant.auth0.com/"; Auth0's issuer is always "https://{domain}/".
    public string Authority =>
        $"https://{Domain.Replace("https://", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/')}/";
}
