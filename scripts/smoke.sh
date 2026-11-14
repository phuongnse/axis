#!/usr/bin/env bash
# Smoke tests of the ways Axis runs outside the test hosts (requires Docker with the compose plugin, and
# network access to build images and for the first SPA install). Each runs alone, in this order:
# - dev-script: scripts/dev.test.mjs starts scripts/dev.sh in a copy of the repository and stops it.
# - compose: scripts/compose.test.mjs builds the image and runs the Compose stack on free ports.
# - images: scripts/images.test.mjs checks the production images axis-server, axis-worker, axis-agent, axis-admin,
#   axis-controlplane and axis-package-host-dotnet, -node and -python, runs the hosts read-only against a throwaway
#   PostgreSQL that serves TLS, with SSL Mode=VerifyFull in their connection strings, and builds the test package of each
#   language in its package host image.
# - onprem: scripts/onprem.test.mjs installs deploy/compose/compose.yaml from the production images as
#   docs/reference/installation.md says, against a test PostgreSQL over TLS and the example's own identity
#   provider, which the TLS proxy serves on a host of its own. Every
#   connection string sets SSL Mode=VerifyFull, and the test checks that every Axis connection uses TLS. It
#   creates two tenants, activates the leave request sample, checks each tenant's site and sign-in, suspend
#   and resume, and an unknown host. It checks each package host's read-only root, limits, environment and network. It builds
#   the sample packages payment-references and payment-terms in their package host images, and gives tenant one the
#   purchase request sample's bindings, every url binding over https, and activates the sample there.
#   It reads the sample modules, which this script installs in artifacts/modules.
# - ai-eval: scripts/ai-eval-stack.test.mjs builds the server, the publisher and the agent host and starts them with
#   PostgreSQL and Keycloak in Docker, as the authoring eval's stack, see docs/reference/model-bindings.md#the-authoring-eval.
#   The agent host replays scripted answers. It creates a tenant from the built-in approvals application, reads the
#   application's authoring requests as the tenant's author, and without the session, and carries an authoring request
#   through the agent host and the MCP server, over https, to its plan. scripts/ai-eval-run.test.mjs then runs
#   scripts/ai-eval.sh --binding against a stand-in Messages API server that the test runs, twice: once with a model that
#   passes every case, and once with a model that relaxes a contract point. Each run starts the stack, so the two test files
#   run one at a time.
# This script builds the production images once, before the images suite, when images or onprem runs, each up to three
# times with a wait of 15 and 30 seconds, and removes them when it exits. Both suites use them. The PostgreSQL tools
# layer of axis-admin comes from the admin-tools stage, which scripts/lib/smoke-images.mjs saves in
# ~/.cache/axis-docker and loads in a later run while the stage is unchanged, see docs/development.md#smoke-tests.
# Before the first suite, scripts/lib/docker-images.mjs loads the container images that setup saved in
# ~/.cache/axis-docker, once, when a suite of the shard is affected, so the suites pull none that the cache holds.
# Each is skipped when NexKit gives a commit to compare with and every path changed since it is in the
# `unaffected_by` list of .nexkit/config.json, which scripts/lib/affected.mjs reads. CI and NexKit run this script in
# shards, one job per shard, see docs/development.md#shards.
# AXIS_SMOKE_SHARD=k/N, or NEXKIT_SHARD=k/N when it is unset, runs only the suites that scripts/lib/smoke-shard.mjs gives
# shard k of N, by the seconds in scripts/smoke-durations.json. A shard builds the production images only when it holds
# images or onprem. Without a shard every suite runs. See docs/development.md#shards.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

shard_variable=AXIS_SMOKE_SHARD
shard="${AXIS_SMOKE_SHARD:-}"
if [ -z "$shard" ] && [ -n "${NEXKIT_SHARD:-}" ]; then
  shard_variable=NEXKIT_SHARD
  shard="$NEXKIT_SHARD"
fi
if [ -n "$shard" ]; then
  if ! [[ "$shard" =~ ^([1-9][0-9]*)/([1-9][0-9]*)$ ]] || [ "${BASH_REMATCH[1]}" -gt "${BASH_REMATCH[2]}" ]; then
    echo "$shard_variable must be a shard such as 1/3, not '$shard'." >&2
    exit 2
  fi
  # Every shard computes the same split, so a failed split stops the run rather than leave suites out.
  shard_suites="$(node scripts/lib/smoke-shard.mjs "$shard")" || exit 2
fi
in_shard() { [ -z "$shard" ] || grep -qx "$1" <<<"$shard_suites"; }

bash scripts/lib/install-modules.sh

for suite in dev-script compose images onprem ai-eval; do
  if in_shard "$suite" && affected "$suite"; then
    node scripts/lib/docker-images.mjs load
    break
  fi
done

if in_shard dev-script; then
  run_suite_if_affected dev-script . node --test --test-reporter=spec --test-reporter-destination=stdout \
    --test-reporter=junit --test-reporter-destination="$results_dir/dev-script.xml" scripts/dev.test.mjs
fi

if in_shard compose; then
  run_suite_if_affected compose . node --test --test-reporter=spec --test-reporter-destination=stdout \
    --test-reporter=junit --test-reporter-destination="$results_dir/compose.xml" scripts/compose.test.mjs
fi

if { in_shard images && affected images; } || { in_shard onprem && affected onprem; }; then
  export AXIS_SMOKE_IMAGE_VERSION="0.0.0-smoke.$$"
  trap 'node scripts/lib/smoke-images.mjs remove "$AXIS_SMOKE_IMAGE_VERSION" || true' EXIT
  node scripts/lib/smoke-images.mjs build "$AXIS_SMOKE_IMAGE_VERSION" || status=1
fi

if in_shard images; then
  run_suite_if_affected images . node --test --test-reporter=spec --test-reporter-destination=stdout \
    --test-reporter=junit --test-reporter-destination="$results_dir/images.xml" scripts/images.test.mjs
fi

if in_shard onprem; then
  run_suite_if_affected onprem . node --test --test-reporter=spec --test-reporter-destination=stdout \
    --test-reporter=junit --test-reporter-destination="$results_dir/onprem.xml" scripts/onprem.test.mjs
fi

if in_shard ai-eval; then
  run_suite_if_affected ai-eval . node --test --test-reporter=spec --test-reporter-destination=stdout \
    --test-reporter=junit --test-reporter-destination="$results_dir/ai-eval.xml" --test-concurrency=1 \
    scripts/ai-eval-stack.test.mjs scripts/ai-eval-run.test.mjs
fi

finish_report
