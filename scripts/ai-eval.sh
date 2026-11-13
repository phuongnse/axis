#!/usr/bin/env bash
# The authoring eval against one model binding, run on demand and never in CI: scripts/ai-eval.sh --binding <name>. It starts the
# eval stack (PostgreSQL and Keycloak in Docker, and the built server, publisher and agent host) and works through the cases in
# tests/ai-eval/cases one at a time. For each it creates a tenant, sends the request, approves the plan, accepts a `ready` draft
# and judges the case. It writes artifacts/ai-eval/report.json, with the pass bar result and, for each case, whether it passed,
# the reasons, the tokens and `durationMs`. The settings of the binding come from the Ai__Bindings__<name>__* environment
# variables, and no other binding reaches the agent host. It calls the model, which costs money and takes minutes per case.
# AXIS_AI_EVAL_REPORT_DIR writes the report to another folder.
# Exit codes: 0 at or above the pass bar, 1 below it or when the stack does not start, 2 for a wrong argument or a binding with
# no settings, which starts nothing and writes no report.
# See docs/reference/model-bindings.md#the-authoring-eval.
set -euo pipefail
cd "$(dirname "$0")/.."

exec node scripts/lib/ai-eval-run.mjs "$@"
