# Source it from the repository root. Keeps the wheels that the Python sample packages in samples/packages lock in
# python_wheels, so scripts/lib/install-sample-packages.sh installs them offline, see docs/development.md#setup.
source scripts/lib/retry.sh

python_wheels="$HOME/.cache/axis-tools/wheels"

# Downloads the wheel of each requirement in each samples/packages/*/requirements.txt into python_wheels, checked
# against the hash that the file pins. A wheel the folder already holds is found offline. Only a miss downloads, with
# up to 4 tries. Each try gives up after 30 seconds without an answer, as D147 agrees.
fetch_python_wheels() {
  local requirements flags
  flags=(-m pip download --require-hashes --no-deps --only-binary=:all: --disable-pip-version-check --no-input --timeout 30)
  mkdir -p "$python_wheels"
  for requirements in samples/packages/*/requirements.txt; do
    [ -f "$requirements" ] || continue
    python3 "${flags[@]}" --no-index --find-links "$python_wheels" -d "$python_wheels" -r "$requirements" >/dev/null 2>&1 \
      || retry 4 python3 "${flags[@]}" -d "$python_wheels" -r "$requirements"
  done
}
