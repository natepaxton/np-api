# Testing

## Approach

`tests/NpApi.Api.Tests` is an xUnit v3 project running on Microsoft Testing Platform (selected in
`global.json`). Most tests are **integration tests**: they start the real API in memory with
`WebApplicationFactory` against a real Postgres started by Testcontainers. That catches what mocks
can't, like Dapper column mapping, EF connection lifetimes, CORS, and auth middleware ordering.
Pure logic (for example `Auth0Options.Authority`) gets plain unit tests.

| Piece | What it does |
| --- | --- |
| `Infrastructure/ApiFactory` | One API + one `postgres:18` container for the whole test assembly (xUnit assembly fixture). Runs in Development, so migrations apply on startup like they do locally. |
| `Infrastructure/TestAuth` | Issues Auth0-shaped JWTs (`sub`, `permissions`, `scope`) signed with a per-run key. `ApiFactory` points JWT validation at this key instead of Auth0, so tests need no network or secrets. |
| `TestAuth.NewUserId()` | A unique `sub` per test, so tests share the database without seeing each other's rows or needing cleanup. |

Requirements: Docker running locally. The GitHub-hosted Ubuntu runner already has it.

## Running

```sh
dotnet test

# with coverage (Microsoft.Testing.Extensions.CodeCoverage, settings in coverage.config)
dotnet test --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml \
  --coverage-settings coverage.config --results-directory TestResults
dotnet reportgenerator -reports:"TestResults/**/*.cobertura.xml" -targetdir:coverage-report -reporttypes:"TextSummary;Html"
open coverage-report/index.html
```

`coverage.config` excludes EF migrations, source-generator output under `obj/`, and anything marked
`[ExcludeFromCodeCoverage]` (only the design-time `DbContext` factory used by `dotnet ef`).

## CI

`.github/workflows/ci.yml` runs on every push to `main` and every pull request:

| Job | Steps |
| --- | --- |
| Build and test | restore tools → `dotnet build -c Release` → tests with coverage → coverage summary on the run page → HTML report uploaded as the `coverage-report` artifact |
| Terraform checks (Auth0) | `terraform fmt -check`, `terraform init -backend=false`, `terraform validate`. Never contacts the Auth0 tenant. |

### GitHub settings

None needed for CI. It uses no secrets or variables. Later additions will need:

| When | Setting | Kind |
| --- | --- | --- |
| Terraform plan/apply in CI | `AUTH0_DOMAIN` | Variable |
| | `AUTH0_CLIENT_ID`, `AUTH0_CLIENT_SECRET` | Secrets |
| | A remote state backend first (state is local today) | — |
| Migrations on deploy | `NEON_DIRECT_CONNECTION_STRING` per GitHub environment | Secret |
| Deploy | Host-specific credentials (see [deployment.md](deployment.md)) | Secret |

Recommended once code is on GitHub: protect `main` and require the **Build and test** and
**Terraform checks (Auth0)** checks to pass before merging.
