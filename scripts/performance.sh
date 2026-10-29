#!/usr/bin/env bash
# The performance tests of the integration tests, whose classes end in PerformanceTests, against real
# PostgreSQL and Keycloak containers (requires Docker). They run alone, one class at a time, so their
# time limits measure Axis and not other tests. scripts/integration.sh leaves them out, and the
# performance workflow runs this script on every push to main. It is not a NexKit check.
# Arguments go to dotnet test in place of the class filter, for example
# --filter-class '*SearchPerformanceTests'.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

# The tests' Keycloak loads the Axis theme from the test output, which copies artifacts/keycloak-theme.
npm run build:keycloak-theme --prefix web

filter=("$@")
[ "$#" -gt 0 ] || filter=(--filter-class '*PerformanceTests')
run_suite performance tests/Axis.Integration.Tests dotnet test --project tests/Axis.Integration.Tests -c Release \
  --report-xunit-junit --report-xunit-junit-filename performance.xml --results-directory "$results_dir" "${filter[@]}"
finish_report
