resource "auth0_resource_server" "api" {
  name        = var.api_name
  identifier  = var.api_identifier
  signing_alg = "RS256"

  # RBAC: users' role permissions (capped by each app's client grant) go into the
  # access token's "permissions" claim.
  enforce_policies = true
  token_dialect    = "access_token_authz"

  # Lets SPAs get refresh tokens (auth0-spa-js useRefreshTokens / offline_access).
  allow_offline_access = true

  # Your own apps don't show a consent screen. Consent still appears for localhost, which
  # Auth0 can't verify as first party.
  skip_consent_for_verifiable_first_party_clients = true

  subject_type_authorization {
    # Only apps with a user client grant below can get tokens for this API on behalf of users.
    user {
      policy = "require_client_grant"
    }
    # No machine-to-machine access unless a client grant with subject_type = "client" is added.
    client {
      policy = "require_client_grant"
    }
  }
}

resource "auth0_resource_server_scopes" "api" {
  resource_server_identifier = auth0_resource_server.api.identifier

  dynamic "scopes" {
    for_each = var.api_permissions
    content {
      name        = scopes.key
      description = scopes.value
    }
  }
}

resource "auth0_client" "spa" {
  for_each = var.spas

  name            = each.value.name
  app_type        = "spa"
  is_first_party  = true
  oidc_conformant = true

  grant_types = ["authorization_code", "refresh_token"]

  callbacks           = each.value.callback_urls
  allowed_logout_urls = each.value.logout_urls
  web_origins         = each.value.web_origins
  allowed_origins     = each.value.web_origins

  skip_non_verifiable_callback_uri_confirmation_prompt = each.value.skip_non_verifiable_callback_uri_confirmation_prompt

  jwt_configuration {
    alg = "RS256"
  }

  refresh_token {
    rotation_type       = "rotating"
    expiration_type     = "expiring"
    leeway              = 0
    token_lifetime      = 2592000 # 30 days absolute
    idle_token_lifetime = 1296000 # 15 days idle
  }
}

# SPAs are public clients: PKCE, no client secret.
resource "auth0_client_credentials" "spa" {
  for_each = var.spas

  client_id             = auth0_client.spa[each.key].id
  authentication_method = "none"
}

# The link that authorizes each SPA to request user tokens for the API.
resource "auth0_client_grant" "spa_user_access" {
  for_each = var.spas

  client_id    = auth0_client.spa[each.key].id
  audience     = auth0_resource_server.api.identifier
  subject_type = "user"
  scopes       = each.value.scopes

  # Grants can only reference permissions that exist on the API.
  depends_on = [auth0_resource_server_scopes.api]
}
