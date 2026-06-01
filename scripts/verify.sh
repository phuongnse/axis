#!/usr/bin/env bash
# Local mirror of the CI gate (.github/workflows/build-and-test.yml).
# Closes the "build passed locally but CI failed" gap: runs the SAME commands CI
# runs, so charset/format, integration, casing, and drift surface here, not in CI.
#
# Usage: scripts/verify.sh [fast|full]   (default: fast)
#   fast — build + dotnet format --verify + frontend ci+test + doc drift   (no Docker)
#   full — fast scope + full `dotnet test` (Testcontainers; needs Docker), exactly like CI
#
# Only the layers whose files changed (vs origin/main) run, so doc-only work stays quick.
# On Windows run via Git Bash. Wired as the pre-push hook (scripts/hooks/pre-push).
set -uo pipefail
cd "$(dirname "$0")/.." || {
  echo "verify.sh: failed to locate repository root" >&2
  exit 1
}

MODE="${1:-fast}"
case "${MODE}" in
  fast|full) ;;
  *)
    echo "verify.sh: unknown mode '${MODE}' — usage: scripts/verify.sh [fast|full]" >&2
    exit 2
    ;;
esac

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

echo "verify.sh (${MODE}) — .NET=${DOTNET} frontend=${FE}"

if [ "${DOTNET}" = true ]; then
  step ".NET build"   "dotnet build Axis.sln --nologo"
  step ".NET format"  "dotnet format Axis.sln --verify-no-changes"
  if [ "${MODE}" = full ]; then
    step ".NET test (full, Testcontainers)" "dotnet test Axis.sln --nologo"
  fi
fi

if [ "${FE}" = true ]; then
  step "frontend ci (tsc + biome)" "cd frontend && npm run ci"
  step "frontend test"             "cd frontend && npm run test"
fi

# Doc drift self-skips when there is no relevant diff, so it is always safe to run.
step "doc drift" "./scripts/check-doc-drift.sh"

echo ""
if [ "${#FAILED[@]}" -eq 0 ]; then
  echo "verify.sh: PASS (${MODE})"
  [ "${MODE}" != full ] && [ "${DOTNET}" = true ] && \
    echo "note: integration tests (Testcontainers) run in CI — run 'scripts/verify.sh full' to mirror them locally (needs Docker)."
  exit 0
fi

echo "verify.sh: FAIL — ${#FAILED[@]} step(s): ${FAILED[*]}" >&2
exit 1
