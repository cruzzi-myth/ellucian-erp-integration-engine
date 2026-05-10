# outputs.tf
# Outputs consumed by the GitHub Actions deploy pipeline
# and referenced by other Terraform modules (e.g., per-tenant onboarding automation).

output "aks_cluster_name" {
  description = "AKS cluster name — used by GitHub Actions to set kubectl context"
  value       = azurerm_kubernetes_cluster.primary.name
}

output "aks_resource_group" {
  description = "Resource group containing the AKS cluster"
  value       = azurerm_resource_group.primary.name
}

output "servicebus_namespace_alias" {
  description = "Geo-DR alias connection string — always use this, never the primary namespace directly"
  value       = azurerm_servicebus_namespace_disaster_recovery_config.geo_dr.alias_primary_connection_string
  sensitive   = true
}

output "sql_failover_read_write_endpoint" {
  description = "Failover group read-write listener — automatically routes to whichever region is primary"
  value       = "${azurerm_mssql_failover_group.main.name}.database.windows.net"
}

output "key_vault_uri" {
  description = "Key Vault URI for application configuration — used in appsettings.json as a Key Vault reference"
  value       = azurerm_key_vault.main.vault_uri
}

output "application_insights_connection_string" {
  description = "App Insights connection string — set as environment variable in AKS pod spec"
  value       = azurerm_application_insights.main.connection_string
  sensitive   = true
}

output "front_door_hostname" {
  description = "Azure Front Door endpoint hostname — the public entry point for all API traffic"
  value       = azurerm_cdn_frontdoor_profile.main.resource_guid
}
