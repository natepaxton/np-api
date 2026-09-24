using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Database selection:
//  - Default (local dev): a Postgres container managed by Aspire, data kept in a Docker volume.
//  - Database:UseExternal=true (or publish mode): use the "npdb" connection string from config,
//    e.g. a Neon dev branch stored in this project's user secrets. See docs/database.md.
var useExternalDb = builder.Configuration.GetValue<bool>("Database:UseExternal");

IResourceBuilder<IResourceWithConnectionString> db = builder.ExecutionContext.IsPublishMode || useExternalDb
    ? builder.AddConnectionString("npdb")
    : builder.AddPostgres("postgres")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .AddDatabase("npdb");

// Auth0 settings are prompted for in the dashboard if missing. Set them once with:
//   dotnet user-secrets --project src/NpApi.AppHost set Parameters:auth0-domain <tenant>.us.auth0.com
//   dotnet user-secrets --project src/NpApi.AppHost set Parameters:auth0-audience https://api.example.com
var auth0Domain = builder.AddParameter("auth0-domain");
var auth0Audience = builder.AddParameter("auth0-audience");

// Cloudinary API credentials (Settings > API Keys in the Cloudinary console). Secret parameters are
// masked in the dashboard. Set them once with:
//   dotnet user-secrets --project src/NpApi.AppHost set Parameters:cloudinary-api-key <key>
//   dotnet user-secrets --project src/NpApi.AppHost set Parameters:cloudinary-api-secret <secret>
var cloudinaryApiKey = builder.AddParameter("cloudinary-api-key", secret: true);
var cloudinaryApiSecret = builder.AddParameter("cloudinary-api-secret", secret: true);

var api = builder.AddProject<Projects.NpApi_Api>("api")
    .WithReference(db)
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience)
    .WithEnvironment("Cloudinary__ApiKey", cloudinaryApiKey)
    .WithEnvironment("Cloudinary__ApiSecret", cloudinaryApiSecret)
    .WithHttpHealthCheck("/health");

if (db.Resource is PostgresDatabaseResource)
{
    api.WaitFor(db);
}

builder.Build().Run();
