using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NpApi.Api.Auth;
using NpApi.Api.Data;

namespace NpApi.Api.Features.Places;

// Country is an ISO 3166-1 alpha-2 code ("US", case-insensitive); see GET /countries.
public sealed record PlaceRequest(string? City, string? StateProvince, string? Country, double? Lat, double? Lng);

public sealed record PlaceResponse(
    Guid Id,
    string? City,
    string? StateProvince,
    CountryCode? Country,
    string? CountryName,
    double? Lat,
    double? Lng,
    string DisplayName)
{
    public static PlaceResponse From(Place place) => new(
        place.Id,
        place.City,
        place.StateProvince,
        place.CountryCode,
        place.CountryCode is { } code ? Places.Country.Names[code] : null,
        place.Latitude,
        place.Longitude,
        place.DisplayName);
}

public sealed record CountryResponse(CountryCode Code, string Name);

public static class PlacesEndpoints
{
    public static IEndpointRouteBuilder MapPlaces(this IEndpointRouteBuilder app)
    {
        // The supported countries, from the lookup table (for pickers).
        app.MapGet("/countries", async (AppDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.Countries.AsNoTracking()
                    .OrderBy(c => c.Name)
                    .Select(c => new CountryResponse(c.Code, c.Name))
                    .ToListAsync(ct)))
            .WithTags("Places")
            .RequireAuthorization(Permissions.ReadPlaces);

        var places = app.MapGroup("/places").WithTags("Places");

        places.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Places.AsNoTracking()
                .OrderBy(p => p.CountryCode).ThenBy(p => p.StateProvince).ThenBy(p => p.City)
                .ToListAsync(ct);

            return TypedResults.Ok(rows.Select(PlaceResponse.From));
        }).RequireAuthorization(Permissions.ReadPlaces);

        places.MapGet("/{id:guid}", async Task<Results<Ok<PlaceResponse>, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
            await db.Places.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) is { } place
                ? TypedResults.Ok(PlaceResponse.From(place))
                : TypedResults.NotFound())
            .WithName("GetPlace")
            .RequireAuthorization(Permissions.ReadPlaces);

        places.MapPost("/", async Task<Results<CreatedAtRoute<PlaceResponse>, ValidationProblem>> (
            PlaceRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (Validate(request) is { Count: > 0 } errors)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var place = new Place();
            Apply(request, place);
            db.Places.Add(place);
            await db.SaveChangesAsync(ct);

            return TypedResults.CreatedAtRoute(PlaceResponse.From(place), "GetPlace", new { id = place.Id });
        }).RequireAuthorization(Permissions.WritePlaces);

        places.MapPut("/{id:guid}", async Task<Results<Ok<PlaceResponse>, NotFound, ValidationProblem>> (
            Guid id, PlaceRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (Validate(request) is { Count: > 0 } errors)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var place = await db.Places.FindAsync([id], ct);
            if (place is null)
            {
                return TypedResults.NotFound();
            }

            Apply(request, place);
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(PlaceResponse.From(place));
        }).RequireAuthorization(Permissions.WritePlaces);

        // Photos linked to the place are unlinked by the database (ON DELETE SET NULL), not deleted.
        places.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
            await db.Places.Where(p => p.Id == id).ExecuteDeleteAsync(ct) > 0
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
            .RequireAuthorization(Permissions.WritePlaces);

        return app;
    }

    private static Dictionary<string, string[]> Validate(PlaceRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var (field, value) in new[]
                 {
                     (nameof(request.City), request.City),
                     (nameof(request.StateProvince), request.StateProvince),
                 })
        {
            if (value?.Trim().Length > Place.MaxNameLength)
            {
                errors[field] = [$"Must be {Place.MaxNameLength} characters or fewer."];
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Country) && !Places.Country.TryParse(request.Country, out _))
        {
            errors[nameof(request.Country)] =
                [$"Country must be one of: {string.Join(", ", Enum.GetNames<CountryCode>())}."];
        }

        if (request.Lat.HasValue != request.Lng.HasValue)
        {
            errors[nameof(request.Lat)] = ["Provide both lat and lng, or neither."];
        }
        else if (request.Lat is < -90 or > 90 || request.Lng is < -180 or > 180)
        {
            errors[nameof(request.Lat)] = ["Lat must be between -90 and 90 and lng between -180 and 180."];
        }

        if (string.IsNullOrWhiteSpace(request.City) && string.IsNullOrWhiteSpace(request.StateProvince)
            && string.IsNullOrWhiteSpace(request.Country) && request.Lat is null)
        {
            errors[""] = ["A place needs at least a city, state/province, country, or lat/lng."];
        }

        return errors;
    }

    // Trimmed; blank names are stored as null.
    private static void Apply(PlaceRequest request, Place place)
    {
        place.City = Normalize(request.City);
        place.StateProvince = Normalize(request.StateProvince);
        place.CountryCode = Places.Country.TryParse(request.Country, out var code) ? code : null;
        place.SetPoint(request.Lat, request.Lng);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
