#!/usr/bin/env bash
# Fails when production code changes without matching doc updates, or new handlers lack tests.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

BASE="${BASE_BRANCH:-main}"
if git rev-parse --verify "origin/${BASE}" >/dev/null 2>&1; then
  RANGE="origin/${BASE}...HEAD"
elif git rev-parse --verify "${BASE}" >/dev/null 2>&1; then
  RANGE="${BASE}...HEAD"
else
  RANGE="HEAD~1...HEAD"
fi

CHANGED="$(git diff --name-only "${RANGE}" 2>/dev/null || true)"
if [ -z "${CHANGED}" ]; then
  echo "check-doc-drift: no diff in ${RANGE} — skip"
  exit 0
fi

ERR=0
fail() {
  echo "check-doc-drift FAIL: $1" >&2
  ERR=1
}

any_changed() {
  echo "${CHANGED}" | grep -qE "$1"
}

docs_changed_under() {
  echo "${CHANGED}" | grep -q "^$1"
}

check_epic_docs() {
  local code_pattern="$1"
  local doc_prefix="$2"
  local label="$3"
  if any_changed "${code_pattern}"; then
    if ! docs_changed_under "${doc_prefix}"; then
      fail "${label}: code changed but no files under ${doc_prefix}/ in this PR"
    fi
  fi
}

check_epic_docs 'src/Axis\.Api/Endpoints/Execution' 'docs/epics/E06-workflow-engine' 'E06 WorkflowEngine API'
check_epic_docs 'src/Modules/WorkflowEngine/' 'docs/epics/E06-workflow-engine' 'E06 WorkflowEngine module'
check_epic_docs 'src/Axis\.Api/Endpoints/Model' 'docs/epics/E03-data-modeling' 'E03 DataModeling API'
check_epic_docs 'src/Modules/DataModeling/' 'docs/epics/E03-data-modeling' 'E03 DataModeling module'
check_epic_docs 'src/Axis\.Api/Endpoints/Workflow' 'docs/epics/E04-workflow-builder' 'E04 WorkflowBuilder API'
check_epic_docs 'src/Modules/WorkflowBuilder/' 'docs/epics/E04-workflow-builder' 'E04 WorkflowBuilder module'
check_epic_docs 'src/Axis\.Api/Endpoints/Form' 'docs/epics/E05-form-builder' 'E05 FormBuilder API'
check_epic_docs 'src/Modules/FormBuilder/' 'docs/epics/E05-form-builder' 'E05 FormBuilder module'
check_epic_docs 'src/Modules/.*/.*OrganizationVerifiedHandler' 'docs/epics/E01-platform-foundation' 'E01 tenant provisioning'
check_epic_docs 'frontend/src/(features/auth|routes/|components/layout/AppShell)' 'docs/epics/E02-identity-access' 'E02 auth frontend'

if any_changed '^frontend/src/'; then
  if ! docs_changed_under 'docs/epics/'; then
    fail "frontend/src/ changed but no files under docs/epics/ in this PR"
  fi
fi

if any_changed '^src/' && docs_changed_under 'docs/PROGRESS.md'; then
  if ! echo "${CHANGED}" | grep -q '^docs/epics/'; then
    fail "docs/PROGRESS.md updated but no docs/epics/ change while src/ changed"
  fi
fi

# Sync-over-async guard: `GetAwaiter().GetResult()` blocks the thread on a Task
# and is a classic deadlock recipe under any sync-context (ASP.NET classic,
# WinForms). We flag it everywhere in src/ — Wolverine and Minimal API are both
# fully async; there is no legitimate need. (`.Result` and `.Wait()` are not
# grep-banned here because both names are used by domain types in this codebase;
# the Roslyn analyzer in PR #98 will catch the Task-typed versions with type
# info.)
SYNC_OVER_ASYNC_PATTERN='GetAwaiter\(\)\.GetResult\(\)'
while IFS= read -r added; do
  [ -z "${added}" ] && continue
  fail "Sync-over-async (.GetAwaiter().GetResult()) introduced — await the Task instead: ${added}"
done < <(
  git diff --unified=0 "${RANGE}" -- 'src/*.cs' 2>/dev/null \
    | awk -v pat="${SYNC_OVER_ASYNC_PATTERN}" '
        /^\+\+\+ b\// { file = substr($0, 7); next }
        /^\+[^+]/ && $0 ~ pat { print file ": " substr($0, 2) }
      '
)

# Hardcoded connection-string guard. Connection strings belong in
# appsettings.json / env vars / Vault — never inline in C#. Pattern matches
# the three most common forms (Postgres "Host=", SQL Server "Server=" and
# "Data Source="). Scope: src/*.cs only, since appsettings.json is the
# right home for these values.
CONNSTRING_PATTERN='"(Host=|Server=|Data Source=)'
while IFS= read -r added; do
  [ -z "${added}" ] && continue
  fail "Hardcoded connection string — move to appsettings.json / env / Vault: ${added}"
done < <(
  git diff --unified=0 "${RANGE}" -- 'src/*.cs' 2>/dev/null \
    | awk -v pat="${CONNSTRING_PATTERN}" '
        /^\+\+\+ b\// { file = substr($0, 7); next }
        /^\+[^+]/ && $0 ~ pat { print file ": " substr($0, 2) }
      '
)

# DateTime.Now guard. Server-side code must use DateTime.UtcNow (or a
# clock abstraction in tests) so timestamps are timezone-independent and
# round-trip safely through Postgres `timestamptz`. DateTime.Now silently
# bakes the host's local TZ into stored values — a classic prod-vs-CI bug.
DATETIME_NOW_PATTERN='\bDateTime\.Now\b'
while IFS= read -r added; do
  [ -z "${added}" ] && continue
  fail "DateTime.Now introduced — use DateTime.UtcNow (TZ-dependent values poison Postgres timestamptz): ${added}"
done < <(
  git diff --unified=0 "${RANGE}" -- 'src/*.cs' 'tests/*.cs' 2>/dev/null \
    | awk -v pat="${DATETIME_NOW_PATTERN}" '
        /^\+\+\+ b\// { file = substr($0, 7); next }
        /^\+[^+]/ && $0 ~ pat { print file ": " substr($0, 2) }
      '
)

# Cross-module raw-SQL guard (P0 in CLAUDE.md § Module boundaries).
# Flag *newly introduced* raw-SQL calls in module code so review confirms the
# SQL only touches that module's own tables. Use Wolverine events for any
# cross-module data needs (see patterns.md § Cross-module data pattern).
RAW_SQL_PATTERN='SqlQueryRaw|ExecuteSqlRaw|FromSqlRaw|ExecuteSqlInterpolated|FromSqlInterpolated'
while IFS= read -r added; do
  [ -z "${added}" ] && continue
  fail "New raw-SQL call in module code — confirm same-module tables only: ${added}"
done < <(
  git diff --unified=0 "${RANGE}" -- 'src/Modules/*.cs' 2>/dev/null \
    | awk -v pat="${RAW_SQL_PATTERN}" '
        /^\+\+\+ b\// { file = substr($0, 7); next }
        /^\+[^+]/ && $0 ~ pat { print file ": " substr($0, 2) }
      '
)

# New OR renamed handler must have a matching test file.
# Status A = added; R### = renamed (git emits `R100\tOLD\tNEW`).
while IFS= read -r handler; do
  [ -z "${handler}" ] && continue
  module="$(echo "${handler}" | sed -n 's|src/Modules/\([^/]*\)/.*|\1|p')"
  handler_name="$(basename "${handler}" .cs)"
  if echo "${handler}" | grep -q '/Commands/'; then
    subdir="Commands"
  else
    subdir="Queries"
  fi
  test_file="tests/Modules/${module}/Axis.${module}.Application.Tests/${subdir}/${handler_name}Tests.cs"

  if [ ! -f "${test_file}" ]; then
    fail "Handler ${handler} — create ${test_file}"
  fi
done < <(
  git diff --name-status "${RANGE}" 2>/dev/null | awk '
    ($1 == "A" || $1 ~ /^R/) {
      path = ($1 ~ /^R/) ? $3 : $2
      if (path ~ /^src\/Modules\/.*\/(Commands|Queries)\/.*Handler\.cs$/) print path
    }
  '
)

check_readme_api() {
  local code_pattern="$1"
  local epic_dir="$2"
  local readme="${ROOT}/${epic_dir}/README.md"
  if any_changed "${code_pattern}" && [ -f "${readme}" ]; then
    if grep -qE '\| API \| ⏳' "${readme}"; then
      fail "${epic_dir}/README.md still '| API | ⏳' — set ⚠️ or ✅"
    fi
  fi
}

# P2 hygiene: no new TODO / FIXME / NotImplementedException / placeholder / stub
# in production or test code. Scans *added* lines only — existing markers don't
# break unrelated PRs. Run from any shell (CI or local Git Bash on Windows).
TODO_PATTERN='TODO|FIXME|NotImplementedException|placeholder|stub'
while IFS= read -r added; do
  [ -z "${added}" ] && continue
  fail "New TODO/FIXME/stub marker introduced — resolve or open an issue: ${added}"
done < <(
  git diff --unified=0 "${RANGE}" \
    -- 'src/*' 'tests/*' 'frontend/src/*' \
    ':(exclude)**/obj/**' ':(exclude)**/node_modules/**' \
    2>/dev/null \
    | awk -v pat="${TODO_PATTERN}" '
        /^\+\+\+ b\// { file = substr($0, 7); next }
        /^\+[^+]/ && $0 ~ pat { print file ": " substr($0, 2) }
      '
)

check_readme_api 'src/Axis\.Api/Endpoints/Execution' 'docs/epics/E06-workflow-engine'
check_readme_api 'src/Axis\.Api/Endpoints/Form' 'docs/epics/E05-form-builder'
check_readme_api 'src/Axis\.Api/Endpoints/Model' 'docs/epics/E03-data-modeling'
check_readme_api 'src/Axis\.Api/Endpoints/Workflow' 'docs/epics/E04-workflow-builder'

# WORKAROUND comment ↔ inventory cross-check (docs/WORKAROUNDS.md).
#   Each `// WORKAROUND: see docs/WORKAROUNDS.md#<slug>` comment in production
#   code must reference a real section in WORKAROUNDS.md. New violations of
#   architectural rules should always be documented — silent shortcuts become
#   permanent debt (see docs/WORKAROUNDS.md for the rationale).
WORKAROUNDS_FILE="docs/WORKAROUNDS.md"
if [ -f "${WORKAROUNDS_FILE}" ]; then
  # Extract H3 slugs from WORKAROUNDS.md (### my-slug → my-slug). Lowercased.
  known_slugs="$(awk '
    /^### / {
      sub(/^### /, "")
      gsub(/[^A-Za-z0-9-]/, "")
      print tolower($0)
    }' "${WORKAROUNDS_FILE}")"

  # Find WORKAROUND: comments referencing docs/WORKAROUNDS.md#slug.
  # Match across src/ and tests/, .cs and .ts/.tsx and .md.
  while IFS= read -r match; do
    [ -z "${match}" ] && continue
    file="$(echo "${match}" | cut -d: -f1)"
    referenced="$(echo "${match}" \
      | grep -oE 'docs/WORKAROUNDS\.md#[A-Za-z0-9-]+' \
      | head -n1 \
      | sed 's|.*#||' \
      | tr '[:upper:]' '[:lower:]')"
    [ -z "${referenced}" ] && continue
    if ! echo "${known_slugs}" | grep -qx "${referenced}"; then
      fail "WORKAROUND comment references unknown slug '${referenced}' — add a section to ${WORKAROUNDS_FILE} (or fix the slug): ${file}"
    fi
  done < <(
    grep -rnE 'WORKAROUND:.*docs/WORKAROUNDS\.md#' \
      src/ tests/ frontend/src/ 2>/dev/null \
      || true
  )

  # Also flag WORKAROUND comments without the WORKAROUNDS.md link — they're
  # invisible to the inventory.
  while IFS= read -r match; do
    [ -z "${match}" ] && continue
    fail "WORKAROUND comment without docs/WORKAROUNDS.md reference — add link or rephrase: ${match}"
  done < <(
    grep -rnE 'WORKAROUND:' src/ tests/ frontend/src/ 2>/dev/null \
      | grep -v 'docs/WORKAROUNDS\.md#' \
      || true
  )
fi

# Speculation guard: reference docs (ARCHITECTURE) must describe what exists,
# not what is "planned" or "will be wired". Forward-looking status belongs in
# docs/PROGRESS.md or an epic feature file — places readers know are
# forward-looking. See docs/playbooks/docs-style.md § Anti-patterns.
SPEC_PATTERN='Not yet|\bplanned\b|Will be|To be implemented|Coming soon|in the future'
SPEC_TARGETS=(
  'docs/ARCHITECTURE.md'
)
for target in "${SPEC_TARGETS[@]}"; do
  [ -f "${target}" ] || continue
  if matches="$(grep -nE "${SPEC_PATTERN}" "${target}" 2>/dev/null)"; then
    while IFS= read -r line; do
      fail "Speculation in reference doc — move to docs/PROGRESS.md or an epic feature file: ${target}:${line}"
    done <<< "${matches}"
  fi
done

if [ "${ERR}" -ne 0 ]; then
  echo "" >&2
  echo "See docs/playbooks/agent-checklist.md" >&2
  exit 1
fi

echo "check-doc-drift: OK (${RANGE})"
