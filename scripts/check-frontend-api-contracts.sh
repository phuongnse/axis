#!/usr/bin/env bash
# Frontend API contracts must come from frontend/src/lib/api-types.ts, which is
# generated from openapi.json. Hand-authored Request/Response/Dto types silently
# reintroduce FE/BE drift.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

ERR=0
fail() {
  echo "check-frontend-api-contracts FAIL: $1" >&2
  ERR=1
}

scan() {
  local pattern="$1"
  if command -v rg >/dev/null 2>&1 && rg --version >/dev/null 2>&1; then
    rg -n "${pattern}" frontend/src \
      --glob '*.ts' \
      --glob '*.tsx' \
      --glob '!lib/api-types.ts' \
      --glob '!routeTree.gen.ts' \
      --glob '!**/node_modules/**' \
      || true
    return
  fi

  find frontend/src \
    \( -path '*/node_modules/*' -o -path '*/lib/api-types.ts' -o -path '*/routeTree.gen.ts' \) -prune \
    -o \( -name '*.ts' -o -name '*.tsx' \) -type f -print0 \
    | xargs -0 grep -nE "${pattern}" 2>/dev/null \
    || true
}

while IFS= read -r hit; do
  [ -z "${hit}" ] && continue
  fail "Hand-authored frontend API model. Import/alias from generated api-types.ts instead: ${hit}"
done < <(
  scan '(^|[[:space:]])(export[[:space:]]+)?(interface|type)[[:space:]]+[A-Za-z0-9_]*(Request|Response|Dto)\b' \
    | grep -Ev "components\\[['\"]schemas['\"]\\]|operations\\[['\"]" \
    || true
)

if [ "${ERR}" -ne 0 ]; then
  echo "" >&2
  echo "Run scripts/generate-api-contracts.ps1 after API contract changes." >&2
  exit 1
fi

echo "check-frontend-api-contracts: OK"
