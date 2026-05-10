# variables.tf
# All input variables for the integration engine infrastructure.
# Sensitive values (passwords, secrets) are passed via environment variables
# or a secrets manager — never committed to version control.

variable "environment" {
  description = "Deployment environment identifier (prod, staging, dev)"
  type        = string

  validation {
    condition     = contains(["prod", "staging", "dev"], var.environment)
    error_message = "Environment must be one of: prod, staging, dev."
  }
}

variable "primary_region" {
  description = "Primary Azure region for all active resources"
  type        = string
  default     = "eastus"
}

variable "secondary_region" {
  description = "Secondary Azure region for DR/passive replicas"
  type        = string
  default     = "westus2"
}

variable "kubernetes_version" {
  description = "AKS Kubernetes version. Pin to a tested minor version; upgrade deliberately."
  type        = string
  default     = "1.28"
}

variable "sql_admin_username" {
  description = "SQL Server administrator login name"
  type        = string
  sensitive   = true
}

variable "sql_admin_password" {
  description = "SQL Server administrator password. Use Key Vault reference in prod."
  type        = string
  sensitive   = true
}

variable "sql_aad_admin_login" {
  description = "Azure AD user/group login for SQL AAD administrator"
  type        = string
}

variable "sql_aad_admin_object_id" {
  description = "Azure AD object ID for the SQL AAD administrator"
  type        = string
}
