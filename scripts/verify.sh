#!/usr/bin/env bash
# Fast local gate for pre-push feedback. CI remains the authoritative full gate
# and runs the Testcontainers-backed Infrastructure/API suites before merge.
#
# Usage: scripts/verify.sh
#   build + vulnerable package scan + dotnet format --verify + unit test
#   projects + frontend ci+test + doc drift.
#
# Only the layers whose files changed (vs origin/main) run, so doc-only and
# frontend-only work stays quick. On Windows run via Git Bash. Wired as the
# pre-push hook (scripts/hooks/pre-push).
set -uo pipefail
cd "$(dirname "$0")/.." || {
  echo "verify.sh: failed to locate repository root" >&2
  exit 1
}

if [ "$#" -gt 0 ]; then
  echo "verify.sh: mode argument '$1' ignored - verify.sh runs the fast local gate." >&2
fi

BASE="${BASE_BRANCH:-main}"
if git rev-parse --verify "origin/${BASE}" >/dev/null 2>&1; then
  RANGE="origin/${BASE}...HEAD"
else
  RANGE="HEAD~1...HEAD"
fi
CHANGED="$(git diff --name-only "${RANGE}" 2>/dev/null || true)"
changed() { echo "${CHANGED}" | grep -qE "$1"; }

DOTNET=false
FE=false
if [ -z "${CHANGED}" ]; then
  DOTNET=true
  FE=true
else
  changed '^(src/|tests/|Directory\.|Axis\.sln$|global\.json$|\.editorconfig$|openapi\.json$|\.github/workflows/build-and-test\.yml$)' && DOTNET=true
  changed '^(frontend/|\.editorconfig$|openapi\.json$|\.github/workflows/build-and-test\.yml$)' && FE=true
fi

# Endpoint changes commonly require regenerating openapi.json and frontend
# generated API types. This is a reminder; OpenApiDocumentTests remain the hard
# gate in CI because they need the API test fixture.
API_SURFACE_DRIFT=false
if changed '^src/Axis\.Api/Endpoints/' && ! changed '^openapi\.json$'; then
  API_SURFACE_DRIFT=true
fi

FAILED=()
step() {
  local name="$1" cmd="$2"
  echo ""
  echo "> ${name}"
  if bash -c "${cmd}"; then
    echo "OK ${name}"
  else
    echo "FAIL ${name}"
    FAILED+=("${name}")
  fi
}

echo "verify.sh - .NET=${DOTNET} frontend=${FE}"

if [ "${DOTNET}" = true ]; then
  step ".NET build" "dotnet build Axis.sln --nologo"
  step ".NET vulnerable packages" "bash ./scripts/check-vulnerable-packages.sh"
  step ".NET format" "dotnet format Axis.sln --verify-no-changes"
  step ".NET test (unit projects)" "bash ./scripts/test-unit.sh"
fi

if [ "${FE}" = true ]; then
  step "frontend ci (tsc + biome)" "cd frontend && npm run ci"
  step "frontend test" "cd frontend && npm run test"
fi

step "doc drift" "./scripts/check-doc-drift.sh"

if [ "${API_SURFACE_DRIFT}" = true ]; then
  echo ""
  echo "WARN API surface changed (src/Axis.Api/Endpoints/) but openapi.json is unchanged."
  echo "  If you added or changed a route / request / response shape, regenerate the contract:"
  echo "    dotnet test tests/Api/Axis.Api.Tests --filter OpenApiDocumentTests"
  echo "    (cd frontend && npm run gen:api-types)"
  echo "  then commit openapi.json + api-types.ts; CI's OpenApiDocumentTests fails otherwise."
fi

echo ""
if [ "${#FAILED[@]}" -eq 0 ]; then
  echo "verify.sh: PASS"
  exit 0
fi

echo "verify.sh: FAIL - ${#FAILED[@]} step(s): ${FAILED[*]}" >&2
exit 1
