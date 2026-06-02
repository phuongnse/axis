#!/usr/bin/env bash
# Ensure every committed test project is covered by the Unit/Integration/Architecture naming convention.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

failed=false

while IFS= read -r project; do
  name="$(basename "${project}" .csproj)"

  case "${name}" in
    Axis.*.Domain.Tests|Axis.*.Application.Tests)
      ;;
    Axis.*.Infrastructure.Tests|Axis.Api.Tests)
      ;;
    Axis.Architecture.Tests)
      ;;
    Axis.Testing)
      ;;
    *.Tests)
      echo "check-test-project-classification: ${project} is not classified" >&2
      failed=true
      ;;
    *)
      echo "check-test-project-classification: ${project} is not classified" >&2
      failed=true
      ;;
  esac
done < <(git ls-files 'tests/**/*.csproj')

if [ "${failed}" = true ]; then
  echo "check-test-project-classification: FAIL - rename the project to match the Unit/Integration/Architecture convention or update this guard" >&2
  exit 1
fi

echo "check-test-project-classification: OK"
