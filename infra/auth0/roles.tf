# np-api permissions granted to existing Auth0 roles. The roles themselves (and any permissions
# they hold for other APIs) are managed outside Terraform, so they're looked up by name and
# permissions are added one by one with auth0_role_permission (additive) rather than
# auth0_role_permissions, which would remove everything not listed here.
data "auth0_role" "role" {
  for_each = var.role_permissions

  name             = each.key
  skip_permissions = true
  skip_users       = true
}

locals {
  role_permission_pairs = merge([
    for role, permissions in var.role_permissions : {
      for permission in permissions : "${role}/${permission}" => { role = role, permission = permission }
    }
  ]...)
}

resource "auth0_role_permission" "api" {
  for_each = local.role_permission_pairs

  role_id                    = data.auth0_role.role[each.value.role].id
  resource_server_identifier = auth0_resource_server.api.identifier
  permission                 = each.value.permission

  depends_on = [auth0_resource_server_scopes.api]
}
