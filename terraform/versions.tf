terraform {
  required_version = ">= 1.9.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # PoC shortcut: state lives on disk next to this file. Anything with more than one
  # operator wants a remote backend instead, e.g.
  #
  # backend "azurerm" {
  #   resource_group_name  = "rg-tfstate"
  #   storage_account_name = "sttfstate<suffix>"
  #   container_name       = "tfstate"
  #   key                  = "invoicerecon.tfstate"
  # }
}

provider "azurerm" {
  subscription_id = var.subscription_id

  features {
    key_vault {
      # PoC convenience: `terraform destroy` should leave nothing behind that blocks a
      # re-apply under the same name. Do not do this in production.
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = true
    }
  }
}
