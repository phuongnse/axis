"""Cross-platform host HTTPS probe selection for Axis local development."""

from __future__ import annotations

import base64
from dataclasses import dataclass
from typing import Callable


class HostHttpsProbeUnavailable(RuntimeError):
    """Raised when a supported host lacks its trust-aware HTTPS client."""


@dataclass(frozen=True)
class HostHttpsProbe:
    command: tuple[str, ...]
    boundary: str


ExecutableLookup = Callable[[str], str | None]
ProbeFactory = Callable[[str, int, ExecutableLookup, str], HostHttpsProbe]


def _required_executable(
    names: tuple[str, ...],
    *,
    executable_lookup: ExecutableLookup,
    label: str,
) -> str:
    for name in names:
        resolved = executable_lookup(name)
        if resolved:
            return resolved
    raise HostHttpsProbeUnavailable(
        f"{label} is unavailable; host HTTPS cannot be verified through its trust stack"
    )


def _windows_probe(
    url: str,
    timeout_seconds: int,
    executable_lookup: ExecutableLookup,
    _python_executable: str,
) -> HostHttpsProbe:
    powershell = _required_executable(
        ("powershell.exe", "pwsh.exe"),
        executable_lookup=executable_lookup,
        label="Windows PowerShell",
    )
    url_literal = "'" + url.replace("'", "''") + "'"
    script = (
        "$ErrorActionPreference = 'Stop'; "
        "$ProgressPreference = 'SilentlyContinue'; "
        "$response = Invoke-WebRequest -UseBasicParsing -Method Head -MaximumRedirection 0 "
        f"-TimeoutSec {timeout_seconds} -Uri {url_literal}; "
        "[Console]::Out.WriteLine([int]$response.StatusCode)"
    )
    encoded_script = base64.b64encode(script.encode("utf-16le")).decode("ascii")
    return HostHttpsProbe(
        command=(
            powershell,
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-EncodedCommand",
            encoded_script,
        ),
        boundary="Windows current-user trust",
    )


def _macos_probe(
    url: str,
    timeout_seconds: int,
    executable_lookup: ExecutableLookup,
    _python_executable: str,
) -> HostHttpsProbe:
    curl = _required_executable(
        ("curl",),
        executable_lookup=executable_lookup,
        label="macOS curl",
    )
    return HostHttpsProbe(
        command=(
            curl,
            "--fail",
            "--silent",
            "--show-error",
            "--head",
            "--proto",
            "=https",
            "--max-time",
            str(timeout_seconds),
            "--output",
            "/dev/null",
            "--write-out",
            "%{http_code}",
            url,
        ),
        boundary="resolved macOS curl trust configuration",
    )


_PYTHON_HTTPS_PROBE = """\
import sys
import ssl
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, file_pointer, code, message, headers, new_url):
        return None


request = urllib.request.Request(sys.argv[1], method="HEAD")
context = ssl.create_default_context()
opener = urllib.request.build_opener(
    urllib.request.ProxyHandler({}),
    urllib.request.HTTPSHandler(context=context),
    NoRedirect(),
)
with opener.open(request, timeout=float(sys.argv[2])) as response:
    print(response.status)
"""


def _linux_probe(
    url: str,
    timeout_seconds: int,
    _executable_lookup: ExecutableLookup,
    python_executable: str,
) -> HostHttpsProbe:
    return HostHttpsProbe(
        command=(
            python_executable,
            "-c",
            _PYTHON_HTTPS_PROBE,
            url,
            str(timeout_seconds),
        ),
        boundary="Linux default SSL trust",
    )


_PROBE_FACTORIES: dict[str, ProbeFactory] = {
    "windows": _windows_probe,
    "wsl": _windows_probe,
    "darwin": _macos_probe,
    "linux": _linux_probe,
}
SUPPORTED_HOST_KINDS = frozenset(_PROBE_FACTORIES)


def resolve_host_https_probe(
    host: str,
    *,
    url: str,
    timeout_seconds: int,
    executable_lookup: ExecutableLookup,
    python_executable: str,
) -> HostHttpsProbe:
    factory = _PROBE_FACTORIES.get(host)
    if factory is None:
        raise HostHttpsProbeUnavailable(
            f"unsupported host kind `{host}`; expected one of {', '.join(sorted(SUPPORTED_HOST_KINDS))}"
        )
    return factory(url, timeout_seconds, executable_lookup, python_executable)
