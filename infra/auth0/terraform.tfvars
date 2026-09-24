# Nothing here is secret. Credentials come from .env.

# Existing API. The identifier can't be changed, so it stays "np-api" (the backend's Auth0:Audience).
api_identifier = "np-api"

api_permissions = {
  "read:photos"  = "Read photo records"
  "write:photos" = "Create and update photo records"
  "read:people"  = "Read people (photo subjects and camera owners)"
  "write:people" = "Create, update and delete people"
}

# What each Auth0 role may do in np-api. Users get these in their token's "permissions" claim.
role_permissions = {
  admin  = ["read:photos", "write:photos", "read:people", "write:people"]
  member = ["read:photos", "read:people"]
}

spas = {
  # One shared SPA client for every np-web app (sandbox, yellowstone, roadie). The Auth0 SDK
  # sends window.location.origin as both redirect_uri and logout returnTo, so every URL is an
  # origin with no trailing slash and the three lists are identical. Add new apps' origins to all three.
  np_aspire = {
    name = "np-aspire"
    callback_urls = [
      "http://localhost:4300",              # sandbox
      "http://localhost:4301",              # yellowstone
      "http://localhost:4302",              # roadie
      "https://np-yellowstone.netlify.app", # yellowstone (Netlify)
      "https://yellowstone.natepaxton.com", # yellowstone
    ]
    logout_urls = [
      "http://localhost:4300",              # sandbox
      "http://localhost:4301",              # yellowstone
      "http://localhost:4302",              # roadie
      "https://np-yellowstone.netlify.app", # yellowstone (Netlify)
      "https://yellowstone.natepaxton.com", # yellowstone
    ]
    web_origins = [
      "http://localhost:4300",              # sandbox
      "http://localhost:4301",              # yellowstone
      "http://localhost:4302",              # roadie
      "https://np-yellowstone.netlify.app", # yellowstone (Netlify)
      "https://yellowstone.natepaxton.com", # yellowstone
    ]
    scopes = ["read:photos", "write:photos", "read:people", "write:people"]
  }

  # Public PKCE client (no secret) for developer tools: Postman (leave Client Secret empty) and
  # scripts/get-dev-token.py, which listens on localhost:8765 for the redirect.
  postman = {
    name          = "np-postman"
    callback_urls = ["https://oauth.pstmn.io/v1/callback", "http://localhost:8765/callback"]
    logout_urls   = []
    web_origins   = []
    scopes        = ["read:photos", "write:photos", "read:people", "write:people"]
  }
}
