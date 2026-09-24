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

`.github/workflows/ci.yml` runs on every push to `main` and every pull request. It also runs weekly
on `main` (Mondays 13:00 UTC) so newly published vulnerabilities surface without a code change,
and you can start it by hand from the Actions tab. The jobs run in parallel. Each is a separate
check you can require in branch protection.

| Check | What fails it | Fix locally |
| --- | --- | --- |
| **Build and test** | Compile error. **A model change without a migration** (`dotnet ef migrations has-pending-model-changes`). A failing test. **Coverage below the minimum.** | `dotnet ef migrations add <Name> --project src/NpApi.Api --output-dir Data/Migrations`; `dotnet test` |
| **Format** | Code that doesn't match `.editorconfig` (`dotnet format --verify-no-changes`) | `dotnet format` |
| **Vulnerable packages** | A direct or transitive NuGet package with a **High or Critical** advisory. Low/Moderate only warn. | `scripts/check-vulnerable-packages.sh`, then update the package |
| **Container image** | The production image doesn't build (`dotnet publish -t:PublishContainer`, linux-x64), or doesn't start and serve `/alive` = 200 and `/api/v1/me` = 401 | `scripts/smoke-test-image.sh` after publishing locally |
| **Terraform checks (Auth0)** | Unformatted `.tf`, provider/lock mismatch, invalid configuration. Doesn't read `terraform.tfvars` or contact Auth0. | `terraform fmt` in `infra/auth0` |

### Coverage minimum

**80% line and 70% branch** coverage, measured on the ReportGenerator summary (the same numbers
shown on the run page), after excluding migrations and generated code (`coverage.config`). Today
it's about 99% line and 86% branch.

This follows common practice: 80% is the most widely used line-coverage bar, and branch coverage
naturally runs lower. The floor sits well below current coverage on purpose. It catches untested
features or large deletions of tests without failing a PR over a few lines. If coverage stays
high, raise the minimums (`MIN_LINE_COVERAGE` / `MIN_BRANCH_COVERAGE` defaults in
`scripts/check-coverage.sh`) instead of chasing 100%.

### Formatting

`.editorconfig` enforces layout and whitespace only (indentation, braces, newlines, `using`
order). Code-style preferences are suggestions, so they show in the IDE but don't fail CI. Two ways
to change this later:

- **Too strict:** relax or remove the specific rule in `.editorconfig`, or delete the `format` job
  from the workflow (and remove it from required checks).
- **Enforce more:** raise individual rules to `warning` in `.editorconfig`.

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
