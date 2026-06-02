#!/usr/bin/env bash
# Run unit test projects only, discovered from committed project naming.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

./scripts/check-test-project-classification.sh

mapfile -t projects < <(
  git ls-files 'tests/**/*.csproj' |
    grep -E '/Axis\..*\.(Domain|Application)\.Tests/Axis\..*\.(Domain|Application)\.Tests\.csproj$'
)

if [ "${#projects[@]}" -eq 0 ]; then
  echo "test-unit: no unit test projects found" >&2
  exit 1
fi

for project in "${projects[@]}"; do
  echo ""
  echo "> dotnet test ${project}"
  dotnet test "${project}" --nologo "$@"
done
