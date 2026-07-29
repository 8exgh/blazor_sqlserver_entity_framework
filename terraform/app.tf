resource "azurerm_service_plan" "this" {
  name                = "asp-${local.base}-${local.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  os_type             = "Linux"
  sku_name            = var.app_service_sku_name
  tags                = local.tags
}

resource "azurerm_linux_web_app" "this" {
  name                = "app-${local.base}-${local.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_service_plan.this.location
  service_plan_id     = azurerm_service_plan.this.id

  https_only = true

  # Blazor Server keeps per-user circuit state in the server process, so requests for a
  # session must land on the same instance. ARR affinity is the cheap answer; the real one is
  # an Azure SignalR Service in serverless mode.
  client_affinity_enabled = true

  identity {
    type = "SystemAssigned"
  }

  site_config {
    always_on           = var.app_service_sku_name != "F1"
    ftps_state          = "Disabled"
    minimum_tls_version = "1.2"
    http2_enabled       = true

    # Interactive server rendering runs over a WebSocket.
    websockets_enabled = true

    health_check_path = "/"

    application_stack {
      dotnet_version = var.dotnet_version
    }
  }

  app_settings = {
    ASPNETCORE_ENVIRONMENT                = "Production"
    APPLICATIONINSIGHTS_CONNECTION_STRING = azurerm_application_insights.this.connection_string

    # App Service's codeless .NET agent — no SDK reference needed in the project.
    ApplicationInsightsAgent_EXTENSION_VERSION = "~3"
    XDT_MicrosoftApplicationInsights_Mode      = "recommended"
  }

  # Surfaces to the app as ConnectionStrings:Default, which is the name Program.cs reads.
  # The value is a Key Vault reference resolved at startup by the managed identity above.
  connection_string {
    name  = "Default"
    type  = "SQLAzure"
    value = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.sql_connection_string.versionless_id})"
  }

  logs {
    detailed_error_messages = true
    failed_request_tracing  = true

    application_logs {
      file_system_level = "Information"
    }

    http_logs {
      file_system {
        retention_in_days = 7
        retention_in_mb   = 35
      }
    }
  }

  tags = local.tags
}

resource "azurerm_monitor_diagnostic_setting" "app" {
  name                       = "to-log-analytics"
  target_resource_id         = azurerm_linux_web_app.this.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id

  enabled_log {
    category = "AppServiceConsoleLogs"
  }

  enabled_log {
    category = "AppServiceAppLogs"
  }

  enabled_log {
    category = "AppServiceHTTPLogs"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}
