# Health checks

Defined in `src/NpApi.ServiceDefaults/Extensions.cs`, mapped in every environment, anonymous.

| Endpoint | Checks | Meaning | Use it for |
| --- | --- | --- | --- |
| `GET /alive` | only checks tagged `live` (`self`) | The process is up and serving requests | Platform **liveness** probes, uptime monitors |
| `GET /health` | all checks, including `AppDbContext` (database) | The API can do real work | Aspire dashboard, **startup/readiness** probes, manual checks, post-deploy smoke tests |

Both return JSON with overall status, each check's status, and durations. HTTP 200 means healthy
or degraded, and 503 means unhealthy. Error descriptions are included only in Development.

Every check has a 5-second timeout (set in `AddDefaultHealthChecks`). Without it, the database
check sits through EF's retry-on-failure policy and takes about a minute to report an outage.

The database check is registered automatically by the Aspire EF integration (`AddNpgsqlDbContext`).
The AppHost's `WithHttpHealthCheck("/health")` makes the dashboard show the API as unhealthy when
the database is unreachable.

## Why two endpoints

A liveness probe that fails makes the platform **restart** the container. If liveness included the
database, a Neon hiccup would restart every instance, which doesn't fix anything. So:

- **Liveness → `/alive`** (no external dependencies).
- **Readiness/startup → `/health`**, called once at startup or rarely.

**Neon-specific:** don't point a frequent (every few seconds) probe or external uptime monitor at
`/health`. Each call runs a database query, which keeps Neon's compute from suspending and burns
free-tier compute hours. Monitor `/alive` for uptime, and check `/health` after deployments.

## Adding checks

Add checks where a dependency failing means the API genuinely can't serve requests:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<StorageHealthCheck>("storage");          // readiness only
    // .AddCheck("x", ..., tags: ["live"])             // only if it's about the process itself
```

Don't add a check for Auth0. The JWKS keys are cached, and marking the API unhealthy during an
Auth0 outage wouldn't help anyone.

## Security

Responses expose check names and timings, not connection strings or exceptions (outside
Development). If you add checks that reveal sensitive information, restrict `/health` (for example
`.RequireHost(...)` or an internal-only port).
