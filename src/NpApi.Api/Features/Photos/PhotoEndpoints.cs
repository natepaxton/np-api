using System.Diagnostics;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NpApi.Api.Auth;
using NpApi.Api.Data;
using NpApi.Api.Features.Photos.Storage;
using NpApi.Api.Features.Places;

namespace NpApi.Api.Features.Photos;

// Field names match np-web's yellowstone photos.json so the frontend can switch to the API as-is.
// cameraOwner is the person's display name (as in photos.json); cameraOwnerId identifies them.
// lat/lng are the photo's own point; place carries the fallback point for the map.
// Load photos with .Include(p => p.CameraOwner).Include(p => p.Place) before mapping.
public sealed record PhotoResponse(
    Guid Id,
    string Filename,
    Guid? CameraOwnerId,
    string? CameraOwner,
    PlaceResponse? Place,
    double? Lat,
    double? Lng,
    LocationSource? LocationSource,
    DateTimeOffset? DateTaken,
    string? DateCategory,
    IReadOnlyList<string> Tags,
    int Width,
    int Height,
    string Thumbnail,
    string Medium,
    string Full,
    DateTimeOffset UploadedAt)
{
    public static PhotoResponse From(Photo photo, string cloudName) => new(
        photo.Id,
        photo.Filename,
        photo.CameraOwnerId,
        photo.CameraOwner?.DisplayName,
        photo.Place is null ? null : PlaceResponse.From(photo.Place),
        photo.Latitude,
        photo.Longitude,
        photo.LocationSource,
        photo.DateTaken,
        photo.DateCategory,
        photo.Tags,
        photo.Width,
        photo.Height,
        PhotoUrls.Thumbnail(cloudName, photo.CloudinaryPublicId),
        PhotoUrls.Medium(cloudName, photo.CloudinaryPublicId),
        PhotoUrls.Full(cloudName, photo.CloudinaryPublicId),
        photo.UploadedAt);
}

// multipart/form-data. Lat/Lng and DateTaken override what the image's EXIF says.
public sealed class UploadPhotoForm
{
    public IFormFile? File { get; init; }
    public Guid? CameraOwnerId { get; init; }
    public Guid? PlaceId { get; init; }
    public string? DateCategory { get; init; }
    public double? Lat { get; init; }
    public double? Lng { get; init; }
    public DateTimeOffset? DateTaken { get; init; }
}

public static class PhotoEndpoints
{
    // Cloudinary's free plan rejects images over 10 MB; fail fast with a clear message instead.
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/heic", "image/heif",
    };

    public static IHostApplicationBuilder AddPhotos(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<CloudinaryOptions>()
            .BindConfiguration(CloudinaryOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CloudinaryOptions>>().Value;
            var cloudinary = new Cloudinary(new Account(options.CloudName, options.ApiKey, options.ApiSecret));
            cloudinary.Api.Secure = true;
            return cloudinary;
        });
        builder.Services.AddSingleton<IPhotoStorage, CloudinaryPhotoStorage>();
        builder.Services.AddScoped<UploadPhotoHandler>();

        return builder;
    }

    public static IEndpointRouteBuilder MapPhotos(this IEndpointRouteBuilder app)
    {
        var photos = app.MapGroup("/photos").WithTags("Photos");

        photos.MapGet("/", async (AppDbContext db, IOptions<CloudinaryOptions> cloudinary, CancellationToken ct) =>
        {
            var rows = await db.Photos.AsNoTracking()
                .Include(p => p.CameraOwner)
                .Include(p => p.Place)
                .OrderBy(p => p.DateTaken == null)
                .ThenBy(p => p.DateTaken)
                .ThenBy(p => p.UploadedAt)
                .ToListAsync(ct);

            return TypedResults.Ok(rows.Select(p => PhotoResponse.From(p, cloudinary.Value.CloudName)));
        }).RequireAuthorization(Permissions.ReadPhotos);

        photos.MapGet("/{id:guid}", async Task<Results<Ok<PhotoResponse>, NotFound>> (
            Guid id, AppDbContext db, IOptions<CloudinaryOptions> cloudinary, CancellationToken ct) =>
        {
            var photo = await db.Photos.AsNoTracking()
                .Include(p => p.CameraOwner)
                .Include(p => p.Place)
                .SingleOrDefaultAsync(p => p.Id == id, ct);

            return photo is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(PhotoResponse.From(photo, cloudinary.Value.CloudName));
        }).WithName("GetPhoto").RequireAuthorization(Permissions.ReadPhotos);

        photos.MapPost("/", UploadAsync)
            .RequireAuthorization(Permissions.WritePhotos)
            // Bearer-token API: no cookies, so no CSRF exposure to protect against.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes + 1024 * 1024))
            .Accepts<UploadPhotoForm>("multipart/form-data");

        return app;
    }

    // HTTP only: check the uploaded file, hand the rest to UploadPhotoHandler, map its result.
    private static async Task<Results<CreatedAtRoute<PhotoResponse>, ValidationProblem, ProblemHttpResult>> UploadAsync(
        [FromForm] UploadPhotoForm form,
        HttpContext http,
        UploadPhotoHandler handler,
        IOptions<CloudinaryOptions> cloudinary,
        CancellationToken ct)
    {
        var command = new UploadPhotoCommand(
            Stream.Null,
            form.File?.FileName ?? "",
            http.User.GetUserId(),
            form.CameraOwnerId,
            form.PlaceId,
            form.DateCategory,
            form.Lat,
            form.Lng,
            form.DateTaken);

        var errors = UploadPhotoHandler.Validate(command);
        if (ValidateFile(form.File) is { } fileError)
        {
            errors[nameof(form.File)] = [fileError];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await using var content = form.File!.OpenReadStream();
        return await handler.HandleAsync(command with { Content = content }, ct) switch
        {
            UploadPhotoResult.Uploaded(var photo) =>
                TypedResults.CreatedAtRoute(PhotoResponse.From(photo, cloudinary.Value.CloudName), "GetPhoto", new { id = photo.Id }),
            UploadPhotoResult.Invalid(var fieldErrors) =>
                TypedResults.ValidationProblem(fieldErrors),
            UploadPhotoResult.InvalidImage =>
                TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(form.File)] = ["The file is not an image the photo host can process."],
                }),
            UploadPhotoResult.StorageUnavailable =>
                TypedResults.Problem(
                    title: "Photo storage unavailable",
                    detail: "The image could not be uploaded. Try again later.",
                    statusCode: StatusCodes.Status502BadGateway),
            var other => throw new UnreachableException($"Unhandled upload result {other}."),
        };
    }

    private static string? ValidateFile(IFormFile? file) => file switch
    {
        null or { Length: 0 } => "An image file is required.",
        { Length: > MaxUploadBytes } => $"The image must be {MaxUploadBytes / (1024 * 1024)} MB or smaller.",
        _ when !AllowedContentTypes.Contains(file.ContentType) => "Only JPEG, PNG, WebP and HEIC images are supported.",
        _ => null,
    };
}
