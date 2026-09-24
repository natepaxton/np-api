# Testing locally with Postman

## 1. Start the stack

```sh
dotnet run --project src/NpApi.AppHost
```

API base URL: `https://localhost:7223`. If Postman complains about the certificate, run
`dotnet dev-certs https --trust`, or turn off **Settings → SSL certificate verification** for local work.

## 2. Import the API

While the API is running in Development: **Postman → Import → Link** →
`https://localhost:7223/openapi/v1.json`. This creates a collection with every endpoint. Re-import
after you add endpoints.

Create a Postman **environment** "np-api local" with:

| Variable | Value |
| --- | --- |
| `baseUrl` | `https://localhost:7223` |
| `auth0Domain` | `your-tenant.us.auth0.com` |
| `audience` | your API identifier |

Set the collection's base URL to `{{baseUrl}}`.

## 3. Get an Auth0 token

Configure auth once on the **collection** (Authorization tab → type **OAuth 2.0**). Requests
inherit it.

**Option A: as a real user (recommended; matches what the frontends do).**
Terraform defines a public PKCE client for Postman (the `postman` entry in
`infra/auth0/terraform.tfvars`). Its client ID is in `terraform output spa_client_ids`. In Postman:

- Grant type: Authorization Code (With PKCE)
- Callback URL: `https://oauth.pstmn.io/v1/callback` (tick "Authorize using browser")
- Auth URL: `https://{{auth0Domain}}/authorize?audience={{audience}}`
- Access Token URL: `https://{{auth0Domain}}/oauth/token`
- Client ID: the Postman client ID. **Leave Client Secret empty.**
- Scope: `openid profile email`

Click **Get New Access Token**, log in, then **Use Token**.

**Option B: machine-to-machine (no login, but no real user).**
Add a Machine-to-Machine application and an `auth0_client_grant` with `subject_type = "client"`
in Terraform, then use grant type **Client Credentials** with the same token URL, the app's
client ID and secret, and `audience` = `{{audience}}` under Advanced → Token Request. The `sub`
will be `<client-id>@clients`.

## 4. Smoke test

| Request | Expected |
| --- | --- |
| `GET {{baseUrl}}/alive` (No Auth) | 200 `{"status":"Healthy",...}` |
| `GET {{baseUrl}}/health` (No Auth) | 200, includes `AppDbContext` |
| `GET {{baseUrl}}/api/v1/me` without token | 401 |
| `GET {{baseUrl}}/api/v1/me` with token | 200 with your `userId` and `permissions` |
| `POST {{baseUrl}}/api/v1/notes` body `{"title":"hi"}` | 201 with `Location` header |
| `GET {{baseUrl}}/api/v1/notes` | 200, list including the note |

When you get a 401, the `WWW-Authenticate` response header says why (for example
`invalid_token, error_description="The audience ... is invalid"`). Paste the token into
[jwt.io](https://jwt.io) and check that `aud` matches your audience and `iss` is `https://<domain>/`.

The Aspire dashboard shows each request's trace, including the SQL that EF and Dapper ran.
