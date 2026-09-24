# Architecture

## Projects

| Project | Role | Deployed? |
| --- | --- | --- |
| `NpApi.AppHost` | Aspire orchestrator. Starts Postgres, wires connection strings and parameters into the API, hosts the dashboard. | No |
| `NpApi.ServiceDefaults` | Shared plumbing: OpenTelemetry (logs/traces/metrics), HttpClient resilience, service discovery, health endpoints. | Compiled into the API |
| `NpApi.Api` | The HTTP API. | Yes, as a container |

Aspire is a development-time tool here. In production the API container runs on its own and gets
the same settings (`ConnectionStrings__npdb`, `Auth0__*`) from environment variables that the
AppHost injects locally. Nothing in the API depends on Aspire being present.

## Folder layout inside the API

Organize by feature, not by technical layer:

```
src/NpApi.Api/
  Program.cs              composition root
  Auth/                   Auth0 options + JWT setup + claim helpers
  Data/                   AppDbContext, migrations, data registration
  Features/
    Photos/
      Photo.cs                  entity + LocationSource + IEntityTypeConfiguration
      PhotoEndpoints.cs         AddPhotos() + MapPhotos()
      PhotoMetadataReader.cs    EXIF capture time and GPS
      Storage/                  IPhotoStorage + Cloudinary implementation
    Me/
```

Everything a feature needs lives in its folder. Deleting a feature means deleting a folder and two
lines in `Program.cs`.

## Dependency injection

The built-in container (`Microsoft.Extensions.DependencyInjection`) is enough; you don't need
Autofac or similar.

**1. Keep `Program.cs` a table of contents.** Each area exposes an extension method:

```csharp
builder.AddData();            // IHostApplicationBuilder extensions when config is needed
builder.AddAuth0();
builder.Services.AddNotes();  // IServiceCollection extensions for plain registrations
...
app.MapNotes();
```

**2. Lifetimes.**

| Lifetime | Use for |
| --- | --- |
| Scoped (per request) | `AppDbContext` and anything that depends on it (e.g. a Dapper query class) |
| Singleton | Stateless services, caches, `TimeProvider.System`, typed options |
| Transient | Rarely needed; lightweight stateless objects with no shared state |

Never inject a scoped service into a singleton. ASP.NET Core validates this in Development
(`ValidateScopes`) and throws at startup if you do.

**3. Don't wrap EF in a generic repository.** `DbContext` is already a unit of work and each
`DbSet<T>` is already a repository. A `IRepository<T>` layer hides EF features (projection,
`AsNoTracking`, `Include`, `ExecuteUpdateAsync`) and adds code to maintain. Inject `AppDbContext`
into endpoints or feature services directly.

**4. Add interfaces at real seams only.** Good candidates: external systems (email, payments, the
Auth0 Management API), or when you genuinely have multiple implementations (use keyed services:
`AddKeyedScoped<IStorage, S3Storage>("s3")`). Concrete classes (a Dapper query class, say) don't need an
interface; tests can hit a real Postgres via Aspire's testing package or Testcontainers.

**5. Options pattern for configuration**, validated at startup so a missing setting fails fast
instead of on the first request:

```csharp
builder.Services.AddOptions<Auth0Options>()
    .BindConfiguration("Auth0")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**6. Outbound HTTP via typed clients.** `builder.Services.AddHttpClient<WeatherClient>(...)` picks
up retries/timeouts/circuit breaker from ServiceDefaults automatically.

**7. Time.** Inject `TimeProvider` (register `TimeProvider.System` as a singleton when first needed)
instead of calling `DateTime.UtcNow`, so tests can control time.

## EF Core and Dapper

### What Dapper is

Dapper is a "micro-ORM": a set of extension methods on `IDbConnection` (`QueryAsync<T>`,
`ExecuteAsync`, `QuerySingleOrDefaultAsync<T>`, ...) that run SQL **you write** and map the result
columns onto C# objects. It does not track changes, generate SQL, or manage schema/migrations. In
exchange it is thin, fast, and lets you use any Postgres feature (CTEs, window functions,
`jsonb` operators, full-text search) without fighting a LINQ translator.

```csharp
// How many photos carry each tag, for one camera owner. Easy in SQL, awkward in LINQ.
var counts = await connection.QueryAsync<TagCount>(
    """
    select tag, count(*) as photo_count
    from photos, unnest(tags) as tag
    where camera_owner = @owner
    group by tag
    order by photo_count desc
    """,
    new { owner });   // parameters are always sent as real parameters, never concatenated
```

### How they're combined here

- **EF Core owns the schema** (entities, configurations, migrations) and handles writes and simple reads.
- **Dapper handles read queries** that are complex, performance-sensitive, or easier to express in SQL.
- Dapper query classes inject `AppDbContext` and call `db.Database.GetDbConnection()` for each query.
  EF owns that connection (it opens, closes and disposes it), and Dapper borrows it. So a Dapper
  query inside an EF transaction sees uncommitted EF changes. Pass the transaction explicitly
  (`transaction: db.Database.CurrentTransaction?.GetDbTransaction()`) when you do this.
- **Don't register EF's connection in DI** (for example `AddScoped<IDbConnection>(sp => ...GetDbConnection())`).
  The container disposes whatever a factory returns at the end of the request. The `DbContext` then
  goes back to EF's pool holding a disposed connection, and the next request that gets it fails with
  `ObjectDisposedException`. It only shows up once a pooled context is reused, so give the first
  Dapper query an integration test that alternates it with an EF write over several requests.
- Dapper is installed and configured but not used yet. Add the first query class when there's a read
  that's clearer in SQL (e.g. tag counts or facets for filtering).

### Rules of thumb

- Tables and columns are snake_case (`UseSnakeCaseNamingConvention()`), so SQL needs no quoting.
  Dapper maps `uploaded_at` → `UploadedAt` because `DefaultTypeMap.MatchNamesWithUnderscores = true`.
- Dapper result types are records with `init` properties. Positional records fail: Dapper's
  constructor mapping needs exact column names **and** types, and Npgsql returns `timestamptz` as
  `DateTime`, not `DateTimeOffset`.
- Always pass the `CancellationToken` via `CommandDefinition`.
- When you need an explicit transaction, wrap it in the execution strategy because the Aspire EF
  integration enables retry-on-failure:

  ```csharp
  var strategy = db.Database.CreateExecutionStrategy();
  await strategy.ExecuteAsync(async () =>
  {
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      // EF and/or Dapper work
      await tx.CommitAsync(ct);
  });
  ```
