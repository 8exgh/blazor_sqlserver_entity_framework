resource "random_password" "sql_admin" {
  length           = 28
  min_lower        = 1
  min_upper        = 1
  min_numeric      = 1
  min_special      = 1
  special          = true
  # Azure SQL rejects some punctuation in passwords, and anything that needs escaping in a
  # connection string causes trouble downstream.
  override_special = "!#%*()-_=+[]{}?"
}

resource "azurerm_mssql_server" "this" {
  name                = "sql-${local.base}-${local.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  version             = "12.0"

  administrator_login          = var.sql_admin_username
  administrator_login_password = random_password.sql_admin.result

  minimum_tls_version = "1.2"

  # PoC: the server is reachable from the internet, gated only by the firewall rules below.
  # Production wants public_network_access_enabled = false plus a private endpoint.
  public_network_access_enabled = true

  tags = local.tags
}

# The documented way to say "any Azure service", which is what App Service outbound traffic
# looks like on a non-VNet-integrated plan.
resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_mssql_firewall_rule" "client" {
  count = var.client_ip_address == null ? 0 : 1

  name             = "AllowOperator"
  server_id        = azurerm_mssql_server.this.id
  start_ip_address = var.client_ip_address
  end_ip_address   = var.client_ip_address
}

resource "azurerm_mssql_database" "this" {
  name      = "InvoiceRecon" # matches the database the app's connection string expects
  server_id = azurerm_mssql_server.this.id

  sku_name    = var.sql_sku_name
  max_size_gb = var.sql_max_size_gb
  collation   = "SQL_Latin1_General_CP1_CI_AS"

  zone_redundant       = false
  storage_account_type = "Local" # cheapest backup redundancy; fine for a PoC

  tags = local.tags
}
