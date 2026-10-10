# Shared by the test scripts. Source it from the repository root.
# Every suite runs even after another one fails, writes a JUnit file to
# artifacts/test-results/NAME.xml, and scripts/test-report.mjs reports on them.

root="$(pwd)"
results_dir="$root/artifacts/test-results"
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
  rm -f "$results_dir/$name.xml"
  suites+=("$name:$cwd")
  "$@" || status=1
}

# run_suite_if_affected NAME CWD COMMAND...
# Like run_suite, but skips the suite when scripts/lib/affected.mjs shows that no path it
# covers changed since $NEXKIT_BASE_SHA. Any other outcome runs it.
run_suite_if_affected() {
  local decision
  decision="$(node "$root/scripts/lib/affected.mjs" "$1")" || decision=run
  if [ "$decision" = skip ]; then
    rm -f "$results_dir/$1.xml"
    suites+=("$1:$2")
    skipped+=(--skipped "$1")
  else
    run_suite "$@"
  fi
}

# Writes the summary for the suites that ran and exits non-zero when any failed.
finish_report() {
  node "$root/scripts/test-report.mjs" --results-dir "$results_dir" --root "$root" "${skipped[@]}" "${suites[@]}" || status=1
  exit "$status"
}
