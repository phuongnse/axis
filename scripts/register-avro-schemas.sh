#!/usr/bin/env bash
# Registers WorkflowBuilder Avro schemas with Confluent Schema Registry (ADR-019).
# Usage: SCHEMA_REGISTRY_URL=http://localhost:8081 ./scripts/register-avro-schemas.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SCHEMA_REGISTRY_URL="${SCHEMA_REGISTRY_URL:-http://localhost:8081}"
SCHEMA_DIR="$ROOT/src/Modules/WorkflowBuilder/Axis.WorkflowBuilder.Contracts/Schemas"

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

echo "register-avro-schemas: OK"
