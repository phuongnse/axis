#!/usr/bin/env bash
# Integration tests against real PostgreSQL containers, and the smoke test of scripts/dev.sh
# (requires Docker with the compose plugin, and network access for the first SPA install).
# The smoke test is skipped when $NEXKIT_BASE_SHA shows that nothing it covers changed.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

run_suite Axis.Integration.Tests tests/Axis.Integration.Tests dotnet test --project tests/Axis.Integration.Tests -c Release \
  --report-xunit-junit --report-xunit-junit-filename Axis.Integration.Tests.xml --results-directory "$results_dir"

run_suite_if_affected dev-script . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/dev-script.xml" scripts/dev.test.mjs

finish_report
