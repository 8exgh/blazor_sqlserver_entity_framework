locals {
  # Key Vault names are alphanumeric + hyphen, 3-24 chars. Trim the project/environment part
  # so "kv-" + name + "-" + suffix always fits.
  kv_stub = substr(replace(local.base, "-", ""), 0, min(14, length(replace(local.base, "-", ""))))

  sql_connection_string = join("", [
    "Server=tcp:${azurerm_mssql_server.this.fully_qualified_domain_name},1433;",
    "Database=${azurerm_mssql_database.this.name};",
    "User ID=${var.sql_admin_username};",
    "Password=${random_password.sql_admin.result};",
    "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;",
  ])
}

resource "azurerm_key_vault" "this" {
  name                = "kv-${local.kv_stub}-${local.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  # RBAC rather than access policies: one permission model for the whole subscription.
  rbac_authorization_enabled = true

  soft_delete_retention_days = 7
  purge_protection_enabled   = false # PoC: keeps `terraform destroy` clean

  tags = local.tags
}

# Whoever runs terraform needs data-plane rights to write the secret; the control-plane
# Contributor role does not grant that under RBAC.
resource "azurerm_role_assignment" "deployer_kv_secrets_officer" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_key_vault_secret" "sql_connection_string" {
  name         = "sql-connection-string"
  value        = local.sql_connection_string
  key_vault_id = azurerm_key_vault.this.id

  # Role assignments are eventually consistent; without this the first apply can 403.
  depends_on = [azurerm_role_assignment.deployer_kv_secrets_officer]
}

resource "azurerm_role_assignment" "app_kv_secrets_user" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_linux_web_app.this.identity[0].principal_id
}
