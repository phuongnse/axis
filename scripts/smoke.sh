#!/usr/bin/env bash
# Smoke tests of the ways Axis runs outside the test hosts (requires Docker with the compose plugin, and
# network access to build images and for the first SPA install). Each runs alone, in this order:
# - dev-script: scripts/dev.test.mjs starts scripts/dev.sh in a copy of the repository and stops it.
# - compose: scripts/compose.test.mjs builds the image and runs the Compose stack on free ports.
# - images: scripts/images.test.mjs checks the production images axis-server, axis-worker, axis-agent, axis-admin and
#   axis-controlplane and runs them read-only against a throwaway PostgreSQL that serves TLS, with
#   SSL Mode=VerifyFull in their connection strings.
# - onprem: scripts/onprem.test.mjs installs deploy/compose/compose.yaml from the production images as
#   docs/reference/installation.md says, against a test PostgreSQL over TLS and the example's own identity
#   provider, which the TLS proxy serves on a host of its own. Every
#   connection string sets SSL Mode=VerifyFull, and the test checks that every Axis connection uses TLS. It
#   creates two tenants, activates the leave request sample, checks each tenant's site and sign-in, suspend
#   and resume, and an unknown host. It reads the sample modules, which this script installs in artifacts/modules.
# This script builds the production images once, before the images suite, when images or onprem runs, each up to three
# times with a wait of 15 and 30 seconds, and removes them when it exits. Both suites use them. The PostgreSQL tools
# layer of axis-admin comes from the admin-tools stage, which scripts/lib/smoke-images.mjs saves in
# ~/.cache/axis-docker and loads in a later run while the stage is unchanged, see docs/development.md#smoke-tests.
# Each is skipped when NexKit gives a commit to compare with and every path changed since it is in the
# `unaffected_by` list of .nexkit/config.json, which scripts/lib/affected.mjs reads. CI runs this script as a job of its own, beside the integration
# tests and the Playwright shards.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

bash scripts/lib/install-modules.sh

run_suite_if_affected dev-script . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/dev-script.xml" scripts/dev.test.mjs

run_suite_if_affected compose . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/compose.xml" scripts/compose.test.mjs

if affected images || affected onprem; then
  export AXIS_SMOKE_IMAGE_VERSION="0.0.0-smoke.$$"
  trap 'node scripts/lib/smoke-images.mjs remove "$AXIS_SMOKE_IMAGE_VERSION" || true' EXIT
  node scripts/lib/smoke-images.mjs build "$AXIS_SMOKE_IMAGE_VERSION" || status=1
fi

run_suite_if_affected images . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/images.xml" scripts/images.test.mjs

run_suite_if_affected onprem . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/onprem.xml" scripts/onprem.test.mjs

finish_report
