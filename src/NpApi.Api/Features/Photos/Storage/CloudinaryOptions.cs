using System.ComponentModel.DataAnnotations;

namespace NpApi.Api.Features.Photos.Storage;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    // Public; it appears in every delivery URL.
    [Required]
    public string CloudName { get; init; } = "";

    // Secrets: user secrets on the AppHost locally, environment variables when deployed.
    [Required]
    public string ApiKey { get; init; } = "";

    [Required]
    public string ApiSecret { get; init; } = "";

    // Public ID prefix. Development uses a separate folder so local uploads never mix with production.
    [Required]
    public string Folder { get; init; } = "";
}
