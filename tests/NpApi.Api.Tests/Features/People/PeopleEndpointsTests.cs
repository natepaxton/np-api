using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NpApi.Api.Auth;
using NpApi.Api.Data;
using NpApi.Api.Features.People;
using NpApi.Api.Features.Photos;
using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Features.People;

public class PeopleEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Editor() => factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPeople, Permissions.WritePeople);

    private async Task<PersonResponse> CreateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/people", request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PersonResponse>(Ct))!;
    }

    private async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Photo> InsertPhotoAsync(AppDbContext db, Guid? cameraOwnerId = null)
    {
        var photo = new Photo
        {
            CloudinaryPublicId = $"np-api/test/{Guid.CreateVersion7()}",
            Url = "https://example.test/photo.jpg",
            Filename = "photo.jpg",
            UploadedBy = TestAuth.NewUserId(),
            CameraOwnerId = cameraOwnerId,
        };
        db.Photos.Add(photo);
        await db.SaveChangesAsync(Ct);
        return photo;
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/people", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reading_requires_read_people()
    {
        var photosOnly = factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPhotos, Permissions.WritePhotos);

        Assert.Equal(HttpStatusCode.Forbidden, (await photosOnly.GetAsync("/api/v1/people", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await photosOnly.GetAsync($"/api/v1/people/{Guid.CreateVersion7()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Changing_people_requires_write_people()
    {
        var reader = factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPeople);
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/v1/people", new { firstName = "Amy" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"/api/v1/people/{id}", new { firstName = "Amy" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"/api/v1/people/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_trims_names_and_can_be_fetched()
    {
        var client = Editor();

        var response = await client.PostAsJsonAsync("/api/v1/people",
            new { firstName = "  Laura ", middleName = "   ", lastName = " Paxton" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var person = (await response.Content.ReadFromJsonAsync<PersonResponse>(Ct))!;
        Assert.Equal(("Laura", null, "Paxton", "Laura Paxton"), (person.FirstName, person.MiddleName, person.LastName, person.DisplayName));

        var fetched = await client.GetFromJsonAsync<PersonResponse>(response.Headers.Location, Ct);
        Assert.Equal(person, fetched);
    }

    public static TheoryData<string, object> InvalidPeople => new()
    {
        { "FirstName", new { lastName = "Paxton" } },
        { "FirstName", new { firstName = "   " } },
        { "FirstName", new { firstName = new string('x', Person.MaxNameLength + 1) } },
        { "MiddleName", new { firstName = "Amy", middleName = new string('x', Person.MaxNameLength + 1) } },
        { "LastName", new { firstName = "Amy", lastName = new string('x', Person.MaxNameLength + 1) } },
    };

    [Theory]
    [MemberData(nameof(InvalidPeople))]
    public async Task Invalid_names_return_validation_problem(string field, object request)
    {
        var client = Editor();

        foreach (var response in new[]
                 {
                     await client.PostAsJsonAsync("/api/v1/people", request, Ct),
                     await client.PutAsJsonAsync($"/api/v1/people/{(await CreateAsync(client, new { firstName = "Amy" })).Id}", request, Ct),
                 })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Assert.True(errors.TryGetProperty(field, out _), $"Expected an error for {field}: {errors}");
        }
    }

    [Fact]
    public async Task List_is_sorted_by_first_then_last_name()
    {
        var client = Editor();
        var suffix = Guid.NewGuid().ToString("N");
        var zed = await CreateAsync(client, new { firstName = $"Zed{suffix}" });
        var amyB = await CreateAsync(client, new { firstName = $"Amy{suffix}", lastName = "B" });
        var amyA = await CreateAsync(client, new { firstName = $"Amy{suffix}", lastName = "A" });

        var people = await client.GetFromJsonAsync<PersonResponse[]>("/api/v1/people", Ct);

        Assert.Equal([amyA.Id, amyB.Id, zed.Id], people!.Where(p => p.FirstName.EndsWith(suffix)).Select(p => p.Id));
    }

    [Fact]
    public async Task Update_replaces_names()
    {
        var client = Editor();
        var person = await CreateAsync(client, new { firstName = "Nate", middleName = "M", lastName = "Paxton" });

        var response = await client.PutAsJsonAsync($"/api/v1/people/{person.Id}", new { firstName = "Nathan", lastName = "Paxton" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<PersonResponse>(Ct))!;
        Assert.Equal((person.Id, "Nathan", null, "Paxton"), (updated.Id, updated.FirstName, updated.MiddleName, updated.LastName));
    }

    [Fact]
    public async Task Unknown_person_returns_404()
    {
        var client = Editor();
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/people/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/v1/people/{id}", new { firstName = "Amy" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/people/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_person_removes_their_photo_tags()
    {
        var client = Editor();
        var person = await CreateAsync(client, new { firstName = "Greg" });
        Guid photoId = default;
        await WithDbAsync(async db =>
        {
            var photo = await InsertPhotoAsync(db);
            photoId = photo.Id;
            db.PhotoPeople.Add(new PhotoPerson { PhotoId = photo.Id, PersonId = person.Id, TaggedBy = "test" });
            await db.SaveChangesAsync(Ct);
        });

        var response = await client.DeleteAsync($"/api/v1/people/{person.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/people/{person.Id}", Ct)).StatusCode);
        await WithDbAsync(async db =>
        {
            Assert.False(await db.PhotoPeople.AnyAsync(pp => pp.PersonId == person.Id, Ct));
            Assert.True(await db.Photos.AnyAsync(p => p.Id == photoId, Ct), "The photo itself must remain.");
        });
    }

    [Fact]
    public async Task A_camera_owner_cannot_be_deleted()
    {
        var client = Editor();
        var person = await CreateAsync(client, new { firstName = "Travis" });
        await WithDbAsync(db => InsertPhotoAsync(db, cameraOwnerId: person.Id));

        var response = await client.DeleteAsync($"/api/v1/people/{person.Id}", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("1 photo", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/people/{person.Id}", Ct)).StatusCode);
    }

    // Schema rules the tagging work relies on.
    [Fact]
    public async Task Database_enforces_tag_and_camera_owner_rules()
    {
        var person = await factory.CreatePersonAsync("Nancy");

        await WithDbAsync(async db =>
        {
            var photo = await InsertPhotoAsync(db, cameraOwnerId: person.Id);
            db.PhotoPeople.Add(new PhotoPerson { PhotoId = photo.Id, PersonId = person.Id, TaggedBy = "test" });
            await db.SaveChangesAsync(Ct);

            // The same person can't be tagged twice on one photo.
            db.ChangeTracker.Clear();
            db.PhotoPeople.Add(new PhotoPerson { PhotoId = photo.Id, PersonId = person.Id, TaggedBy = "test" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

            // The database itself refuses to delete a camera owner (RESTRICT), even bypassing the API.
            db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                db.People.Where(p => p.Id == person.Id).ExecuteDeleteAsync(Ct));

            // Deleting a photo removes its tags.
            db.ChangeTracker.Clear();
            await db.Photos.Where(p => p.Id == photo.Id).ExecuteDeleteAsync(Ct);
            Assert.False(await db.PhotoPeople.AnyAsync(pp => pp.PhotoId == photo.Id, Ct));
        });
    }
}
