#!/usr/bin/env bash
# Local mirror of the CI gate (.github/workflows/build-and-test.yml).
# Closes the "build passed locally but CI failed" gap: runs the SAME commands CI
# runs — INCLUDING the Testcontainers integration tests — so charset/format,
# casing, drift AND integration failures surface here, before push, not after a
# red CI run. There is no "fast"/skip-integration mode: a backend change never
# reaches a PR without its integration tests having passed locally first.
#
# Usage: scripts/verify.sh
#   build + dotnet format --verify + FULL `dotnet test` (Testcontainers; needs
#   Docker) + frontend ci+test + doc drift. This is THE push gate.
#
# Only the layers whose files changed (vs origin/main) run, so doc-only and
# frontend-only work stays quick and does not require Docker.
# On Windows run via Git Bash. Wired as the pre-push hook (scripts/hooks/pre-push).
set -uo pipefail
cd "$(dirname "$0")/.." || {
  echo "verify.sh: failed to locate repository root" >&2
  exit 1
}

# The fast/full mode split was removed: integration tests are mandatory before
# push. Tolerate (and ignore) a leftover argument so old `verify.sh fast|full`
# invocations and muscle memory still run the one real gate.
if [ "$#" -gt 0 ]; then
  echo "verify.sh: mode argument '$1' ignored — verify.sh always runs the full gate (integration tests included)." >&2
fi

# ── What changed? (mirror CI's "Detect changes"; run everything if unknown) ──
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
  changed '^(src/|tests/|Directory\.|Axis\.sln$|global\.json$)' && DOTNET=true
  changed '^frontend/' && FE=true
fi

# A change under Axis.Api/Endpoints that does NOT also touch openapi.json is the
# single most common late CI failure in this repo: OpenApiDocumentTests drift,
# because the OpenAPI contract (and the frontend types generated from it) was not
# regenerated. The contract is generated, never hand-edited — see
# repo-layout-discovery.md. Surface it here so it is caught before CI, not after.
API_SURFACE_DRIFT=false
if changed '^src/Axis\.Api/Endpoints/' && ! changed '^openapi\.json$'; then
  API_SURFACE_DRIFT=true
fi

FAILED=()
step() {
  local name="$1" cmd="$2"
  echo ""
  echo "▶ ${name}"
  if bash -c "${cmd}"; then
    echo "✓ ${name}"
  else
    echo "✗ ${name} — FAILED"
    FAILED+=("${name}")
  fi
}

echo "verify.sh — .NET=${DOTNET} frontend=${FE}"

# The .NET test step runs the Testcontainers integration tests, which need a
# running Docker daemon. When backend files changed, fail early with an
# actionable message instead of a cryptic Testcontainers error mid-run.
# (Doc-only / frontend-only pushes set DOTNET=false and never reach this — so
# they still do not require Docker.)
if [ "${DOTNET}" = true ]; then
  if ! docker info >/dev/null 2>&1; then
    echo "" >&2
    echo "✗ Docker is not available, but backend changed and the gate runs the Testcontainers integration tests." >&2
    echo "  Integration tests are REQUIRED before pushing backend changes (Gate 1 — local = CI)." >&2
    echo "  Start Docker and re-run 'scripts/verify.sh'." >&2
    exit 1
  fi
fi

if [ "${DOTNET}" = true ]; then
  step ".NET build"   "dotnet build Axis.sln --nologo"
  step ".NET format"  "dotnet format Axis.sln --verify-no-changes"
  step ".NET test (full, Testcontainers)" "dotnet test Axis.sln --nologo"
fi

if [ "${FE}" = true ]; then
  step "frontend ci (tsc + biome)" "cd frontend && npm run ci"
  step "frontend test"             "cd frontend && npm run test"
fi

# Doc drift self-skips when there is no relevant diff, so it is always safe to run.
step "doc drift" "./scripts/check-doc-drift.sh"

# Non-blocking: a real contract change can only be confirmed by OpenApiDocumentTests
# (needs Docker), so this is a reminder, not a gate. The hard gate stays in CI.
if [ "${API_SURFACE_DRIFT}" = true ]; then
  echo ""
  echo "⚠ API surface changed (src/Axis.Api/Endpoints/) but openapi.json is unchanged."
  echo "  If you added or changed a route / request / response shape, regenerate the contract:"
  echo "    dotnet test tests/Api/Axis.Api.Tests --filter OpenApiDocumentTests   # rewrites openapi.json on drift (Docker)"
  echo "    (cd frontend && npm run gen:api-types)                               # refresh generated types"
  echo "  then commit openapi.json + api-types.ts — CI's OpenApiDocumentTests fails otherwise."
fi

echo ""
if [ "${#FAILED[@]}" -eq 0 ]; then
  echo "verify.sh: PASS"
  exit 0
fi

echo "verify.sh: FAIL — ${#FAILED[@]} step(s): ${FAILED[*]}" >&2
exit 1
