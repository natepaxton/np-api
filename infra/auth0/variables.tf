variable "api_name" {
  description = "Display name of the API in the Auth0 dashboard."
  type        = string
  default     = "np-api"
}

variable "api_identifier" {
  description = "API identifier (the audience). Must match Auth0:Audience in the backend and the audience the SPAs request. Cannot be changed after creation."
  type        = string
}

variable "api_permissions" {
  description = "Permissions defined on the API (name => description). Assign them to roles in Auth0; each SPA's grant caps which ones its tokens can carry."
  type        = map(string)
}

variable "spas" {
  description = "Single-page applications allowed to call the API on behalf of signed-in users. The map key is a stable Terraform address; don't rename it after import."
  type = map(object({
    name = string
    # Full URLs, e.g. "http://localhost:5173" and "https://app.example.com".
    callback_urls = list(string)
    logout_urls   = list(string)
    # Origins only (scheme + host + port, no path), e.g. "https://app.example.com".
    web_origins = list(string)
    # Permissions this app may request for the API. Empty until RBAC permissions are defined.
    scopes = optional(list(string), [])
    # "true" skips Auth0's confirmation prompt for callback URLs it can't verify (e.g. localhost).
    skip_non_verifiable_callback_uri_confirmation_prompt = optional(string)
  }))
}
