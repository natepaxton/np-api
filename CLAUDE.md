# CLAUDE.md

.NET 10 minimal-API backend. Aspire orchestrates local dev; EF Core + Dapper over Postgres (Neon in
the cloud, a container locally); Auth0 JWT bearer auth. Human-facing docs live in `docs/`.

## Commands

```sh
dotnet tool restore                                  # installs dotnet-ef from dotnet-tools.json
dotnet run --project src/NpApi.AppHost               # runs Postgres + API + Aspire dashboard (needs Docker)
dotnet build
dotnet ef migrations add <Name> --project src/NpApi.Api --output-dir Data/Migrations
dotnet ef migrations remove --project src/NpApi.Api  # only for migrations not yet applied anywhere shared

dotnet format                                        # CI fails on formatting drift (.editorconfig)
dotnet test                                          # all tests; needs Docker (Testcontainers Postgres)
dotnet test --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml \
  --coverage-settings coverage.config --results-directory TestResults
dotnet reportgenerator -reports:"TestResults/**/*.cobertura.xml" -targetdir:coverage-report -reporttypes:"JsonSummary;TextSummary;Html"
scripts/check-coverage.sh                            # CI minimum: 80% line, 70% branch
scripts/check-vulnerable-packages.sh                 # CI fails on High/Critical advisories
dotnet ef migrations has-pending-model-changes --project src/NpApi.Api   # CI fails if a migration is missing

scripts/get-dev-token.py                             # Auth0 sign-in (PKCE) → .dev-token for local API calls

cd infra/auth0 && set -a && source .env && set +a && terraform plan     # Auth0 changes; never apply without reviewing the plan
```

The API listens on https://localhost:7223 when run through the AppHost. Migrations are applied
automatically on startup in Development only.

## Layout

- `src/NpApi.AppHost` — Aspire orchestration only (resources, parameters). Not deployed.
- `src/NpApi.ServiceDefaults` — OpenTelemetry, resilience, health-check endpoints (`/health`, `/alive`).
- `infra/auth0/` — Terraform for the Auth0 API, SPA clients, and client grants. Auth0 changes go
  here, not in the dashboard.
- `tests/NpApi.Api.Tests` — xUnit v3 (Microsoft Testing Platform) integration tests; see `docs/testing.md`.
- `.github/workflows/ci.yml` — build/test/coverage gate/migrations check, format, vulnerable packages,
  container build + smoke test, Terraform fmt/validate. Details in `docs/testing.md`.
- `scripts/` — CI helper scripts, also runnable locally.
- `src/NpApi.Api` — the deployable API.
  - `Program.cs` — composition root; should read as a list of `AddX()` / `MapX()` calls.
  - `Data/` — `AppDbContext`, migrations, data DI registration.
  - `Auth/` — Auth0 JWT setup and claim helpers.
  - `Features/<Feature>/` — entity + EF config, Dapper queries, endpoints, and the feature's
    `AddX(IServiceCollection)` / `MapX(IEndpointRouteBuilder)` extensions.

## Conventions

- New feature: create `Features/<Name>/`, expose `Add<Name>()` and `Map<Name>()`, call both from `Program.cs`.
  Feature routes are mapped on the `/api/v1` group (the frontends in `np-web` call `<npApiUrl>/api/v1/...`);
  health endpoints stay at the root.
- CORS: every frontend origin must be in `Cors:AllowedOrigins` (dev: 4300 sandbox, 4301 yellowstone, 4302 roadie).
- Endpoints use `TypedResults` and `Results<...>` return types so OpenAPI is accurate.
- DI: inject `AppDbContext` directly; do not add repository/unit-of-work wrappers around EF. Add an
  interface only for a real seam (external service, multiple implementations). Services that use
  `AppDbContext` must be scoped. Use `TimeProvider` rather than `DateTime.UtcNow`
  in new code that needs testable time.
- Config classes use the options pattern with `ValidateDataAnnotations().ValidateOnStart()`.
- Data access: EF Core for writes and simple reads; Dapper for complex/reporting reads. Dapper
  query classes inject `AppDbContext` and call `db.Database.GetDbConnection()` per query. Never
  register that connection in DI: the container disposes it at the end of the request and breaks the
  pooled `DbContext` for the next one; give the first Dapper query an integration test that alternates
  it with EF writes. Dapper is configured but unused so far. Never write through both in one
  operation without a transaction.
- Timestamps written by the app are truncated to microseconds (Postgres `timestamptz` precision) so
  the value a POST returns matches later reads; use `Timestamps.UtcNow()`.
- Database naming is snake_case (EFCore.NamingConventions). Raw SQL uses snake_case: `uploaded_at`, `camera_owner`.
- Dapper row types are records with `init` properties, not positional records — constructor mapping
  fails on snake_case columns and on `timestamptz` (read as `DateTime`).
- Manual transactions must be wrapped in `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`
  because the Aspire EF integration enables retry-on-failure.
- Permissions: `Auth/Permissions.cs` mirrors the Auth0 API permissions (`infra/auth0` `api_permissions`);
  each is a policy of the same name — `.RequireAuthorization(Permissions.ReadPhotos)`. Add new ones
  in both places.
- External services go behind an interface (e.g. `IPhotoStorage` for Cloudinary) so tests use a fake
  (`FakePhotoStorage`, swapped per test with `ApiFactory.WithStorage`).
- Auth is on by default (fallback policy requires an authenticated user). Anonymous endpoints must
  call `.AllowAnonymous()` explicitly. Get the caller's id with `User.GetUserId()` (Auth0 `sub`).
  Scope data to the owner in the query, not after loading.
- Never put connection strings or secrets in `appsettings*.json`. Local: user secrets on the
  AppHost. Deployed: environment variables (`ConnectionStrings__npdb`, `Auth0__Domain`, `Auth0__Audience`,
  `Cloudinary__ApiKey`, `Cloudinary__ApiSecret`).
- Photo capture times come from EXIF offset or GPS UTC; never interpret EXIF local time in the
  server's time zone. Delivery URLs are derived from the Cloudinary public ID, not stored.
- Never edit a migration that has been applied to Neon; add a new one.
- `/health` checks the database; `/alive` does not. Platform liveness probes must use `/alive` so
  they don't keep Neon's scale-to-zero compute awake.

## Testing

- Every endpoint change gets an integration test in `tests/NpApi.Api.Tests` through `ApiFactory`
  (real Postgres via Testcontainers, tokens from `TestAuth`). Use `TestAuth.NewUserId()` per test so
  tests stay isolated without cleanup.
- Tests must pass on Linux (CI) — don't rely on macOS behavior such as its clock resolution.
- Entity/configuration changes need a migration in the same change; CI checks for missing ones.

## Docs

- `docs/architecture.md` — project structure, DI guidance, EF vs Dapper
- `docs/database.md` — local vs Neon, dev/prod switching, migrations in CI
- `docs/auth0.md` — how the API validates tokens, permissions, frontend token requirements
- `docs/auth0-terraform.md` — SPA ↔ API wiring checklist, Terraform bootstrap/import/workflow
- `docs/health-checks.md` — endpoints and probe configuration
- `docs/deployment.md` — Cloud Run (recommended) and alternatives
- `docs/photos.md` — Photo model, id strategy, upload flow, EXIF dates, finding locations
- `docs/postman.md` — local testing with Postman
- `docs/testing.md` — test approach, coverage, CI, GitHub settings
