using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NpApi.Api.Auth;
using NpApi.Api.Data;
using NpApi.Api.Features.Photos;
using NpApi.Api.Features.Places;
using NpApi.Api.Tests.Infrastructure;

namespace NpApi.Api.Tests.Features.Places;

public class PlacesEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Editor() => factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPlaces, Permissions.WritePlaces);

    private async Task<PlaceResponse> CreateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/places", request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlaceResponse>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task Requires_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/places", Ct)).StatusCode);
    }

    [Fact]
    public async Task Reading_requires_read_places()
    {
        var other = factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPhotos, Permissions.ReadPeople);

        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/v1/places", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/v1/places/{Guid.CreateVersion7()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Changing_places_requires_write_places()
    {
        var reader = factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPlaces);
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/v1/places", new { city = "Cody" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"/api/v1/places/{id}", new { city = "Cody" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"/api/v1/places/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_trims_names_stores_blanks_as_null_and_can_be_fetched()
    {
        var client = Editor();

        var response = await client.PostAsJsonAsync("/api/v1/places",
            new { city = " West Yellowstone ", stateProvince = "   ", country = "us", lat = 44.6621, lng = -111.1041 }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var place = (await response.Content.ReadFromJsonAsync<PlaceResponse>(TestJson.Options, Ct))!;
        Assert.Equal(("West Yellowstone", null, CountryCode.US, "United States", 44.6621, -111.1041, "West Yellowstone, US"),
            (place.City, place.StateProvince, place.Country, place.CountryName, place.Lat, place.Lng, place.DisplayName));

        Assert.Equal(place, await client.GetFromJsonAsync<PlaceResponse>(response.Headers.Location, TestJson.Options, Ct));
    }

    [Fact]
    public async Task A_place_can_be_just_a_country_or_just_a_point()
    {
        var client = Editor();

        var country = await CreateAsync(client, new { country = "CA" });
        var point = await CreateAsync(client, new { lat = 44.4605, lng = -110.8281 });

        Assert.Equal(("CA", "Canada"), (country.DisplayName, country.CountryName));
        Assert.Equal("44.4605, -110.8281", point.DisplayName);
    }

    public static TheoryData<string, object> InvalidPlaces => new()
    {
        { "", new { } },
        { "", new { city = "  ", country = "" } },
        { "Lat", new { city = "Cody", lat = 44.5 } },
        { "Lat", new { city = "Cody", lat = 91, lng = 0 } },
        { "Lat", new { city = "Cody", lat = 0, lng = 181 } },
        { "City", new { city = new string('x', Place.MaxNameLength + 1) } },
        { "StateProvince", new { city = "Cody", stateProvince = new string('x', Place.MaxNameLength + 1) } },
        { "Country", new { city = "Cody", country = "USA" } },
        { "Country", new { city = "Cody", country = "ZZ" } },
        { "Country", new { city = "Cody", country = "1" } },
        { "Country", new { country = "United States" } },
    };

    [Theory]
    [MemberData(nameof(InvalidPlaces))]
    public async Task Invalid_places_return_validation_problem(string field, object request)
    {
        var client = Editor();
        var existing = await CreateAsync(client, new { city = "Cody" });

        foreach (var response in new[]
                 {
                     await client.PostAsJsonAsync("/api/v1/places", request, Ct),
                     await client.PutAsJsonAsync($"/api/v1/places/{existing.Id}", request, Ct),
                 })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Assert.True(errors.TryGetProperty(field, out _), $"Expected an error for '{field}': {errors}");
        }
    }

    [Fact]
    public async Task List_is_sorted_by_country_then_state_then_city()
    {
        var client = Editor();
        var tag = Guid.NewGuid().ToString("N");
        var usWy = await CreateAsync(client, new { city = $"Cody {tag}", stateProvince = "Wyoming", country = "US" });
        var usMtB = await CreateAsync(client, new { city = $"Gardiner {tag}", stateProvince = "Montana", country = "US" });
        var usMtA = await CreateAsync(client, new { city = $"Bozeman {tag}", stateProvince = "Montana", country = "US" });
        var mx = await CreateAsync(client, new { city = $"Tijuana {tag}", stateProvince = "Baja California", country = "MX" });
        var ca = await CreateAsync(client, new { city = $"Banff {tag}", stateProvince = "Alberta", country = "CA" });

        var places = await client.GetFromJsonAsync<PlaceResponse[]>("/api/v1/places", TestJson.Options, Ct);

        Assert.Equal([ca.Id, mx.Id, usMtA.Id, usMtB.Id, usWy.Id],
            places!.Where(p => p.City?.EndsWith(tag) == true).Select(p => p.Id));
    }

    [Fact]
    public async Task Update_replaces_fields_and_can_clear_the_point()
    {
        var client = Editor();
        var place = await CreateAsync(client, new { city = "Gardner", country = "US", lat = 45.03, lng = -110.70 });

        var response = await client.PutAsJsonAsync($"/api/v1/places/{place.Id}",
            new { city = "Gardiner", stateProvince = "Montana", country = "US" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<PlaceResponse>(TestJson.Options, Ct))!;
        Assert.Equal((place.Id, "Gardiner", "Montana", null, null), (updated.Id, updated.City, updated.StateProvince, updated.Lat, updated.Lng));
    }

    [Fact]
    public async Task Unknown_place_returns_404()
    {
        var client = Editor();
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/places/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/v1/places/{id}", new { city = "Cody" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/places/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_place_unlinks_its_photos()
    {
        var client = Editor();
        var place = await factory.CreatePlaceAsync();
        var photoId = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Photos.Add(new Photo
            {
                Id = photoId,
                CloudinaryPublicId = $"np-api/test/{photoId}",
                Url = "https://example.test/photo.jpg",
                Filename = "photo.jpg",
                UploadedBy = TestAuth.NewUserId(),
                PlaceId = place.Id,
            });
            await db.SaveChangesAsync(Ct);
        }

        var response = await client.DeleteAsync($"/api/v1/places/{place.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var photo = await db.Photos.AsNoTracking().SingleAsync(p => p.Id == photoId, Ct);
            Assert.Null(photo.PlaceId);
        }
    }

    [Fact]
    public async Task Countries_lists_the_supported_codes_by_name()
    {
        var reader = factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPlaces);

        var countries = await reader.GetFromJsonAsync<CountryResponse[]>("/api/v1/countries", TestJson.Options, Ct);

        Assert.Equal(
            [new(CountryCode.CA, "Canada"), new(CountryCode.MX, "Mexico"), new CountryResponse(CountryCode.US, "United States")],
            countries!);
    }

    [Fact]
    public async Task Countries_requires_read_places()
    {
        var other = factory.CreateClientFor(TestAuth.NewUserId(), Permissions.ReadPhotos);

        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/v1/countries", Ct)).StatusCode);
    }

    // The enum and the seeded table must list exactly the same codes, and the database must refuse
    // any other code even when the API is bypassed.
    [Fact]
    public async Task Countries_table_matches_the_enum_and_is_enforced()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var seeded = await db.Countries.AsNoTracking().ToListAsync(Ct);
        Assert.Equal(Enum.GetValues<CountryCode>().Order(), seeded.Select(c => c.Code).Order());
        Assert.All(seeded, c => Assert.Equal(Country.Names[c.Code], c.Name));

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "insert into places (id, city, country_code, created_at) values (gen_random_uuid(), 'Nowhere', 'ZZ', now())", Ct));
    }

    [Fact]
    public async Task Database_rejects_an_empty_place()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Places.Add(new Place());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }
}
