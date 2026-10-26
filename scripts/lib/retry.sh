# Runs a command up to ATTEMPTS times, doubling the wait after each failure. Source it from the repository root.
retry() {
  local attempts="$1" attempt=1 status
  shift
  until "$@"; do
    status=$?
    if [ "$attempt" -ge "$attempts" ]; then
      echo "Failed after $attempts attempts: $*" >&2
      return "$status"
    fi
    echo "Attempt $attempt of $attempts failed with status $status: $*" >&2
    sleep "$(( ${AXIS_RETRY_DELAY_SECONDS:-15} * (1 << (attempt - 1)) ))"
    attempt=$((attempt + 1))
  done
}
