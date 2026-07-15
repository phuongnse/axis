"""Portable, verified user-local tool installation for ``axis setup``."""

from __future__ import annotations

import hashlib
import json
import os
import platform
import posixpath
import shutil
import tarfile
import tempfile
import urllib.parse
import urllib.request
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Callable, Mapping, TextIO

DOTNET_SDK_VERSION = "8.0.423"
NODE_VERSION = "22.23.1"
LYCHEE_VERSION = "0.23.0"
GH_VERSION = "2.96.0"
DOWNLOAD_TIMEOUT_SECONDS = 60
USER_AGENT = "Axis setup"


class SetupError(RuntimeError):
    """Raised when portable setup cannot continue safely."""


@dataclass(frozen=True)
class SetupPlatform:
    os: str
    arch: str

    @property
    def label(self) -> str:
        return f"{self.os}-{self.arch}"


@dataclass(frozen=True)
class Artifact:
    tool: str
    version: str
    name: str
    url: str
    checksum_algorithm: str
    checksum: str


def detect_platform(*, system: str | None = None, machine: str | None = None) -> SetupPlatform:
    system_name = (system or platform.system()).strip().lower()
    machine_name = (machine or platform.machine()).strip().lower()
    os_names = {"linux": "linux", "darwin": "darwin", "windows": "windows"}
    architectures = {
        "x86_64": "x64",
        "amd64": "x64",
        "arm64": "arm64",
        "aarch64": "arm64",
    }
    if system_name not in os_names:
        raise SetupError(
            f"unsupported operating system `{system or platform.system()}`; "
            "Axis setup supports Windows, Linux/WSL, and macOS"
        )
    if machine_name not in architectures:
        raise SetupError(
            f"unsupported architecture `{machine or platform.machine()}`; "
            "Axis setup supports x64 and arm64"
        )
    return SetupPlatform(os_names[system_name], architectures[machine_name])


def _platform_asset_key(platform_spec: SetupPlatform) -> tuple[str, str]:
    return platform_spec.os, platform_spec.arch


def asset_name(tool: str, platform_spec: SetupPlatform) -> str:
    key = _platform_asset_key(platform_spec)
    if tool == "dotnet":
        rid_os = {"linux": "linux", "darwin": "osx", "windows": "win"}[platform_spec.os]
        extension = "zip" if platform_spec.os == "windows" else "tar.gz"
        return f"dotnet-sdk-{DOTNET_SDK_VERSION}-{rid_os}-{platform_spec.arch}.{extension}"
    if tool == "node":
        node_os = {"linux": "linux", "darwin": "darwin", "windows": "win"}[platform_spec.os]
        extension = "zip" if platform_spec.os == "windows" else "tar.gz"
        return f"node-v{NODE_VERSION}-{node_os}-{platform_spec.arch}.{extension}"
    if tool == "gh":
        gh_os = {"linux": "linux", "darwin": "macOS", "windows": "windows"}[platform_spec.os]
        gh_arch = "amd64" if platform_spec.arch == "x64" else "arm64"
        extension = "zip" if platform_spec.os in {"darwin", "windows"} else "tar.gz"
        return f"gh_{GH_VERSION}_{gh_os}_{gh_arch}.{extension}"
    if tool == "lychee":
        names = {
            ("linux", "x64"): "lychee-x86_64-unknown-linux-gnu.tar.gz",
            ("linux", "arm64"): "lychee-aarch64-unknown-linux-gnu.tar.gz",
            ("darwin", "arm64"): "lychee-arm64-macos.tar.gz",
            ("windows", "x64"): "lychee-x86_64-windows.exe",
        }
        if key not in names:
            label = {
                ("darwin", "x64"): "macOS x64",
                ("windows", "arm64"): "Windows arm64",
            }.get(key, platform_spec.label)
            raise SetupError(
                f"Lychee {LYCHEE_VERSION} has no verified portable artifact for {label}; "
                "install the pinned version externally and rerun doctor"
            )
        return names[key]
    raise SetupError(f"unknown managed tool `{tool}`")


def managed_tools_root(
    *,
    platform_spec: SetupPlatform | None = None,
    env: Mapping[str, str] | None = None,
    home: Path | None = None,
) -> Path:
    platform_spec = platform_spec or detect_platform()
    values = os.environ if env is None else env
    override = values.get("AXIS_TOOLS_DIR")
    if override:
        return Path(override).expanduser()

    home = Path.home() if home is None else home
    if platform_spec.os == "windows":
        local_app_data = values.get("LOCALAPPDATA")
        base = Path(local_app_data) if local_app_data else home / "AppData" / "Local"
        return base / "Axis" / "tools"
    if platform_spec.os == "darwin":
        return home / "Library" / "Application Support" / "Axis" / "tools"
    xdg_data_home = values.get("XDG_DATA_HOME")
    base = Path(xdg_data_home) if xdg_data_home else home / ".local" / "share"
    return base / "axis" / "tools"


def tool_version(tool: str) -> str:
    versions = {
        "dotnet": DOTNET_SDK_VERSION,
        "node": NODE_VERSION,
        "lychee": LYCHEE_VERSION,
        "gh": GH_VERSION,
    }
    try:
        return versions[tool]
    except KeyError as exc:
        raise SetupError(f"unknown managed tool `{tool}`") from exc


def managed_executable(
    tool: str,
    *,
    platform_spec: SetupPlatform | None = None,
    root: Path | None = None,
) -> Path:
    version = tool_version(tool)
    platform_spec = platform_spec or detect_platform()
    root = managed_tools_root(platform_spec=platform_spec) if root is None else root
    install_root = root / tool / version
    windows = platform_spec.os == "windows"
    relative = {
        "dotnet": Path("dotnet.exe" if windows else "dotnet"),
        "node": Path("node.exe") if windows else Path("bin") / "node",
        "lychee": Path("lychee.exe" if windows else "lychee"),
        "gh": Path("bin") / ("gh.exe" if windows else "gh"),
    }[tool]
    return install_root / relative


def managed_bin_dir(tool: str, *, platform_spec: SetupPlatform | None = None) -> Path:
    return managed_executable(tool, platform_spec=platform_spec).parent


def managed_tools_for_profile(profile: str) -> tuple[str, ...]:
    normalized = "review" if profile == "all" else profile
    if normalized in {"build", "local-dev"}:
        return ("dotnet", "node")
    if normalized == "review":
        return ("dotnet", "node", "lychee", "gh")
    raise SetupError(f"unknown setup profile `{profile}`")


def confirm_install(tools: tuple[str, ...], *, assume_yes: bool, stdin: TextIO) -> None:
    if not tools or assume_yes:
        return
    if not stdin.isatty():
        raise SetupError("user-local tool installation requires confirmation; rerun with --yes")
    labels = ", ".join(tools)
    print(f"Install pinned user-local tools ({labels})? [y/N] ", end="", flush=True)
    answer = stdin.readline()
    if answer.strip().lower() not in {"y", "yes"}:
        raise SetupError("user-local tool installation cancelled")


def setup_plan(
    *,
    profile: str,
    install_user_tools: bool,
    browsers: bool,
    platform_spec: SetupPlatform,
) -> list[str]:
    normalized = "review" if profile == "all" else profile
    steps = [f"detect supported platform ({platform_spec.label})", "validate Python 3 and Git prerequisites"]
    if install_user_tools:
        labels: list[str] = []
        external: list[str] = []
        for tool in managed_tools_for_profile(normalized):
            label = {
                "dotnet": f".NET SDK {DOTNET_SDK_VERSION}",
                "node": f"Node.js {NODE_VERSION}",
                "lychee": f"Lychee {LYCHEE_VERSION}",
                "gh": f"GitHub CLI {GH_VERSION}",
            }[tool]
            try:
                asset_name(tool, platform_spec)
            except SetupError:
                external.append(label)
            else:
                labels.append(label)
        if labels:
            steps.append(f"install missing pinned user-local tools: {', '.join(labels)}")
        if external:
            steps.append(f"verified portable artifact unavailable; {', '.join(external)} requires external installation")
    else:
        steps.append("validate required toolchains; do not install executables")
    if normalized in {"local-dev", "review"}:
        steps.append("diagnose Docker Engine, Compose, and OpenSSL without changing OS services")
    if normalized == "review":
        steps.append("diagnose review tools and report authentication follow-ups")
    steps.extend(["restore locked .NET dependencies", "install locked frontend dependencies"])
    if normalized in {"local-dev", "review"} or browsers:
        steps.append("install Playwright Chromium")
    if normalized in {"local-dev", "review"}:
        steps.extend(
            [
                "generate local HTTPS certificates",
                "install repository pre-push hook",
            ]
        )
    return steps


def fetch_text(url: str) -> str:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=DOWNLOAD_TIMEOUT_SECONDS) as response:
        return response.read().decode("utf-8")


def fetch_json(url: str) -> dict[str, object]:
    return json.loads(fetch_text(url))


def resolve_node_artifact(
    platform_spec: SetupPlatform,
    *,
    fetch_text: Callable[[str], str] = fetch_text,
) -> Artifact:
    name = asset_name("node", platform_spec)
    base_url = f"https://nodejs.org/download/release/v{NODE_VERSION}"
    shasums = fetch_text(f"{base_url}/SHASUMS256.txt")
    checksum = ""
    for line in shasums.splitlines():
        parts = line.split()
        if len(parts) == 2 and parts[1].lstrip("*") == name:
            checksum = parts[0]
            break
    if not checksum:
        raise SetupError(f"official Node SHASUMS256.txt does not contain `{name}`")
    return Artifact("node", NODE_VERSION, name, f"{base_url}/{name}", "sha256", checksum)


def resolve_dotnet_artifact(
    platform_spec: SetupPlatform,
    *,
    fetch_json: Callable[[str], dict[str, object]] = fetch_json,
) -> Artifact:
    name = asset_name("dotnet", platform_spec)
    metadata_url = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json"
    metadata = fetch_json(metadata_url)
    rid_os = {"linux": "linux", "darwin": "osx", "windows": "win"}[platform_spec.os]
    expected_rid = f"{rid_os}-{platform_spec.arch}"
    releases = metadata.get("releases")
    if not isinstance(releases, list):
        raise SetupError("official .NET release metadata has no releases list")

    for release in releases:
        if not isinstance(release, dict):
            continue
        sdk = release.get("sdk")
        if not isinstance(sdk, dict) or sdk.get("version") != DOTNET_SDK_VERSION:
            continue
        files = sdk.get("files")
        if not isinstance(files, list):
            break
        for file in files:
            if not isinstance(file, dict) or file.get("rid") != expected_rid:
                continue
            url = file.get("url")
            checksum = file.get("hash")
            if not isinstance(url, str) or Path(urllib.parse.urlparse(url).path).name != name:
                continue
            if not isinstance(checksum, str) or len(checksum) != 128:
                raise SetupError(f"official .NET metadata for `{name}` has no valid SHA-512 hash")
            return Artifact("dotnet", DOTNET_SDK_VERSION, name, url, "sha512", checksum)
    raise SetupError(f"official .NET release metadata does not contain `{name}`")


def resolve_github_artifact(
    tool: str,
    platform_spec: SetupPlatform,
    *,
    fetch_json: Callable[[str], dict[str, object]] = fetch_json,
) -> Artifact:
    if tool == "gh":
        version = GH_VERSION
        api_url = f"https://api.github.com/repos/cli/cli/releases/tags/v{version}"
    elif tool == "lychee":
        version = LYCHEE_VERSION
        api_url = f"https://api.github.com/repos/lycheeverse/lychee/releases/tags/lychee-v{version}"
    else:
        raise SetupError(f"`{tool}` is not a GitHub release tool")

    name = asset_name(tool, platform_spec)
    release = fetch_json(api_url)
    assets = release.get("assets")
    if not isinstance(assets, list):
        raise SetupError(f"official {tool} release metadata has no assets list")
    for asset in assets:
        if not isinstance(asset, dict) or asset.get("name") != name:
            continue
        url = asset.get("browser_download_url")
        digest = asset.get("digest")
        if not isinstance(url, str):
            raise SetupError(f"official {tool} release asset `{name}` has no download URL")
        if not isinstance(digest, str) or not digest.startswith("sha256:"):
            raise SetupError(f"official {tool} release asset `{name}` has no published sha256 digest")
        checksum = digest.removeprefix("sha256:")
        if len(checksum) != 64:
            raise SetupError(f"official {tool} release asset `{name}` has an invalid sha256 digest")
        return Artifact(tool, version, name, url, "sha256", checksum)
    raise SetupError(f"official {tool} release metadata does not contain `{name}`")


def resolve_artifact(tool: str, platform_spec: SetupPlatform) -> Artifact:
    if tool == "node":
        return resolve_node_artifact(platform_spec)
    if tool == "dotnet":
        return resolve_dotnet_artifact(platform_spec)
    if tool in {"gh", "lychee"}:
        return resolve_github_artifact(tool, platform_spec)
    raise SetupError(f"unknown managed tool `{tool}`")


def verify_checksum(path: Path, algorithm: str, expected: str) -> None:
    try:
        digest = hashlib.new(algorithm)
    except ValueError as exc:
        raise SetupError(f"unsupported checksum algorithm `{algorithm}`") from exc
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    actual = digest.hexdigest().lower()
    if actual != expected.strip().lower():
        raise SetupError(
            f"checksum mismatch for {path.name}: expected {expected.strip().lower()}, got {actual}"
        )


def _safe_member_path(name: str, target: Path) -> Path:
    posix = PurePosixPath(name.replace("\\", "/"))
    if posix.is_absolute() or ".." in posix.parts:
        raise SetupError(f"unsafe archive member `{name}`")
    candidate = (target / Path(*posix.parts)).resolve()
    target_resolved = target.resolve()
    if candidate != target_resolved and target_resolved not in candidate.parents:
        raise SetupError(f"unsafe archive member `{name}`")
    return candidate


def _safe_link_target(member: tarfile.TarInfo, target: Path) -> None:
    link = PurePosixPath(member.linkname.replace("\\", "/"))
    if link.is_absolute():
        raise SetupError(f"unsafe archive link `{member.name}` -> `{member.linkname}`")
    combined = link if member.islnk() else PurePosixPath(member.name).parent / link
    normalized = posixpath.normpath(str(combined))
    if normalized == ".." or normalized.startswith("../"):
        raise SetupError(f"unsafe archive link `{member.name}` -> `{member.linkname}`")
    _safe_member_path(normalized, target)


def extract_verified_archive(archive: Path, target: Path) -> None:
    target.mkdir(parents=True, exist_ok=True)
    if archive.name.endswith(".zip"):
        with zipfile.ZipFile(archive) as handle:
            for member in handle.infolist():
                _safe_member_path(member.filename, target)
            handle.extractall(target)
        return
    if archive.name.endswith((".tar.gz", ".tgz")):
        with tarfile.open(archive, "r:gz") as handle:
            for member in handle.getmembers():
                _safe_member_path(member.name, target)
                if not (member.isfile() or member.isdir() or member.issym() or member.islnk()):
                    raise SetupError(f"unsafe archive member type for `{member.name}`")
                if member.issym() or member.islnk():
                    _safe_link_target(member, target)
            try:
                handle.extractall(target, filter="data")
            except TypeError:
                handle.extractall(target)
        return
    raise SetupError(f"unsupported archive format `{archive.name}`")


def download_file(url: str, destination: Path) -> None:
    if not url.lower().startswith("https://"):
        raise SetupError(f"refusing non-HTTPS tool download: {url}")
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    destination.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(request, timeout=DOWNLOAD_TIMEOUT_SECONDS) as response:
        with destination.open("wb") as output:
            shutil.copyfileobj(response, output)


def install_artifact(
    artifact: Artifact,
    *,
    platform_spec: SetupPlatform,
    root: Path | None = None,
    downloader: Callable[[str, Path], object] = download_file,
) -> Path:
    root = managed_tools_root(platform_spec=platform_spec) if root is None else root
    final_root = root / artifact.tool / artifact.version
    expected = managed_executable(artifact.tool, platform_spec=platform_spec, root=root)

    parent = final_root.parent
    parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".axis-setup-", dir=parent) as temp:
        temp_root = Path(temp)
        downloaded = temp_root / artifact.name
        downloader(artifact.url, downloaded)
        if not downloaded.is_file():
            raise SetupError(f"download did not create `{downloaded}`")
        verify_checksum(downloaded, artifact.checksum_algorithm, artifact.checksum)

        staged = temp_root / "staged"
        if artifact.name.endswith(".exe"):
            relative = expected.relative_to(final_root)
            staged_target = staged / relative
            staged_target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(downloaded, staged_target)
        else:
            payload = temp_root / "payload"
            extract_verified_archive(downloaded, payload)
            source = payload
            if artifact.tool in {"node", "gh"}:
                children = list(payload.iterdir())
                if len(children) != 1 or not children[0].is_dir():
                    raise SetupError(f"`{artifact.name}` does not contain the expected top-level directory")
                source = children[0]
            source.rename(staged)

        staged_expected = staged / expected.relative_to(final_root)
        if not staged_expected.is_file():
            raise SetupError(f"`{artifact.name}` does not contain `{expected.relative_to(final_root)}`")
        if platform_spec.os != "windows":
            staged_expected.chmod(staged_expected.stat().st_mode | 0o111)

        previous = temp_root / "previous"
        if final_root.exists():
            final_root.rename(previous)
        try:
            staged.rename(final_root)
        except OSError:
            if previous.exists() and not final_root.exists():
                previous.rename(final_root)
            raise
    return expected


def install_tool(
    tool: str,
    *,
    platform_spec: SetupPlatform | None = None,
    root: Path | None = None,
) -> Path:
    platform_spec = platform_spec or detect_platform()
    artifact = resolve_artifact(tool, platform_spec)
    return install_artifact(artifact, platform_spec=platform_spec, root=root)
