#!/usr/bin/env bash
# Registers module Avro schemas with Confluent Schema Registry (ADR-019).
# Usage: SCHEMA_REGISTRY_URL=http://localhost:8081 ./scripts/register-avro-schemas.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SCHEMA_REGISTRY_URL="${SCHEMA_REGISTRY_URL:-http://localhost:8081}"
WB_SCHEMA_DIR="$ROOT/src/Modules/WorkflowBuilder/Axis.WorkflowBuilder.Contracts/Schemas"
DM_SCHEMA_DIR="$ROOT/src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas"
ID_SCHEMA_DIR="$ROOT/src/Modules/Identity/Axis.Identity.Contracts/Schemas"

register() {
  local file="$1"
  local subject="$2"
  if [[ ! -f "$file" ]]; then
    echo "ERROR: schema file not found: $file (subject: $subject, registry: $SCHEMA_REGISTRY_URL)" >&2
    exit 1
  fi
  local schema
  schema="$(tr -d '\n' <"$file" | sed 's/"/\\"/g')"
  curl -fsS -X POST \
    -H "Content-Type: application/vnd.schemaregistry.v1+json" \
    --data "{\"schema\": \"$schema\"}" \
    "$SCHEMA_REGISTRY_URL/subjects/$subject/versions" >/dev/null
  echo "registered $subject"
}

register "$WB_SCHEMA_DIR/WorkflowPublishedEvent.avsc" "axis.workflowbuilder.workflow-published-value"
register "$WB_SCHEMA_DIR/WorkflowArchivedEvent.avsc" "axis.workflowbuilder.workflow-archived-value"
register "$WB_SCHEMA_DIR/WorkflowUnarchivedEvent.avsc" "axis.workflowbuilder.workflow-unarchived-value"

register "$DM_SCHEMA_DIR/ModelCreatedEvent.avsc" "axis.datamodeling.model-created-value"
register "$DM_SCHEMA_DIR/ModelDeletedEvent.avsc" "axis.datamodeling.model-deleted-value"
register "$DM_SCHEMA_DIR/DataClassCreatedEvent.avsc" "axis.datamodeling.data-class-created-value"
register "$DM_SCHEMA_DIR/DataClassDeletedEvent.avsc" "axis.datamodeling.data-class-deleted-value"
register "$DM_SCHEMA_DIR/DataRecordCreatedEvent.avsc" "axis.datamodeling.data-record-created-value"
register "$DM_SCHEMA_DIR/DataRecordDeletedEvent.avsc" "axis.datamodeling.data-record-deleted-value"
register "$DM_SCHEMA_DIR/FieldAddedEvent.avsc" "axis.datamodeling.field-added-value"
register "$DM_SCHEMA_DIR/FieldUpdatedEvent.avsc" "axis.datamodeling.field-updated-value"
register "$DM_SCHEMA_DIR/FieldRemovedEvent.avsc" "axis.datamodeling.field-removed-value"

FB_SCHEMA_DIR="$ROOT/src/Modules/FormBuilder/Axis.FormBuilder.Contracts/Schemas"

register "$ID_SCHEMA_DIR/OrganizationVerifiedEvent.avsc" "axis.identity.organization-verified-value"
register "$ID_SCHEMA_DIR/TenantModuleProvisionReportEvent.avsc" "axis.identity.tenant-module-provision-report-value"
register "$ID_SCHEMA_DIR/UserDeactivatedEvent.avsc" "axis.identity.user-deactivated-value"
register "$ID_SCHEMA_DIR/UserReactivatedEvent.avsc" "axis.identity.user-reactivated-value"
register "$ID_SCHEMA_DIR/RoleAssignedEvent.avsc" "axis.identity.role-assigned-value"
register "$ID_SCHEMA_DIR/RoleRemovedEvent.avsc" "axis.identity.role-removed-value"

WE_SCHEMA_DIR="$ROOT/src/Modules/WorkflowEngine/Axis.WorkflowEngine.Contracts/Schemas"

register "$FB_SCHEMA_DIR/FormDeletedEvent.avsc" "axis.formbuilder.form-deleted-value"
register "$FB_SCHEMA_DIR/FormTaskSubmittedEvent.avsc" "axis.formbuilder.form-task-submitted-value"
register "$FB_SCHEMA_DIR/FormTaskExpiredEvent.avsc" "axis.formbuilder.form-task-expired-value"
register "$WE_SCHEMA_DIR/FormStepReachedEvent.avsc" "axis.workflowengine.form-step-reached-value"

echo "register-avro-schemas: OK"
