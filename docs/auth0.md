# Auth0

The frontends sign users in with Auth0 and send an **access token** to this API as
`Authorization: Bearer <token>`. The API only validates tokens. It never shows a login page or
talks to Auth0 per request. Signing keys are fetched once from the tenant's JWKS endpoint and cached.

## Tenant setup

The API, SPA applications, and the grants between them are managed with Terraform in
`infra/auth0/`. See [auth0-terraform.md](auth0-terraform.md) for the full checklist of what each
SPA needs, and for how the existing tenant was imported.

Each frontend must request tokens **for this API**:

```js
// @auth0/auth0-react / auth0-spa-js
authorizationParams: { audience: "https://api.np-api.dev", redirect_uri: window.location.origin }
```

Without `audience`, Auth0 issues an opaque token or only an ID token, and the API rejects it with 401.
**Do not send the ID token to the API.** It identifies the user to the frontend and isn't meant for APIs.

Add each frontend's origin to `Cors:AllowedOrigins` (see `appsettings.Development.json`, or
`Cors__AllowedOrigins__0=https://app.example.com` in production).

## API configuration

| Setting | Value | Local | Deployed |
| --- | --- | --- | --- |
| `Auth0:Domain` | `your-tenant.us.auth0.com` or your custom domain (a leading `https://` is tolerated) | AppHost parameter `auth0-domain` | env `Auth0__Domain` |
| `Auth0:Audience` | The API identifier (`np-api` in this tenant) | AppHost parameter `auth0-audience` | env `Auth0__Audience` |

The API fails at startup if either is missing (`ValidateOnStart`).

## How it's wired (`src/NpApi.Api/Auth/AuthExtensions.cs`)

- `AddJwtBearer` with `Authority = https://{Domain}/` and `Audience`. The handler validates the
  issuer, audience, lifetime, and signature.
- `MapInboundClaims = false` keeps Auth0 claim names (`sub`, `permissions`, `scope`) instead of
  translating them to long `http://schemas.xmlsoap.org/...` URIs.
- **Fallback policy = authenticated user.** Every endpoint requires a valid token unless it calls
  `.AllowAnonymous()`. New endpoints are secure by default.
- `User.GetUserId()` returns the `sub` claim (`auth0|abc123`, `google-oauth2|123`). Use it as the
  owner key in your tables. Don't use email, which can change and isn't always verified.

## Permission-based authorization

When you turn on RBAC, add a policy per permission and apply it to endpoints:

```csharp
// AuthExtensions.AddAuth0()
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(...)
    .AddPolicy("write:notes", p => p.RequireClaim("permissions", "write:notes"));

// endpoint
notes.MapPost("/", ...).RequireAuthorization("write:notes");
```

Auth0 puts permissions in a JSON array claim, and the JWT handler turns each element into its own
`permissions` claim, so `RequireClaim` works directly.

## Machine-to-machine callers

Other backends can use the **client credentials** flow with an Auth0 Machine-to-Machine
application. The API's client access policy is per-app, so each one needs an `auth0_client_grant`
with `subject_type = "client"` in `infra/auth0/`. Those tokens have
`sub = <client-id>@clients` and no user. Keep that in mind for endpoints that assume a human owner.
Auth0's free plan limits how many M2M tokens you can issue each month, so cache and reuse them
until they expire.
