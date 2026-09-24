using System.Text.Json.Serialization;
using NpApi.Api.Auth;
using NpApi.Api.Data;
using NpApi.Api.Features.Me;
using NpApi.Api.Features.People;
using NpApi.Api.Features.Photos;
using NpApi.Api.Features.Places;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddData();
builder.AddAuth0();
builder.AddPhotos();

builder.Services.AddProblemDetails();

// Enums as strings in JSON ("Exif", not 0).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Browser frontends (SPAs) calling this API need their origins listed here, e.g.
// Cors__AllowedOrigins__0=https://app.example.com
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Malformed requests (unreadable form or JSON body, too large, bad route/form values) surface as
    // BadHttpRequestException carrying 400/413. Keep that status instead of turning it into a 500.
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});
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
api.MapPhotos();
api.MapPeople();
api.MapPlaces();

await app.ApplyMigrationsInDevelopmentAsync();

app.Run();
