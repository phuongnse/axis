# Stops the processes that scripts/dev.sh starts, and the process groups they lead. Source it from the
# repository root. Works with the bash 3.2 that ships with macOS.

descendants() {
  local child
  for child in $(pgrep -P "$1" 2>/dev/null || true); do
    echo "$child"
    descendants "$child"
  done
}

# signal_and_wait SIGNAL SECONDS
# Sends the signal to every pid in $pids and to the process group of every root in $roots, then
# waits until every pid in $waiting has exited, or the time is up.
signal_and_wait() {
  local pid root i alive
  for pid in $pids; do kill "-$1" "$pid" 2>/dev/null || true; done
  for root in $roots; do kill "-$1" -- "-$root" 2>/dev/null || true; done
  for i in $(seq 1 $(($2 * 4))); do
    alive=0
    for pid in $waiting; do
      if kill -0 "$pid" 2>/dev/null; then alive=1; fi
    done
    [ "$alive" -eq 1 ] || return 0
    sleep 0.25
  done
}

# stop_processes ROOT...
# Stops the processes and everything below them. The apps and Vite stop on SIGINT. dotnet
# watch does not: it waits for a file change after its app has stopped. So the wait covers
# the apps and Vite only, and dotnet watch gets killed after that. The output prefixers
# are not waited for either, because they end when the pipes close.
# Each ROOT leads its own process group, so the signals also go to that group. The group reaches
# processes that started after the list was taken, such as the builds that dotnet watch starts.
stop_processes() {
  local pid command root
  pids="" waiting="" roots="$*"
  for root in "$@"; do
    pids="$pids $root $(descendants "$root")"
  done
  for pid in $pids; do
    command="$(ps -o args= -p "$pid" 2>/dev/null || true)"
    case "$command" in
      *dotnet-watch* | *"dotnet watch"* | *dev.sh*) ;;
      *) waiting="$waiting $pid" ;;
    esac
  done
  signal_and_wait INT 10
  signal_and_wait TERM 5
  for pid in $pids; do kill -KILL "$pid" 2>/dev/null || true; done
  for root in $roots; do kill -KILL -- "-$root" 2>/dev/null || true; done
}
