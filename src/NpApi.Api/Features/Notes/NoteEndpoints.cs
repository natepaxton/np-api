using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NpApi.Api.Auth;
using NpApi.Api.Data;

namespace NpApi.Api.Features.Notes;

public sealed record CreateNoteRequest(string Title, string? Body);

public static class NoteEndpoints
{
    public static IServiceCollection AddNotes(this IServiceCollection services)
    {
        services.AddScoped<NoteQueries>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotes(this IEndpointRouteBuilder app)
    {
        var notes = app.MapGroup("/notes").WithTags("Notes");

        notes.MapGet("/", async (HttpContext http, NoteQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.ListForOwnerAsync(http.User.GetUserId(), ct)));

        notes.MapGet("/{id:guid}", async Task<Results<Ok<Note>, NotFound>> (
            Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var note = await db.Notes.AsNoTracking()
                .SingleOrDefaultAsync(n => n.Id == id && n.OwnerId == http.User.GetUserId(), ct);

            return note is null ? TypedResults.NotFound() : TypedResults.Ok(note);
        }).WithName("GetNote");

        notes.MapPost("/", async Task<Results<CreatedAtRoute<Note>, ValidationProblem>> (
            CreateNoteRequest request, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Title)] = ["Title is required."]
                });
            }

            var note = new Note { OwnerId = http.User.GetUserId(), Title = request.Title, Body = request.Body };
            db.Notes.Add(note);
            await db.SaveChangesAsync(ct);

            return TypedResults.CreatedAtRoute(note, "GetNote", new { id = note.Id });
        });

        return app;
    }
}
