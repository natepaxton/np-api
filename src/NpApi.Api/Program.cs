using NpApi.Api.Auth;
using NpApi.Api.Data;
using NpApi.Api.Features.Me;
using NpApi.Api.Features.Notes;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddData();
builder.AddAuth0();

builder.Services.AddProblemDetails();

// Browser frontends (SPAs) calling this API need their origins listed here, e.g.
// Cors__AllowedOrigins__0=https://app.example.com
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddOpenApi();

builder.Services.AddNotes();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Import http://localhost:<port>/openapi/v1.json into Postman to generate a collection.
    app.MapOpenApi().AllowAnonymous();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Health endpoints stay at the root for platform probes; everything else is versioned.
app.MapDefaultEndpoints();

var api = app.MapGroup("/api/v1");
api.MapMe();
api.MapNotes();

await app.ApplyMigrationsInDevelopmentAsync();

app.Run();
