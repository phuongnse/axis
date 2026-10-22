# Shared by the test scripts. Source it from the repository root.
# Every suite runs even after another one fails, writes a JUnit file to
# artifacts/test-results/NAME.xml, and scripts/test-report.mjs reports on them.
# A suite may also write artifacts/test-results/NAME.json, a Playwright JSON report.

root="$(pwd)"
results_dir="${AXIS_TEST_RESULTS_DIR:-$root/artifacts/test-results}"
mkdir -p "$results_dir"
status=0
suites=()
skipped=()

# run_suite NAME CWD COMMAND...
# Runs a suite whose results go to $results_dir/NAME.xml. CWD is the directory
# its relative file paths start from. A failure is recorded in $status.
run_suite() {
  local name="$1" cwd="$2"
  shift 2
  rm -f "$results_dir/$name.xml" "$results_dir/$name.json"
  suites+=("$name:$cwd")
  "$@" || status=1
}

# expect_suite NAME CWD
# Registers a suite whose results another suite writes to $results_dir/NAME.xml. Call it
# before that suite runs, so a results file left by an earlier run is removed first.
expect_suite() {
  rm -f "$results_dir/$1.xml" "$results_dir/$1.json"
  suites+=("$1:$2")
}

# affected NAME
# Succeeds unless scripts/lib/affected.mjs shows that the suite NAME cannot be affected by the
# change since $NEXKIT_BASE_SHA. Any other outcome runs it.
affected() {
  local decision
  decision="$(node "$root/scripts/lib/affected.mjs" "$1")" || decision=run
  [ "$decision" != skip ]
}

# skip_suite NAME CWD
# Registers a suite that does not run, so the summary lists it as skipped.
skip_suite() {
  rm -f "$results_dir/$1.xml" "$results_dir/$1.json"
  suites+=("$1:$2")
  skipped+=(--skipped "$1")
}

# run_suite_if_affected NAME CWD COMMAND...
# Like run_suite, but skips the suite when affected NAME says it cannot be affected.
run_suite_if_affected() {
  if affected "$1"; then
    run_suite "$@"
  else
    skip_suite "$1" "$2"
  fi
}

# Writes the summary for the suites that ran and exits non-zero when any failed.
finish_report() {
  node "$root/scripts/test-report.mjs" --results-dir "$results_dir" --root "$root" "${skipped[@]}" "${suites[@]}" || status=1
  exit "$status"
}
