# The image scanner's pinned version and checksums, its download and the checksum check that every
# tool download uses. The owner guards this file, because a change to it could weaken the image scan, see
# docs/decisions/d137-authentication-by-default-and-the-owner-guards-the-security-checks-alone.md.
# Source it from the repository root. scripts/scan-images.sh calls install_trivy, so setup and
# NexKit's checks never download Trivy, and scripts/lib/tools.sh uses verify_sha256.

source scripts/lib/retry.sh

# Trivy scans the images. Aqua withdrew the releases of its March 2026 supply chain incident, so the
# pin is a later release, and its archives' checksums are pinned here rather than read from the
# release itself.
TRIVY_VERSION=0.76.1
TRIVY_SHA256_LINUX_64BIT=c7cbe3137fb8a48a17574bede3dd6beb16742d1e9772929940fc90082c39d9c0
TRIVY_SHA256_LINUX_ARM64=e174dff2a6dba5e3ae41f8d0537a6324405bd08132497340304c6b20e0f16a1f

tools_dir="$HOME/.cache/axis-tools"
trivy="$tools_dir/trivy-$TRIVY_VERSION/trivy"

# Checks FILE against the SHA-256 checksum EXPECTED.
verify_sha256() {
  local file="$1" expected="$2" actual
  if command -v sha256sum >/dev/null; then
    actual="$(sha256sum "$file" | cut -d' ' -f1)"
  else
    actual="$(shasum -a 256 "$file" | cut -d' ' -f1)"
  fi
  if [ "$actual" != "$expected" ]; then
    echo "Checksum mismatch for $file: expected $expected, got $actual" >&2
    return 1
  fi
}

# Downloads Trivy when it is missing, checked against its pinned checksum. The image scan runs on
# Linux, in CI and in the development container.
install_trivy() {
  local platform sum work archive
  if [ -x "$trivy" ]; then return 0; fi
  if [ "$(uname -s)" != Linux ]; then
    echo "The image scan runs on Linux, not $(uname -s)" >&2
    return 1
  fi
  case "$(uname -m)" in
    x86_64 | amd64) platform=Linux-64bit sum="$TRIVY_SHA256_LINUX_64BIT" ;;
    arm64 | aarch64) platform=Linux-ARM64 sum="$TRIVY_SHA256_LINUX_ARM64" ;;
    *) echo "Unsupported architecture: $(uname -m)" >&2; return 1 ;;
  esac
  archive="trivy_${TRIVY_VERSION}_$platform.tar.gz"
  mkdir -p "$tools_dir"
  work="$(mktemp -d "$tools_dir/.install.XXXXXX")"
  mkdir "$work/trivy"
  retry 4 curl -fsSL --retry 3 -o "$work/$archive" \
    "https://github.com/aquasecurity/trivy/releases/download/v$TRIVY_VERSION/$archive"
  verify_sha256 "$work/$archive" "$sum"
  tar -xzf "$work/$archive" -C "$work/trivy" trivy
  mv "$work/trivy" "$tools_dir/trivy-$TRIVY_VERSION"
  rm -rf "$work"
}
