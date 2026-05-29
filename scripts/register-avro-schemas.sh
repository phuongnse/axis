#!/usr/bin/env bash
# Registers every module Avro event schema with Confluent Schema Registry (ADR-019).
#
# Self-maintaining: discovers schemas by globbing src/Modules/**/Schemas/*Event.avsc
# and derives the subject the same way Confluent's default TopicNameStrategy does —
# "<topic>-value", where <topic> is "axis.<module>.<kebab-cased event name without
# the Event suffix>" (mirrors the *KafkaTopics.cs constants). Adding a new
# *Event.avsc under a module's Contracts/Schemas needs no edit here.
#
# Nested record schemas (*Record.avsc) are payload sub-types, not top-level Kafka
# values — they get no subject of their own and are intentionally skipped.
#
# Usage:
#   SCHEMA_REGISTRY_URL=http://localhost:8081 ./scripts/register-avro-schemas.sh
#   DRY_RUN=1 ./scripts/register-avro-schemas.sh   # print subjects, no HTTP calls
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SCHEMA_REGISTRY_URL="${SCHEMA_REGISTRY_URL:-http://localhost:8081}"
DRY_RUN="${DRY_RUN:-}"

camel_to_kebab() {
  sed -E 's/([a-z0-9])([A-Z])/\1-\2/g' <<<"$1" | tr '[:upper:]' '[:lower:]'
}

register() {
  local file="$1" subject="$2" schema
  if [[ -n "$DRY_RUN" ]]; then
    echo "would register $subject  <-  ${file#"$ROOT/"}"
    return
  fi
  schema="$(tr -d '\n' <"$file" | sed 's/"/\\"/g')"
  curl -fsS -X POST \
    -H "Content-Type: application/vnd.schemaregistry.v1+json" \
    --data "{\"schema\": \"$schema\"}" \
    "$SCHEMA_REGISTRY_URL/subjects/$subject/versions" >/dev/null
  echo "registered $subject"
}

count=0
while IFS= read -r file; do
  module="$(sed -E 's#.*/src/Modules/([^/]+)/.*#\1#' <<<"$file" | tr '[:upper:]' '[:lower:]')"
  name="$(basename "$file" .avsc)"
  topic="axis.${module}.$(camel_to_kebab "${name%Event}")"
  register "$file" "${topic}-value"
  count=$((count + 1))
done < <(find "$ROOT/src/Modules" -path '*/Schemas/*Event.avsc' | sort)

echo "register-avro-schemas: OK ($count schemas)"
