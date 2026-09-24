# Deployment

The deployable unit is the `NpApi.Api` container image. The AppHost is not deployed. Production
config comes from environment variables:

| Env var | Value |
| --- | --- |
| `ConnectionStrings__npdb` | Neon **pooled** connection string (from a secret) |
| `Auth0__Domain` | `your-tenant.us.auth0.com` |
| `Auth0__Audience` | API identifier |
| `Cors__AllowedOrigins__0`, `__1`, ... | Frontend origins |

`ASPNETCORE_ENVIRONMENT` defaults to `Production` in the container. OpenAPI is off, migrations are
not applied on startup, and health responses omit error details.

## Building the image (no Dockerfile needed)

The .NET SDK builds OCI images directly:

```sh
dotnet publish src/NpApi.Api -c Release -t:PublishContainer \
  -p ContainerRepository=np-api -p ContainerRuntimeIdentifier=linux-x64
```

The image is based on `mcr.microsoft.com/dotnet/aspnet:10.0`, runs as a non-root user, and listens
on port **8080**. On an Apple Silicon Mac, `ContainerRuntimeIdentifier=linux-x64` matters. Without
it you get an arm64 image that most hosts won't run. CI on `ubuntu-latest` builds x64 by default.

## Recommendation: Google Cloud Run

Why it fits:

- **Free tier** that covers a hobby/early-stage API. You pay per request, it scales to zero, and the
  free tier renews monthly. You need a billing account on file.
- **HTTPS automatically**: every service gets `https://<service>-<hash>.<region>.run.app` with a
  managed certificate. Custom domains get managed certificates too.
- Runs any container. Cold starts are a few seconds, much shorter than platforms that put free
  apps to sleep.
- Secrets via Secret Manager, injected as env vars.

Pick the GCP region closest to your Neon region (for example Neon `aws-us-east-2` → GCP `us-east5`
or `us-east1`). Every query crosses between the two, so distance adds latency.

### One-time setup

```sh
gcloud services enable run.googleapis.com artifactregistry.googleapis.com secretmanager.googleapis.com
gcloud artifacts repositories create np-api --repository-format=docker --location=us-east1

# Runtime connection string (pooled)
printf '%s' 'Host=ep-...-pooler...;Database=neondb;Username=...;Password=...;SSL Mode=Require' \
  | gcloud secrets create neon-prod-pooled --data-file=-
# Grant the Cloud Run runtime service account roles/secretmanager.secretAccessor on the secret.
```

### Deploy

```sh
IMAGE=us-east1-docker.pkg.dev/<project>/np-api/np-api
dotnet publish src/NpApi.Api -c Release -t:PublishContainer \
  -p ContainerRegistry=us-east1-docker.pkg.dev \
  -p ContainerRepository=<project>/np-api/np-api \
  -p ContainerImageTag=$GIT_SHA -p ContainerRuntimeIdentifier=linux-x64

gcloud run deploy np-api --image $IMAGE:$GIT_SHA --region us-east1 \
  --allow-unauthenticated \
  --set-secrets ConnectionStrings__npdb=neon-prod-pooled:latest \
  --set-env-vars Auth0__Domain=<tenant>.us.auth0.com,Auth0__Audience=<audience> \
  --min-instances 0 --max-instances 2
```

`--allow-unauthenticated` only means Google doesn't put its own IAM check in front of the service.
The API still enforces Auth0 tokens on every non-anonymous route. `--max-instances` caps cost if
something goes wrong.

Probes: set the liveness probe to HTTP `/alive`. Leave the startup probe on the default TCP check,
or point it at `/health` if you want startup to verify the database. See
[health-checks.md](health-checks.md) for why liveness must not use `/health`.

## Pipeline sketch (GitHub Actions)

Two GitHub **environments**, `staging` and `production`, hold the same secret names with different
values (see [database.md](database.md)). Deploy `main` to staging automatically. Require approval
for production.

```yaml
jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: production          # selects which secrets are used
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet tool restore

      # 1. Migrate (direct endpoint) before the new code goes live
      - run: dotnet ef migrations bundle --project src/NpApi.Api -o efbundle --self-contained -r linux-x64
      - run: ./efbundle --connection "${{ secrets.NEON_DIRECT_CONNECTION_STRING }}"

      # 2. Build + push image, 3. deploy
      - uses: google-github-actions/auth@v2
        with: { workload_identity_provider: ..., service_account: ... }
      - uses: google-github-actions/setup-gcloud@v2
      - run: gcloud auth configure-docker us-east1-docker.pkg.dev --quiet
      - run: dotnet publish src/NpApi.Api -c Release -t:PublishContainer -p ContainerRegistry=... -p ContainerImageTag=${{ github.sha }}
      - run: gcloud run deploy np-api --image ...:${{ github.sha }} --region us-east1 ...

      # 4. Smoke test
      - run: curl --fail --retry 5 https://<service-url>/health
```

Use Workload Identity Federation for GCP auth instead of a JSON key stored as a secret.

## No credit card: Render

Render is the one host checked (September 2026) that runs a .NET container for free **without a
card on file**. Its free web services include HTTPS on `*.onrender.com` and on custom domains.

| | Render free web service |
| --- | --- |
| Card required | No |
| Hours | 750 instance hours per workspace per month, enough for one service running all the time |
| Sleep | Stops after 15 minutes with no inbound traffic. The next request waits about a minute for it to start, plus .NET startup and a possible Neon wake-up |
| Deploy | From a Dockerfile in the repo, or a prebuilt image (e.g. from GHCR) triggered by a deploy hook |
| Not included | SMTP ports. Pre-deploy commands aren't available on free, so migrations stay in GitHub Actions |

How np-api would run there:

- **Image:** CI builds with `dotnet publish -t:PublishContainer`, pushes to GitHub Container
  Registry using the built-in `GITHUB_TOKEN`, then calls Render's deploy hook (the hook URL is a
  GitHub secret, `RENDER_DEPLOY_HOOK_URL`). Or add a Dockerfile and let Render build from the repo.
- **Port:** the .NET image listens on 8080, so set `PORT=8080` on the service.
- **Environment:** `ConnectionStrings__npdb` (Neon pooled, as a Render secret), `Auth0__Domain`,
  `Auth0__Audience`, `Cors__AllowedOrigins__0..n` (the production frontend origins).
- **Health check path:** `/alive`.
- **Region:** pick the one closest to the Neon project (for example Render Ohio for Neon `aws-us-east-2`).
- **Cold starts:** the SPAs should show a loading state on the first call. To avoid sleeping, an
  external uptime monitor can request `/alive` every 10–14 minutes. That fits inside 750 hours for
  one service. Never ping `/health`, which would keep Neon awake too.

## Alternatives checked

| Platform | Card? | Why not the pick |
| --- | --- | --- |
| **Google Cloud Run** | Yes (billing account) | Best free tier and short cold starts, but needs a card. Recommended above if a card is fine. |
| **Koyeb** | Yes ($29 pre-authorization hold) | One small free instance (0.1 vCPU, 512 MB), Frankfurt or Washington D.C. only. |
| **Railway** | Trial only | After the trial the free plan is $1 of credit per month, too little to keep an API running. |
| **Hugging Face Spaces** | — | Docker Spaces now need a paid plan. |
| **Fly.io, Northflank, Oracle Cloud** | Yes | No free tier without a card. |
| **Vercel, Netlify, Cloudflare Workers** | No | Can't run a .NET container on the free plan. |
| **Azure Container Apps / App Service** | Yes, except **Azure for Students** | Most Aspire-native (`aspire deploy`). Azure for Students gives credit without a card if you have an academic email. |

Free tiers and pricing change often. Check the provider's current terms before committing.
