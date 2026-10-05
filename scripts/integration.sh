#!/usr/bin/env bash
# Integration tests against real PostgreSQL containers (requires Docker).
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

run_suite Axis.Integration.Tests tests/Axis.Integration.Tests dotnet test --project tests/Axis.Integration.Tests -c Release \
  --report-xunit-junit --report-xunit-junit-filename Axis.Integration.Tests.xml --results-directory "$results_dir"

finish_report
