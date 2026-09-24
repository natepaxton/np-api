using System.Net;
using System.Net.Http.Json;
using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Features;

public class NotesTests(ApiFactory factory)
{
    private sealed record NoteResponse(Guid Id, string OwnerId, string Title, string? Body, DateTimeOffset CreatedAt);
    private sealed record NoteSummaryResponse(Guid Id, string Title, DateTimeOffset CreatedAt);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_returns_201_with_location_of_the_new_note()
    {
        var userId = TestAuth.NewUserId();
        var client = factory.CreateClientFor(userId);

        var response = await client.PostAsJsonAsync("/api/v1/notes", new { title = "Old Faithful", body = "On time" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<NoteResponse>(Ct);
        Assert.NotNull(created);
        Assert.Equal(userId, created.OwnerId);
        Assert.Equal("Old Faithful", created.Title);
        Assert.Equal("On time", created.Body);

        var fetched = await client.GetFromJsonAsync<NoteResponse>(response.Headers.Location, Ct);
        Assert.Equal(created, fetched);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_without_title_returns_validation_problem(string title)
    {
        var client = factory.CreateClientFor(TestAuth.NewUserId());

        var response = await client.PostAsJsonAsync("/api/v1/notes", new { title }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // Exercises the Dapper query: snake_case column mapping, owner filter, and ordering.
    [Fact]
    public async Task List_returns_only_the_callers_notes_newest_first()
    {
        var me = factory.CreateClientFor(TestAuth.NewUserId());
        var someoneElse = factory.CreateClientFor(TestAuth.NewUserId());

        await me.PostAsJsonAsync("/api/v1/notes", new { title = "first" }, Ct);
        await someoneElse.PostAsJsonAsync("/api/v1/notes", new { title = "not mine" }, Ct);
        await me.PostAsJsonAsync("/api/v1/notes", new { title = "second" }, Ct);

        var notes = await me.GetFromJsonAsync<NoteSummaryResponse[]>("/api/v1/notes", Ct);

        Assert.NotNull(notes);
        Assert.Equal(["second", "first"], notes.Select(n => n.Title));
        Assert.All(notes, n =>
        {
            Assert.NotEqual(Guid.Empty, n.Id);
            Assert.True(n.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        });
    }

    // Regression: Dapper used EF's connection through a DI factory, so the container disposed it at
    // the end of the request and the next request on the same pooled DbContext failed.
    [Fact]
    public async Task Dapper_reads_and_ef_writes_can_alternate_across_requests()
    {
        var client = factory.CreateClientFor(TestAuth.NewUserId());

        for (var i = 0; i < 5; i++)
        {
            var list = await client.GetAsync("/api/v1/notes", Ct);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);

            var create = await client.PostAsJsonAsync("/api/v1/notes", new { title = $"note {i}" }, Ct);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }
    }

    [Fact]
    public async Task Get_another_users_note_returns_404()
    {
        var owner = factory.CreateClientFor(TestAuth.NewUserId());
        var created = await (await owner.PostAsJsonAsync("/api/v1/notes", new { title = "private" }, Ct))
            .Content.ReadFromJsonAsync<NoteResponse>(Ct);

        var intruder = factory.CreateClientFor(TestAuth.NewUserId());
        var response = await intruder.GetAsync($"/api/v1/notes/{created!.Id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Notes_require_authentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/notes", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
