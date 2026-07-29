variable "subscription_id" {
  description = "Azure subscription to deploy into. Also settable via ARM_SUBSCRIPTION_ID."
  type        = string
}

variable "project" {
  description = "Short name used to build every resource name. Lowercase letters and digits only."
  type        = string
  default     = "invoicerecon"

  validation {
    condition     = can(regex("^[a-z][a-z0-9]{2,15}$", var.project))
    error_message = "project must be 3-16 chars, lowercase letters and digits, starting with a letter."
  }
}

variable "environment" {
  description = "Environment slug folded into resource names."
  type        = string
  default     = "poc"

  validation {
    condition     = can(regex("^[a-z0-9]{2,6}$", var.environment))
    error_message = "environment must be 2-6 lowercase letters or digits."
  }
}

variable "location" {
  description = "Azure region for every resource."
  type        = string
  default     = "eastus"
}

variable "app_service_sku_name" {
  description = <<-EOT
    App Service plan SKU. B1 is the cheapest tier that supports Always On, which this app
    needs: startup runs EnsureCreated + seeding, so a cold start is expensive. F1 works but
    forces always_on = false.
  EOT
  type        = string
  default     = "B1"
}

variable "dotnet_version" {
  description = <<-EOT
    App Service .NET runtime. The app targets net10.0. If your azurerm provider version does
    not yet accept "10.0" here, either upgrade the provider or publish self-contained
    (`dotnet publish -r linux-x64 --self-contained`) and pin this to a supported value.
  EOT
  type        = string
  default     = "10.0"
}

variable "sql_sku_name" {
  description = <<-EOT
    Azure SQL Database SKU. "Basic" (~$5/mo) is enough for the demo seed. "S0" gives more
    headroom; "GP_S_Gen5_1" is serverless and auto-pauses, which is cheaper when idle but
    adds a resume delay on the first request.
  EOT
  type        = string
  default     = "Basic"
}

variable "sql_max_size_gb" {
  description = "Database max size. Must be <= 2 for the Basic SKU."
  type        = number
  default     = 2
}

variable "sql_admin_username" {
  description = "SQL Server administrator login. Cannot be 'sa', 'admin', 'root' or similar."
  type        = string
  default     = "reconadmin"
}

variable "client_ip_address" {
  description = <<-EOT
    Optional public IP allowed through the SQL firewall so you can connect from your machine
    (`curl -s ifconfig.me`). Leave null to allow only Azure services.
  EOT
  type        = string
  default     = null
}

variable "log_retention_days" {
  description = "Log Analytics retention."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Extra tags merged onto every resource."
  type        = map(string)
  default     = {}
}
