using CloudinaryDotNet;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NpApi.Api.Auth;
using NpApi.Api.Data;
using NpApi.Api.Features.Photos.Storage;

namespace NpApi.Api.Features.Photos;

// Field names match np-web's yellowstone photos.json so the frontend can switch to the API as-is.
public sealed record PhotoResponse(
    Guid Id,
    string Filename,
    string CameraOwner,
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
        photo.CameraOwner,
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
    public string? CameraOwner { get; init; }
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

        return builder;
    }

    public static IEndpointRouteBuilder MapPhotos(this IEndpointRouteBuilder app)
    {
        var photos = app.MapGroup("/photos").WithTags("Photos");

        photos.MapGet("/", async (AppDbContext db, IOptions<CloudinaryOptions> cloudinary, CancellationToken ct) =>
        {
            var rows = await db.Photos.AsNoTracking()
                .OrderBy(p => p.DateTaken == null)
                .ThenBy(p => p.DateTaken)
                .ThenBy(p => p.UploadedAt)
                .ToListAsync(ct);

            return TypedResults.Ok(rows.Select(p => PhotoResponse.From(p, cloudinary.Value.CloudName)));
        }).RequireAuthorization(Permissions.ReadPhotos);

        photos.MapGet("/{id:guid}", async Task<Results<Ok<PhotoResponse>, NotFound>> (
            Guid id, AppDbContext db, IOptions<CloudinaryOptions> cloudinary, CancellationToken ct) =>
        {
            var photo = await db.Photos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);

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

    private static async Task<Results<CreatedAtRoute<PhotoResponse>, ValidationProblem, ProblemHttpResult>> UploadAsync(
        [FromForm] UploadPhotoForm form,
        HttpContext http,
        AppDbContext db,
        IPhotoStorage storage,
        IOptions<CloudinaryOptions> cloudinary,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var errors = Validate(form);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var file = form.File!;
        await using var content = new MemoryStream((int)file.Length);
        await file.CopyToAsync(content, ct);

        content.Position = 0;
        var metadata = PhotoMetadataReader.Read(content);

        var id = Guid.CreateVersion7();
        var publicId = $"{cloudinary.Value.Folder.TrimEnd('/')}/{id}";

        StoredPhoto stored;
        try
        {
            content.Position = 0;
            stored = await storage.UploadAsync(content, file.FileName, publicId, ct);
        }
        catch (PhotoStorageException ex) when (ex.IsInvalidImage)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UploadPhotoForm.File)] = ["The file is not an image the photo host can process."],
            });
        }
        catch (PhotoStorageException)
        {
            return TypedResults.Problem(
                title: "Photo storage unavailable",
                detail: "The image could not be uploaded. Try again later.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var photo = new Photo
        {
            Id = id,
            CloudinaryPublicId = stored.PublicId,
            Url = stored.Url,
            Filename = Path.GetFileName(file.FileName),
            CameraOwner = form.CameraOwner!.Trim(),
            DateCategory = string.IsNullOrWhiteSpace(form.DateCategory) ? null : form.DateCategory.Trim(),
            DateTaken = (form.DateTaken ?? metadata.DateTaken)?.ToUniversalTime(),
            Width = stored.Width,
            Height = stored.Height,
            UploadedBy = http.User.GetUserId(),
        };

        if (form.Lat is { } lat && form.Lng is { } lng)
        {
            photo.SetLocation(lat, lng, LocationSource.Manual);
        }
        else if (metadata.Latitude is { } exifLat && metadata.Longitude is { } exifLng)
        {
            photo.SetLocation(exifLat, exifLng, LocationSource.Exif);
        }

        db.Photos.Add(photo);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Don't leave an orphaned image in Cloudinary when the row can't be saved.
            await TryDeleteAsync(storage, stored.PublicId, loggerFactory.CreateLogger(typeof(PhotoEndpoints)));
            throw;
        }

        return TypedResults.CreatedAtRoute(PhotoResponse.From(photo, cloudinary.Value.CloudName), "GetPhoto", new { id = photo.Id });
    }

    private static Dictionary<string, string[]> Validate(UploadPhotoForm form)
    {
        var errors = new Dictionary<string, string[]>();

        if (form.File is null || form.File.Length == 0)
        {
            errors[nameof(form.File)] = ["An image file is required."];
        }
        else if (form.File.Length > MaxUploadBytes)
        {
            errors[nameof(form.File)] = [$"The image must be {MaxUploadBytes / (1024 * 1024)} MB or smaller."];
        }
        else if (!AllowedContentTypes.Contains(form.File.ContentType))
        {
            errors[nameof(form.File)] = ["Only JPEG, PNG, WebP and HEIC images are supported."];
        }

        if (string.IsNullOrWhiteSpace(form.CameraOwner))
        {
            errors[nameof(form.CameraOwner)] = ["Camera owner is required."];
        }
        else if (form.CameraOwner.Trim().Length > 100)
        {
            errors[nameof(form.CameraOwner)] = ["Camera owner must be 100 characters or fewer."];
        }

        if (form.DateCategory?.Trim().Length > 50)
        {
            errors[nameof(form.DateCategory)] = ["Date category must be 50 characters or fewer."];
        }

        if (form.Lat.HasValue != form.Lng.HasValue)
        {
            errors[nameof(form.Lat)] = ["Provide both lat and lng, or neither."];
        }
        else if (form.Lat is < -90 or > 90 || form.Lng is < -180 or > 180)
        {
            errors[nameof(form.Lat)] = ["Lat must be between -90 and 90 and lng between -180 and 180."];
        }

        return errors;
    }

    private static async Task TryDeleteAsync(IPhotoStorage storage, string publicId, ILogger logger)
    {
        try
        {
            await storage.DeleteAsync(publicId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Photo {PublicId} was uploaded but not saved, and could not be deleted from storage", publicId);
        }
    }
}
