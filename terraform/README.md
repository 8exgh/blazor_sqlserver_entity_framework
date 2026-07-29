# Azure deployment (sample)

Terraform for running the Invoice Reconciliation PoC on Azure: a Linux App Service in front of
an Azure SQL Database, with the connection string held in Key Vault and read by the app's
managed identity.

**This has never been applied against a real subscription.** It is a worked example, not a
tested deployment — read the *Before you apply* section below.

## What it creates

| Resource | Why |
|---|---|
| `azurerm_resource_group` | one group, `rg-invoicerecon-poc` |
| `azurerm_service_plan` (Linux, B1) | B1 is the cheapest tier with Always On, which matters because startup runs `EnsureCreated` + seeding |
| `azurerm_linux_web_app` | the Blazor app; system-assigned identity, WebSockets on, ARR affinity on |
| `azurerm_mssql_server` + `azurerm_mssql_database` | database named `InvoiceRecon`, matching what the app expects |
| `azurerm_mssql_firewall_rule` | Azure services, plus optionally your own IP |
| `azurerm_key_vault` + secret | holds the SQL connection string, RBAC data plane |
| two `azurerm_role_assignment` | the operator can write the secret; the app can read it |
| `azurerm_log_analytics_workspace` + `azurerm_application_insights` | logs and traces |

Roughly $20–25/month at the defaults (B1 plan ~$13, Basic database ~$5, plus log ingestion).

## Usage

```bash
cd terraform
cp terraform.tfvars.example terraform.tfvars   # fill in subscription_id
az login
terraform init
terraform plan
terraform apply
```

Then publish the app into it:

```bash
cd ..
dotnet publish InvoiceRecon -c Release -o ./publish
(cd publish && zip -r ../app.zip .)

az webapp deploy \
  --resource-group "$(terraform -chdir=terraform output -raw resource_group_name)" \
  --name "$(terraform -chdir=terraform output -raw app_name)" \
  --src-path app.zip --type zip

open "$(terraform -chdir=terraform output -raw app_url)"
```

First request is slow: the app retries the SQL connection, runs `EnsureCreated`, and seeds the
demo data.

Tear down with `terraform destroy` — the Key Vault provider block is configured to purge, so
the same names are reusable immediately.

## Before you apply

Things I could not verify without a subscription and the terraform CLI:

- **`dotnet_version = "10.0"`.** The app targets `net10.0`. If your azurerm provider rejects
  that value, either upgrade the provider or publish self-contained
  (`dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=false`) and set
  `dotnet_version` to a supported value — the bundled runtime is what actually runs.
- **Key Vault reference timing.** The app's role assignment on the vault is created after the
  app itself, and RBAC propagation takes a minute or two. If the first request fails to
  resolve `ConnectionStrings:Default`, restart the app once:
  `az webapp restart -g <rg> -n <app>`.
- **Provider argument drift.** `rbac_authorization_enabled` (Key Vault) and `enabled_metric`
  (diagnostic settings) are the azurerm 4.x spellings; on 3.x they are
  `enable_rbac_authorization` and `metric`.
- SKU availability (`Basic` SQL, `B1` plan) varies by region.

## PoC shortcuts

Matching the honesty of the app's own README — what you would change first for anything real:

- **SQL authentication, not Entra ID.** A generated password goes into Key Vault and the app
  reads it back. The better shape has no password at all: an `azuread_administrator` block on
  the server, then `CREATE USER [app-invoicerecon-…] FROM EXTERNAL PROVIDER` inside the
  database and `Authentication=Active Directory Default` in the connection string. Terraform
  cannot run that T-SQL itself — it needs a post-apply script or a deployment job — which is
  why this sample takes the password route.
- **Public networking.** The SQL server is internet-facing behind firewall rules. Production
  wants `public_network_access_enabled = false`, a private endpoint, and VNet integration on
  the App Service.
- **ARR affinity for Blazor circuits.** Fine on one instance; if you scale out, add an Azure
  SignalR Service in serverless mode instead.
- **No staging slot, no deployment pipeline.** Deploy is a manual `az webapp deploy`.
- **`EnsureCreated` at startup.** Carried over from the app. It races if two instances start
  at once, and it has no schema-evolution story — EF migrations run from the pipeline is the
  answer for both.
- **Local state.** Fine for one operator; see the commented backend block in `versions.tf`.
