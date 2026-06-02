#!/usr/bin/env bash
# Fails when production code changes without matching doc updates, or new handlers lack tests.
#
# Adding a new regex check? Read first:
#   docs/playbooks/patterns.md#drift-regex-constraints
#
# GNU awk silently degrades `\.`, `\(`, `\)`, `\b` — use POSIX bracket
# expressions (`[.]`, `[(]`, etc.) for literal punctuation. The doc above
# explains why and walks through the failure mode of the wrong syntax.
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

# Generated frontend artifacts carry no authored behavior — the contract lives
# in the backend (which has its own doc rules) and these files are emitted by a
# tool, not written by hand. A PR that only regenerates them needs no use-case
# doc. Keep this list TIGHT: only truly machine-generated files, or the rule
# below stops protecting hand-written feature code.
#   - frontend/src/lib/api-types.ts  ← openapi-typescript output (npm run gen:api-types)
FRONTEND_GENERATED='^frontend/src/lib/api-types\.ts$'

# True when frontend/src/ has at least one hand-authored change (i.e. a change
# that is not purely a generated artifact).
frontend_authored_changed() {
  echo "${CHANGED}" | grep -E '^frontend/src/' | grep -qvE "${FRONTEND_GENERATED}"
}

# Module + API endpoint → use-case domain rules are discovered from the tree
# (see scripts/doc_drift_domains.py). Only cross-cutting paths stay in EXTRA_* there.
python3 "${ROOT}/scripts/doc_drift_domains.py" --validate || ERR=1

if [ -z "${CHANGED}" ]; then
  if [ "${ERR}" -eq 0 ]; then
    echo "check-doc-drift: no diff in ${RANGE} — skip"
  fi
  exit "${ERR}"
fi

echo "${CHANGED}" | python3 "${ROOT}/scripts/doc_drift_domains.py" --check || ERR=1

if frontend_authored_changed; then
  if ! docs_changed_under 'docs/use-cases/'; then
    fail "frontend/src/ changed but no files under docs/use-cases/ in this PR"
  fi
fi

if any_changed '^src/' && docs_changed_under 'docs/PROGRESS.md'; then
  if ! echo "${CHANGED}" | grep -q '^docs/use-cases/'; then
    fail "docs/PROGRESS.md updated but no docs/use-cases/ change while src/ changed"
  fi
fi

# Sync-over-async guard: `GetAwaiter().GetResult()` blocks the thread on a Task
# and is a classic deadlock recipe under any sync-context (ASP.NET classic,
# WinForms). We flag it everywhere in src/ — Wolverine and Minimal API are both
# fully async; there is no legitimate need. (`.Result` and `.Wait()` are not
# grep-banned here because both names are used by domain types in this codebase;
# a type-aware Roslyn analyzer catches the Task-typed versions instead.)
#
# `[(]` `[)]` `[.]` instead of `\(` `\)` `\.` to escape literal punctuation:
# GNU awk warns on those backslash-escapes and silently treats them as plain
# chars, which would make `\(\)` an empty group matching anywhere and break
# the check.
SYNC_OVER_ASYNC_PATTERN='GetAwaiter[(][)][.]GetResult[(][)]'
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
#
# `[.]` instead of `\.` for the literal dot: GNU awk warns on `\.` and
# silently treats it as any-char, which would broaden matches. We do NOT
# use a word boundary (GNU awk lacks `\b`) — false positives from a
# custom `MyDateTime.Now` are vanishingly unlikely and not worth the
# detection cost; add a more specific guard if one shows up.
#
# DateTimeOffset.Now is NOT flagged here: its return type preserves the
# offset, so Postgres `timestamptz` round-trips cleanly even from local
# time. Consistency with `DateTimeOffset.UtcNow` is preferred but is a
# style call, not a correctness one.
DATETIME_NOW_PATTERN='DateTime[.]Now'
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

# Contract-less endpoint response guard (review-findings-ledger.md: "Endpoint
# returns object/anonymous JSON instead of an Application-layer DTO").
# `.Produces<object>()` emits a bare `object` schema into openapi.json, which
# makes the generated frontend types (api-types.ts) useless — it defeats the
# FE/BE type-safety codegen the repo closed in #165. `Results.Ok(new { … })`
# returns an anonymous type that is not a real contract. Both must be a named
# Application-layer DTO (e.g. `.Produces<CreateModelResponse>(201)`).
#
# Added-lines-only, so this is a ratchet: existing violations are grandfathered
# and burned down separately; NO NEW one may be introduced. Scope: endpoints.
#
# `[.]` `[(]` `[{]` for literal punctuation (GNU awk mangles `\.`/`\(`); `<object>`
# is literal in ERE.
ENDPOINT_OBJECT_PATTERN='[.]Produces<object>|Results[.](Ok|Json|Created|Accepted)[(]new[ ]*[{]'
while IFS= read -r added; do
  [ -z "${added}" ] && continue
  fail "Endpoint returns object/anonymous JSON — use a named Application-layer DTO (review-findings-ledger.md): ${added}"
done < <(
  git diff --unified=0 "${RANGE}" -- 'src/Axis.Api/Endpoints/*.cs' 2>/dev/null \
    | awk -v pat="${ENDPOINT_OBJECT_PATTERN}" '
        /^\+\+\+ b\// { file = substr($0, 7); next }
        /^\+[^+]/ && $0 ~ pat { print file ": " substr($0, 2) }
      '
)

# Endpoint orchestration guard (review-findings-ledger.md): a Minimal-API
# endpoint handler must not call the mediator more than once — multiple sends
# mean orchestration logic leaked into the endpoint (combine into one
# command/handler or a saga). Full-state scan of endpoint handler methods
# (those returning Task<IResult>); baseline is zero, so any new occurrence
# fails. `.Send(`/`.Publish(` inside the Map* registration method (which returns
# IEndpointRouteBuilder, not Task<IResult>) is never counted. Limit:
# inline-lambda endpoints are not covered — the repo uses named handler methods.
#
# `[.]` `[(]` for literal punctuation (GNU awk mangles `\.`/`\(`).
while IFS= read -r hit; do
  [ -z "${hit}" ] && continue
  fail "Endpoint handler calls the mediator more than once — move orchestration into a single command/handler or saga (review-findings-ledger.md): ${hit}"
done < <(
  for ep in src/Axis.Api/Endpoints/*.cs; do
    [ -f "${ep}" ] || continue
    awk -v file="${ep}" '
      /Task<IResult>/ {
        if (method != "" && cnt > 1) print file " :: " method " (" cnt " mediator calls)"
        sig = $0; sub(/^[ \t]+/, "", sig); method = sig; cnt = 0; next
      }
      /[.]Send[(]|[.]Publish[(]/ { cnt++ }
      END { if (method != "" && cnt > 1) print file " :: " method " (" cnt " mediator calls)" }
    ' "${ep}"
  done
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
# docs/PROGRESS.md or a use-case file — places readers know are
# forward-looking. See docs/playbooks/docs-style.md § Anti-patterns.
SPEC_PATTERN='Not yet|\bplanned\b|Will be|To be implemented|Coming soon|in the future'
SPEC_TARGETS=(
  'docs/ARCHITECTURE.md'
)
for target in "${SPEC_TARGETS[@]}"; do
  [ -f "${target}" ] || continue
  if matches="$(grep -nE "${SPEC_PATTERN}" "${target}" 2>/dev/null)"; then
    while IFS= read -r line; do
      fail "Speculation in reference doc — move to docs/PROGRESS.md or a use-case file: ${target}:${line}"
    done <<< "${matches}"
  fi
done

# Stale terminology / artifact guard. The Epic→Use-case migration replaced
# specific phrases; flagging them keeps the doc tree convergent.
#   - "feature file"           → "use-case file" (CLAUDE.md, PR template were stale)
#   - "see gaps below"         → migration-script artifact, meaningless text
#   - "> **Wireframe**:"       → old callout style; use the `## Wireframes` table
#   - "docs/epics/"            → old folder, removed
#   - "_template-feature-us"   → old template name, replaced by USE_CASE_TEMPLATE.md
# Scope: docs/, .github/, CLAUDE.md, CONTRIBUTING.md, README.md.
STALE_TERM_PATTERN='feature file|see gaps below|^> \*\*Wireframe\*\*:|docs/epics/|_template-feature-us|\| Diagram \| Source \| Preview \|'
STALE_TERM_FILES="$(find docs .github -type f -name '*.md' 2>/dev/null) CLAUDE.md CONTRIBUTING.md README.md"
for target in ${STALE_TERM_FILES}; do
  [ -f "${target}" ] || continue
  if matches="$(grep -nE "${STALE_TERM_PATTERN}" "${target}" 2>/dev/null)"; then
    while IFS= read -r line; do
      fail "Stale terminology in ${target}: ${line} (Epic→Use-case migration — see docs/use-cases/README.md)"
    done <<< "${matches}"
  fi
done

# Incident/lesson framing guard: practice/reference docs state the general rule;
# incident specifics belong in the use-case file, PROGRESS.md, or the PR retro
# (docs/playbooks/docs-style.md § Keep practice docs general; agent-checklist
# Gate 3 "Incident-level detail in rule text?"). Deliberately narrow — flags the
# recurring "Lesson (...)" / "**Lesson" callout class, not every over-fit example.
# Scope: playbooks + CLAUDE.md + ARCHITECTURE.md.
LESSON_PATTERN='\*\*Lesson|[Ll]esson \(|[Ll]esson\)'
LESSON_FILES="$(find docs/playbooks -type f -name '*.md' 2>/dev/null) CLAUDE.md docs/ARCHITECTURE.md"
for target in ${LESSON_FILES}; do
  [ -f "${target}" ] || continue
  if matches="$(grep -nE "${LESSON_PATTERN}" "${target}" 2>/dev/null)"; then
    while IFS= read -r line; do
      fail "Incident/lesson framing in practice doc — generalize the rule, move specifics to the use-case/PROGRESS/retro (docs-style.md § Keep practice docs general): ${target}:${line}"
    done <<< "${matches}"
  fi
done

# EF migration pairs: every non-snapshot Migration .cs must have a .Designer.cs
# (hand-written migrations often forget the Designer and never apply in MigrateAsync).
while IFS= read -r migration; do
  [ -z "${migration}" ] && continue
  designer="${migration%.cs}.Designer.cs"
  if [ ! -f "${designer}" ]; then
    fail "EF migration missing .Designer.cs — regenerate with dotnet ef: ${migration}"
  fi
done < <(
  find "${ROOT}/src/Modules" -path '*/Migrations/*.cs' \
    ! -name '*Snapshot*' \
    ! -name '*.Designer.cs' 2>/dev/null \
    || true
)

"${ROOT}/scripts/check-buf-modules.sh" || ERR=1

# Use-case layout validation (flow, AC, wireframes/diagrams, status).
python3 "${ROOT}/scripts/check-use-case-docs.py" --check || ERR=1

# Relative link / image target resolution across docs/, .github/, repo-root *.md.
# Catches the broken `![alt](./missing.svg)` class lychee misses.
python3 "${ROOT}/scripts/check-doc-link-targets.py" --check || ERR=1

# Code-fence indentation integrity. Catches the collapsed-indentation class a
# bulk find-replace introduces — invisible to lychee, prettier, and the
# structural doc checks (see the script header).
python3 "${ROOT}/scripts/check-doc-code-fences.py" --check || ERR=1


python3 "${ROOT}/scripts/check-local-dev-docs.py" --check || ERR=1

# Repo-layout discovery guards (scripts/axis_repo.py). Fail when generated
# indexes or config lists drift from the tree — run the fix command in each script's output.
python3 "${ROOT}/scripts/sync_buf_yaml.py" --check || ERR=1
python3 "${ROOT}/scripts/check_kafka_wiring.py" --check || ERR=1
python3 "${ROOT}/scripts/regenerate-domain-readme-index.py" --check || ERR=1

if any_changed '^docker-compose\.yml$'; then
  if ! docs_changed_under 'docs/playbooks/local-dev.md'; then
    fail "docker-compose.yml changed but docs/playbooks/local-dev.md not updated in this PR"
  fi
fi

if [ "${ERR}" -ne 0 ]; then
  echo "" >&2
  echo "See docs/playbooks/agent-checklist.md" >&2
  exit 1
fi

echo "check-doc-drift: OK (${RANGE})"
