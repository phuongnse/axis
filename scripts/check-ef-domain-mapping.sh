#!/usr/bin/env bash
# Guards DDD persistence mappings from hiding domain relationships behind EF
# private-field queries or primitive id arrays.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

ERR=0
fail() {
  echo "check-ef-domain-mapping FAIL: $1" >&2
  ERR=1
}

scan() {
  local pattern="$1"
  if command -v rg >/dev/null 2>&1 && rg --version >/dev/null 2>&1; then
    rg -n "${pattern}" src tests \
      --glob '*.cs' \
      --glob '!**/bin/**' \
      --glob '!**/obj/**' \
      --glob '!**/Generated/**' \
      || true
    return
  fi

  find src tests \
    \( -path '*/bin/*' -o -path '*/obj/*' -o -path '*/Generated/*' \) -prune \
    -o -name '*.cs' -type f -print0 \
    | xargs -0 grep -nE "${pattern}" 2>/dev/null \
    || true
}

while IFS= read -r hit; do
  [ -z "${hit}" ] && continue
  fail "EF.Property query against private/shadow field hides a persistence concern behind a magic string. Model the relationship explicitly: ${hit}"
done < <(
  scan 'EF[.]Property<[^>]+>[[:space:]]*[(][^,]+,[[:space:]]*"_[A-Za-z0-9]+'
)

while IFS= read -r hit; do
  [ -z "${hit}" ] && continue
  fail "PrimitiveCollection<List<Guid>> stores relationship ids as an array. Use an entity/join table instead: ${hit}"
done < <(
  scan 'PrimitiveCollection<List<Guid>>'
)

if [ "${ERR}" -ne 0 ]; then
  echo "" >&2
  echo "See docs/playbooks/patterns.md#ef-core-aggregate-mapping-patterns" >&2
  exit 1
fi

echo "check-ef-domain-mapping: OK"
