#!/usr/bin/env bash
# Unit tests: no Docker or external services needed. The shards: docs/development.md#shards
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh
source scripts/lib/tools.sh

shard_variable=AXIS_TEST_SHARD
shard="${AXIS_TEST_SHARD:-}"
if [ -z "$shard" ] && [ -n "${NEXKIT_SHARD:-}" ]; then
  shard_variable=NEXKIT_SHARD
  shard="$NEXKIT_SHARD"
fi
run_rest=1
run_vitest=1
web_suite=web
vitest_files=()
if [ -n "$shard" ]; then
  if ! [[ "$shard" =~ ^([1-9][0-9]*)/([1-9][0-9]*)$ ]] || [ "${BASH_REMATCH[1]}" -gt "${BASH_REMATCH[2]}" ]; then
    echo "$shard_variable must be a shard such as 1/3, not '$shard'." >&2
    exit 2
  fi
  shard_index="${BASH_REMATCH[1]}"
  shard_total="${BASH_REMATCH[2]}"
  if [ "$shard_total" -gt 1 ]; then
    if [ "$shard_index" = 1 ]; then
      run_vitest=0
    else
      run_rest=0
      web_suite="web-$shard_index"
      # Every shard computes the same split, so a failed split stops the run rather than leave files out.
      shard_files="$(node scripts/lib/test-shard.mjs "$shard")"
      if [ -z "$shard_files" ]; then
        echo "Shard $shard has no Vitest file. Use fewer shards." >&2
        exit 2
      fi
      mapfile -t vitest_files <<<"$shard_files"
    fi
  fi
fi

if [ "$run_rest" = 1 ]; then
  # The server tests start the server, which reads the built-in modules, sample modules and built-in applications from artifacts/. NexKit
  # runs this script in a job without scripts/build.sh, which installs them too.
  bash scripts/lib/install-modules.sh
  bash scripts/lib/install-sample-templates.sh

  # The server tests read the server's OpenAPI document. NexKit runs this script without scripts/build.sh, which writes it too.
  dotnet run --project tests/Axis.OpenApiDocument -c Release -- artifacts/openapi/axis-server.json || status=1

  for project in Axis.Server.Tests Axis.Configuration.Tests Axis.Data.Tests Axis.Expressions.Tests Axis.Tenancy.Tests Axis.Agent.Tests; do
    run_suite "$project" "tests/$project" dotnet test --project "tests/$project" -c Release \
      --report-xunit-junit --report-xunit-junit-filename "$project.xml" --results-directory "$results_dir"
  done
  run_suite PaymentReferences.Tests samples/packages/PaymentReferences.Tests dotnet test --project samples/packages/PaymentReferences.Tests -c Release \
    --report-xunit-junit --report-xunit-junit-filename PaymentReferences.Tests.xml --results-directory "$results_dir"
  run_suite Axis.ControlPlane.Tests controlplane/tests/Axis.ControlPlane.Tests dotnet test --project controlplane/tests/Axis.ControlPlane.Tests -c Release \
    --report-xunit-junit --report-xunit-junit-filename Axis.ControlPlane.Tests.xml --results-directory "$results_dir"
fi

if [ "$run_vitest" = 1 ]; then
  run_suite "$web_suite" web npm test --prefix web -- \
    --reporter=default --reporter=junit --outputFile.junit="$results_dir/$web_suite.xml" "${vitest_files[@]}"
fi

# The report, smoke test selection, link check, docs structure check and its rules, bundle budget check,
# font check, licence writer, design-system bundle check, dependency scan, SPA lock file, image scan, setup retries, image build retries, E2E, integration and test shard
# splits, the test script's shards, load script, load test workflow, load objectives, cell load scenarios, cell load script, cell load tenants, E2E folder comparison, hang guard, dialog and drag helpers, TOTP function, load test routes and TOTP module, contrast helper, Compose status
# helper, module install script, dev script stop helper, development realm script and Helm chart scripts' own tests.
if [ "$run_rest" = 1 ]; then
  node --test scripts/test-report.test.mjs || status=1
  node --test scripts/affected.test.mjs || status=1
  node --test scripts/check-links.test.mjs || status=1
  node --test scripts/check-docs.test.mjs || status=1
  node --test scripts/lib/docs/*.test.mjs || status=1
  node --test scripts/docs-audit.test.mjs || status=1
  node --test scripts/check-issue-links.test.mjs || status=1
  node --test scripts/check-bundle.test.mjs || status=1
  node --test scripts/check-fonts.test.mjs || status=1
  node --test scripts/write-licences.test.mjs || status=1
  node --test scripts/check-design-system.test.mjs || status=1
  node --test scripts/scan.test.mjs || status=1
  node --test scripts/web-lockfile.test.mjs || status=1
  node --test scripts/scan-images.test.mjs || status=1
  node --test scripts/keycloak-image.test.mjs || status=1
  node --test scripts/setup.test.mjs || status=1
  node --test scripts/e2e.test.mjs || status=1
  node --test scripts/e2e-shard.test.mjs || status=1
  node --test scripts/integration-shard.test.mjs || status=1
  node --test scripts/test-shard.test.mjs || status=1
  node --test scripts/test.test.mjs || status=1
  node --test scripts/load-objectives.test.mjs || status=1
  node --test scripts/load.test.mjs || status=1
  node --test scripts/load-cell.test.mjs || status=1
  node --test scripts/load-cell-tenants.test.mjs || status=1
  node --test scripts/load-workflow.test.mjs || status=1
  node --test scripts/smoke.test.mjs || status=1
  node --test scripts/install-modules.test.mjs || status=1
  node --test scripts/compose-status.test.mjs || status=1
  node --test scripts/dev-stop.test.mjs || status=1
  node --test scripts/development-realm.test.mjs || status=1
  node --test scripts/docker-build.test.mjs || status=1
  node --test tests/e2e/folder-comparison.test.ts || status=1
  node --test tests/e2e/hang-guard.test.ts || status=1
  node --test tests/e2e/dialogs.test.ts || status=1
  node --test tests/e2e/design-drag.test.ts || status=1
  node --test tests/e2e/policy-violations.test.ts || status=1
  node --test tests/e2e/startup-activation.test.ts || status=1
  node --test tests/e2e/totp.test.ts || status=1
  node --test tests/e2e/contrast.test.ts || status=1
  node --test tests/load/objectives.test.mjs || status=1
  node --test tests/load/totp.test.mjs || status=1
  HELM="$helm" node --test scripts/helm.test.mjs || status=1
  HELM="$helm" node --test scripts/helm-controlplane.test.mjs || status=1
fi

finish_report
