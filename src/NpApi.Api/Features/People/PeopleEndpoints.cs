using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NpApi.Api.Auth;
using NpApi.Api.Data;
using Npgsql;

namespace NpApi.Api.Features.People;

public sealed record PersonRequest(string? FirstName, string? MiddleName, string? LastName);

public sealed record PersonResponse(Guid Id, string FirstName, string? MiddleName, string? LastName, string DisplayName)
{
    public static PersonResponse From(Person person) =>
        new(person.Id, person.FirstName, person.MiddleName, person.LastName, person.DisplayName);
}

public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeople(this IEndpointRouteBuilder app)
    {
        var people = app.MapGroup("/people").WithTags("People");

        people.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var rows = await db.People.AsNoTracking()
                .OrderBy(p => p.FirstName).ThenBy(p => p.LastName).ThenBy(p => p.MiddleName)
                .ToListAsync(ct);

            return TypedResults.Ok(rows.Select(PersonResponse.From));
        }).RequireAuthorization(Permissions.ReadPeople);

        people.MapGet("/{id:guid}", async Task<Results<Ok<PersonResponse>, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
            await db.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) is { } person
                ? TypedResults.Ok(PersonResponse.From(person))
                : TypedResults.NotFound())
            .WithName("GetPerson")
            .RequireAuthorization(Permissions.ReadPeople);

        people.MapPost("/", async Task<Results<CreatedAtRoute<PersonResponse>, ValidationProblem>> (
            PersonRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (Validate(request) is { Count: > 0 } errors)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var person = new Person { FirstName = request.FirstName!.Trim() };
            Apply(request, person);
            db.People.Add(person);
            await db.SaveChangesAsync(ct);

            return TypedResults.CreatedAtRoute(PersonResponse.From(person), "GetPerson", new { id = person.Id });
        }).RequireAuthorization(Permissions.WritePeople);

        people.MapPut("/{id:guid}", async Task<Results<Ok<PersonResponse>, NotFound, ValidationProblem>> (
            Guid id, PersonRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (Validate(request) is { Count: > 0 } errors)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var person = await db.People.FindAsync([id], ct);
            if (person is null)
            {
                return TypedResults.NotFound();
            }

            Apply(request, person);
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(PersonResponse.From(person));
        }).RequireAuthorization(Permissions.WritePeople);

        people.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound, ProblemHttpResult>> (
            Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var person = await db.People.FindAsync([id], ct);
            if (person is null)
            {
                return TypedResults.NotFound();
            }

            var ownedPhotos = await db.Photos.CountAsync(p => p.CameraOwnerId == id, ct);
            if (ownedPhotos > 0)
            {
                return CameraOwnerConflict(ownedPhotos);
            }

            // Tags (photo_people rows) are removed with the person by the database.
            db.People.Remove(person);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                // A photo was assigned to them between the check and the delete.
                return CameraOwnerConflict(null);
            }

            return TypedResults.NoContent();
        }).RequireAuthorization(Permissions.WritePeople);

        return app;
    }

    private static ProblemHttpResult CameraOwnerConflict(int? photoCount) => TypedResults.Problem(
        title: "Person owns photos",
        detail: photoCount is { } count
            ? $"This person is the camera owner of {count} photo(s). Reassign those photos before deleting them."
            : "This person is the camera owner of one or more photos. Reassign those photos before deleting them.",
        statusCode: StatusCodes.Status409Conflict);

    private static Dictionary<string, string[]> Validate(PersonRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            errors[nameof(request.FirstName)] = ["First name is required."];
        }

        foreach (var (field, value) in new[]
                 {
                     (nameof(request.FirstName), request.FirstName),
                     (nameof(request.MiddleName), request.MiddleName),
                     (nameof(request.LastName), request.LastName),
                 })
        {
            if (value?.Trim().Length > Person.MaxNameLength && !errors.ContainsKey(field))
            {
                errors[field] = [$"Must be {Person.MaxNameLength} characters or fewer."];
            }
        }

        return errors;
    }

    // Trimmed; blank optional names are stored as null.
    private static void Apply(PersonRequest request, Person person)
    {
        person.FirstName = request.FirstName!.Trim();
        person.MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim();
        person.LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim();
    }
}
