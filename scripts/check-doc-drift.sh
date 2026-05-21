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
check_epic_docs 'src/Axis\.Api/Infrastructure/TenantSchema' 'docs/epics/E01-platform-foundation' 'E01 tenant provisioning'
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
    fail "New handler ${handler} — create ${test_file}"
  fi
done < <(git diff --name-status "${RANGE}" 2>/dev/null | awk '$1 == "A" && /src\/Modules\/.*\/(Commands|Queries)\/.*Handler\.cs$/ { print $2 }')

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

check_readme_api 'src/Axis\.Api/Endpoints/Execution' 'docs/epics/E06-workflow-engine'
check_readme_api 'src/Axis\.Api/Endpoints/Form' 'docs/epics/E05-form-builder'
check_readme_api 'src/Axis\.Api/Endpoints/Model' 'docs/epics/E03-data-modeling'
check_readme_api 'src/Axis\.Api/Endpoints/Workflow' 'docs/epics/E04-workflow-builder'

if [ "${ERR}" -ne 0 ]; then
  echo "" >&2
  echo "See docs/playbooks/agent-checklist.md" >&2
  exit 1
fi

echo "check-doc-drift: OK (${RANGE})"
