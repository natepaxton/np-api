using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(NpApi.Api.Tests.Infrastructure.ApiFactory))]

namespace NpApi.Api.Tests.Infrastructure;

// One API instance and one Postgres container shared by every test in the assembly. Runs in
// Development, so migrations are applied on startup exactly as they are locally.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18").Build();

    public string ConnectionString => _database.GetConnectionString();

    public async ValueTask InitializeAsync() => await _database.StartAsync();

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

        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Validate against the test issuer and key instead of downloading Auth0's metadata.
                var configuration = new OpenIdConnectConfiguration { Issuer = TestAuth.Issuer };
                configuration.SigningKeys.Add(TestAuth.SigningKey);

                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));
    }
}
