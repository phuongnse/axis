#!/usr/bin/env bash
# Smoke tests of the ways Axis runs outside the test hosts (requires Docker with the compose plugin, and
# network access to build images and for the first SPA install). Each runs alone, in this order:
# - dev-script: scripts/dev.test.mjs starts scripts/dev.sh in a copy of the repository and stops it.
# - compose: scripts/compose.test.mjs builds the image and runs the Compose stack on free ports.
# - images: scripts/images.test.mjs builds the production images axis-server, axis-worker and axis-admin,
#   each up to three times with a wait of 15 and 30 seconds, and runs them read-only against a throwaway
#   PostgreSQL that serves TLS, with SSL Mode=VerifyFull in their connection strings.
# - onprem: scripts/onprem.test.mjs installs deploy/compose/compose.yaml from the production images as
#   docs/reference/installation.md says, against a test PostgreSQL over TLS and Keycloak over HTTPS. Every
#   connection string sets SSL Mode=VerifyFull, and the test checks that every Axis connection uses TLS. It
#   creates two tenants, activates the leave request sample, checks each tenant's site and sign-in, suspend
#   and resume, and an unknown host. It reads the sample modules, which this script installs in artifacts/modules.
# Each is skipped when $NEXKIT_BASE_SHA is set and every changed path is on its safe list in
# scripts/lib/affected.mjs. CI runs this script as a job of its own, beside the integration tests and the
# Playwright shards.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

bash scripts/lib/install-sample-modules.sh

run_suite_if_affected dev-script . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/dev-script.xml" scripts/dev.test.mjs

run_suite_if_affected compose . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/compose.xml" scripts/compose.test.mjs

run_suite_if_affected images . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/images.xml" scripts/images.test.mjs

run_suite_if_affected onprem . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/onprem.xml" scripts/onprem.test.mjs

finish_report
