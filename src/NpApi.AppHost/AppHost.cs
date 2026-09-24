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

var api = builder.AddProject<Projects.NpApi_Api>("api")
    .WithReference(db)
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience)
    .WithHttpHealthCheck("/health");

if (db.Resource is PostgresDatabaseResource)
{
    api.WaitFor(db);
}

builder.Build().Run();
