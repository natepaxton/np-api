terraform {
  required_version = ">= 1.7"

  required_providers {
    auth0 = {
      source  = "auth0/auth0"
      version = "~> 1.58"
    }
  }
}

# Credentials come from environment variables so nothing secret lives in this directory:
#   AUTH0_DOMAIN, AUTH0_CLIENT_ID, AUTH0_CLIENT_SECRET
# They belong to the "Terraform" machine-to-machine app described in docs/auth0-terraform.md.
provider "auth0" {}
