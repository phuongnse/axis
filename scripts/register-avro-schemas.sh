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
  local schema
  schema="$(tr -d '\n' <"$file" | sed 's/"/\\"/g')"
  curl -fsS -X POST \
    -H "Content-Type: application/vnd.schemaregistry.v1+json" \
    --data "{\"schema\": \"$schema\"}" \
    "$SCHEMA_REGISTRY_URL/subjects/$subject/versions" >/dev/null
  echo "registered $subject"
}

register "$SCHEMA_DIR/WorkflowPublishedEvent.avsc" "axis.workflowbuilder.workflow-published-value"
register "$SCHEMA_DIR/WorkflowArchivedEvent.avsc" "axis.workflowbuilder.workflow-archived-value"
register "$SCHEMA_DIR/WorkflowUnarchivedEvent.avsc" "axis.workflowbuilder.workflow-unarchived-value"

echo "register-avro-schemas: OK"
