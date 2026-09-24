# Nothing here is secret. Credentials come from .env.

# Existing API. The identifier can't be changed, so it stays "np-api" (the backend's Auth0:Audience).
api_identifier = "np-api"

api_permissions = {
  "read:photos"  = "Read photo records"
  "write:photos" = "Create and update photo records"
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
    scopes = ["read:photos", "write:photos"]
  }

  # Postman as a public PKCE client (no secret). Leave Client Secret empty in Postman.
  postman = {
    name          = "np-postman"
    callback_urls = ["https://oauth.pstmn.io/v1/callback"]
    logout_urls   = []
    web_origins   = []
    scopes        = ["read:photos", "write:photos"]
  }
}
