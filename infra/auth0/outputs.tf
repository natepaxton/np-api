output "api_audience" {
  description = "Use as Auth0:Audience in the backend and authorizationParams.audience in every SPA."
  value       = auth0_resource_server.api.identifier
}

output "spa_client_ids" {
  description = "clientId for each SPA's Auth0 SDK configuration."
  value       = { for key, client in auth0_client.spa : key => client.client_id }
}
