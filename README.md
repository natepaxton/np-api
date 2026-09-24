# np-api

.NET 10 backend API. Aspire for local orchestration, EF Core + Dapper on Postgres (Neon),
Auth0 for authentication.

## Prerequisites

- .NET 10 SDK
- Docker Desktop (Aspire runs Postgres in a container locally)
- Optional: Aspire CLI (`aspire run`), Postman

## Quick start

```sh
dotnet tool restore
dotnet dev-certs https --trust

# One-time: tell the API about your Auth0 tenant (or enter them in the dashboard when prompted)
dotnet user-secrets --project src/NpApi.AppHost set Parameters:auth0-domain <tenant>.us.auth0.com
dotnet user-secrets --project src/NpApi.AppHost set Parameters:auth0-audience <your API identifier>
dotnet user-secrets --project src/NpApi.AppHost set Parameters:cloudinary-api-key <key>
dotnet user-secrets --project src/NpApi.AppHost set Parameters:cloudinary-api-secret <secret>

dotnet run --project src/NpApi.AppHost
```

Run the tests (Docker required) with `dotnet test`. See [docs/testing.md](docs/testing.md).

The console prints a link to the Aspire dashboard (logs, traces, resource health). The API is at
https://localhost:7223:

| Route | Auth | Purpose |
| --- | --- | --- |
| `GET /alive` | anonymous | Liveness |
| `GET /health` | anonymous | Readiness (includes database) |
| `GET /openapi/v1.json` | anonymous, Development only | OpenAPI document for Postman |
| `GET /api/v1/me` | bearer token | Echoes your Auth0 user id and permissions |
| `POST /api/v1/photos` (multipart) | `write:photos` | Upload to Cloudinary; reads EXIF date and GPS |
| `GET /api/v1/photos`, `GET /api/v1/photos/{id}` | `read:photos` | Photos with thumbnail/medium/full URLs |
| `GET /api/v1/people`, `GET /api/v1/people/{id}` | `read:people` | People (photo subjects, camera owners) |
| `POST`/`PUT`/`DELETE /api/v1/people[/{id}]` | `write:people` | Manage people; delete is 409 while they own photos |

## Documentation

- [Architecture and dependency injection](docs/architecture.md)
- [Database: local, Neon, migrations](docs/database.md)
- [Auth0](docs/auth0.md)
- [Auth0 with Terraform](docs/auth0-terraform.md)
- [Health checks](docs/health-checks.md)
- [Deployment](docs/deployment.md)
- [Photos: model, upload, locations](docs/photos.md)
- [People: model, tagging link, camera owners](docs/people.md)
- [Testing with Postman](docs/postman.md)
- [Automated tests, coverage and CI](docs/testing.md)
