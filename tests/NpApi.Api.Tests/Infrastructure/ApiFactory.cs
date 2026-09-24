using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NpApi.Api.Data;
using NpApi.Api.Features.People;
using NpApi.Api.Features.Photos.Storage;
using NpApi.Api.Features.Places;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(NpApi.Api.Tests.Infrastructure.ApiFactory))]

namespace NpApi.Api.Tests.Infrastructure;

// One API instance and one Postgres container shared by every test in the assembly. Runs in
// Development, so migrations are applied on startup exactly as they are locally.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18").Build();

    public const string CloudName = "test-cloud";

    public string ConnectionString => _database.GetConnectionString();

    // Replaces Cloudinary for the shared app instance. Tests needing failures use WithStorage().
    public FakePhotoStorage Storage { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();

        // Start the app now so its startup migrations finish before any test runs. Otherwise extra
        // instances from WithStorage() can race it and both try to create the same tables.
        using var _ = CreateClient();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    public HttpClient CreateClientFor(string userId, params string[] permissions)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.CreateToken(userId, permissions));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:npdb", ConnectionString);
        builder.UseSetting("Auth0:Domain", TestAuth.Domain);
        builder.UseSetting("Auth0:Audience", TestAuth.Audience);
        builder.UseSetting("Cloudinary:CloudName", CloudName);
        builder.UseSetting("Cloudinary:ApiKey", "test-key");
        builder.UseSetting("Cloudinary:ApiSecret", "test-secret");

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Validate against the test issuer and key instead of downloading Auth0's metadata.
                var configuration = new OpenIdConnectConfiguration { Issuer = TestAuth.Issuer };
                configuration.SigningKeys.Add(TestAuth.SigningKey);

                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            services.AddSingleton<IPhotoStorage>(Storage);
        });
    }

    // A second app instance (same database) whose photo storage is the given fake.
    public WebApplicationFactory<Program> WithStorage(FakePhotoStorage storage) =>
        WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IPhotoStorage>(storage)));

    // Inserts a person straight into the database (for tests that need one to exist).
    public async Task<Person> CreatePersonAsync(string firstName = "Laura", string? lastName = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var person = new Person { FirstName = firstName, LastName = lastName };
        db.People.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    public async Task<Place> CreatePlaceAsync(string? city = "Gardiner", string? stateProvince = "Montana",
        CountryCode? countryCode = CountryCode.US, double? latitude = 45.0319, double? longitude = -110.7057)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var place = new Place { City = city, StateProvince = stateProvince, CountryCode = countryCode };
        place.SetPoint(latitude, longitude);
        db.Places.Add(place);
        await db.SaveChangesAsync();
        return place;
    }

    public static HttpClient Authorize(HttpClient client, string userId, params string[] permissions)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.CreateToken(userId, permissions));
        return client;
    }
}
