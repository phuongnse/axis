#!/usr/bin/env bash
# Integration tests against real PostgreSQL and Keycloak containers, and the smoke test of
# scripts/dev.sh (requires Docker with the compose plugin, and network access for the first SPA
# install).
# The integration tests also run the test scenarios of every application under samples/apps/ and of
# every sample template under samples/templates/.
# Each application's scenarios are a suite named scenarios-NAME, written by that test.
# The smoke test is skipped when $NEXKIT_BASE_SHA shows that nothing it covers changed.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

for manifest in samples/apps/*/application.json; do
  name="$(node -p "JSON.parse(require('fs').readFileSync('$manifest','utf8')).name")"
  expect_suite "scenarios-$name" .
done

for manifest in samples/templates/*/application.json; do
  name="$(node -p "JSON.parse(require('fs').readFileSync('$manifest','utf8')).name")"
  expect_suite "scenarios-$name" .
done

run_suite Axis.Integration.Tests tests/Axis.Integration.Tests dotnet test --project tests/Axis.Integration.Tests -c Release \
  --report-xunit-junit --report-xunit-junit-filename Axis.Integration.Tests.xml --results-directory "$results_dir"

run_suite_if_affected dev-script . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/dev-script.xml" scripts/dev.test.mjs

finish_report
