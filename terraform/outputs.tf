output "resource_group_name" {
  description = "Resource group holding everything."
  value       = azurerm_resource_group.this.name
}

output "app_name" {
  description = "App Service name, for `az webapp deploy --name`."
  value       = azurerm_linux_web_app.this.name
}

output "app_url" {
  description = "Where the app lands once code is deployed."
  value       = "https://${azurerm_linux_web_app.this.default_hostname}"
}

output "sql_server_fqdn" {
  description = "Azure SQL host, for sqlcmd or Azure Data Studio."
  value       = azurerm_mssql_server.this.fully_qualified_domain_name
}

output "sql_database_name" {
  value = azurerm_mssql_database.this.name
}

output "sql_admin_username" {
  value = var.sql_admin_username
}

output "sql_admin_password" {
  description = "Generated SQL admin password: `terraform output -raw sql_admin_password`."
  value       = random_password.sql_admin.result
  sensitive   = true
}

output "key_vault_name" {
  value = azurerm_key_vault.this.name
}

output "application_insights_name" {
  value = azurerm_application_insights.this.name
}
