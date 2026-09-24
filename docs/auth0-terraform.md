# Managing Auth0 with Terraform

The Auth0 API, SPA applications, and the grants between them are defined in `infra/auth0/`.
Change them there, not in the dashboard. Dashboard edits get reverted on the next `apply`.

## What "wired up correctly" means

Every SPA needs all of the following to send authorized requests to np-api:

| # | Where | Setting | Why |
| --- | --- | --- | --- |
| 1 | API | Identifier = the audience, RS256 | The backend validates `aud` and the signature |
| 2 | API | Application Access → User access = **Per-app authorization** (`require_client_grant`) | Only apps you list can get tokens for the API |
| 3 | API ↔ SPA | A **client grant** with `subject_type = "user"` | Authorizes that SPA under policy #2. Without it, the SPA gets `access_denied` / "Client is not authorized to access resource server" |
| 4 | SPA | Type SPA, auth method `none`, grants `authorization_code` + `refresh_token` | Public client using PKCE, no secret in the browser |
| 5 | SPA | Allowed Callback URLs, Logout URLs, Web Origins | Login redirect, logout, and silent token refresh |
| 6 | SPA code | `authorizationParams.audience` = API identifier | Without it Auth0 returns an opaque token the API rejects |
| 7 | Backend | `Auth0:Audience` = API identifier, `Cors:AllowedOrigins` includes the SPA origin | Token accepted, and the browser allows the call |

Terraform covers 1–5. You set 6 in each frontend and 7 in the backend config.

If your API's User access is currently **All apps allowed**, any app in the tenant can get tokens
for it. That works, but it's broader than you need. The Terraform config switches it to
per-app authorization and creates a grant for each SPA in the same apply.

## One-time bootstrap

Terraform needs its own credentials for the Auth0 Management API. This is the only thing you
create by hand:

1. Dashboard → Applications → **Create Application** → Machine to Machine → name it `Terraform`.
2. Authorize it for **Auth0 Management API** with these permissions:
   `read/create/update/delete:clients`, `read/create/update/delete:resource_servers`,
   `read/create/update/delete:client_grants`.
3. Put its credentials in `infra/auth0/.env` (gitignored). Load it with `set -a && source .env && set +a`
   so the variables are exported to Terraform even if the file has no `export` keywords:

   ```sh
   export AUTH0_DOMAIN=<tenant>.us.auth0.com
   export AUTH0_CLIENT_ID=...
   export AUTH0_CLIENT_SECRET=...
   ```

## Adopting what already exists

Your API and SPAs already exist, so import them rather than letting Terraform create duplicates.

1. List the current setup:

   ```sh
   cd infra/auth0 && set -a && source .env && set +a
   TOKEN=$(curl -s https://$AUTH0_DOMAIN/oauth/token -H 'content-type: application/json' \
     -d "{\"grant_type\":\"client_credentials\",\"client_id\":\"$AUTH0_CLIENT_ID\",\"client_secret\":\"$AUTH0_CLIENT_SECRET\",\"audience\":\"https://$AUTH0_DOMAIN/api/v2/\"}" | jq -r .access_token)
   curl -s -H "Authorization: Bearer $TOKEN" "https://$AUTH0_DOMAIN/api/v2/resource-servers" \
     | jq '.[] | select(.is_system != true) | {id, name, identifier, subject_type_authorization}'
   curl -s -H "Authorization: Bearer $TOKEN" "https://$AUTH0_DOMAIN/api/v2/clients?app_type=spa&fields=client_id,name,callbacks,web_origins" | jq
   curl -s -H "Authorization: Bearer $TOKEN" "https://$AUTH0_DOMAIN/api/v2/client-grants" | jq '.[] | {id, client_id, audience, subject_type}'
   ```

2. Copy `terraform.tfvars.example` to `terraform.tfvars`, and fill in the real identifier, names,
   and URLs so they match what exists.
3. Uncomment `imports.tf` and fill in the IDs.
4. `terraform init && terraform plan`. The plan should show **imports plus small in-place
   updates**, with no creates or destroys for existing apps. Read every change before applying.
   Each difference is a setting that was wrong or that the config chose differently.
5. `terraform apply`, then delete the import blocks.

## Day-to-day

```sh
cd infra/auth0 && set -a && source .env && set +a
terraform plan
terraform apply
terraform output spa_client_ids   # clientId for each frontend's Auth0 config
```

Role permissions for np-api live in `role_permissions` in `terraform.tfvars` (currently `admin`:
read + write for photos and people, `member`: read for both). The roles themselves and who is in them are managed in
the Auth0 dashboard. Terraform only **adds** np-api permissions to them (`auth0_role_permission`),
so permissions the roles hold for other APIs are left alone.

Adding a SPA means adding an entry to `spas` in `terraform.tfvars`. The app, its PKCE settings,
and its grant to the API are created together.

## State

State is local (`terraform.tfstate`, gitignored). It holds client IDs and settings. SPAs have no
secrets, but the file is still the only record Terraform keeps of what it manages, so don't lose
it. When CI needs to run Terraform or a second machine is involved, move state to a remote
backend such as HCP Terraform (free tier) or a GCS bucket.
