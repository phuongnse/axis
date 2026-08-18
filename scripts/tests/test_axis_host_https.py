from __future__ import annotations

import base64
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis_host_https  # noqa: E402


class TestHostHttpsProbeAdapters(unittest.TestCase):
    def setUp(self) -> None:
        self.executables = {
            "powershell.exe": "resolved-powershell",
            "pwsh.exe": "resolved-pwsh",
            "curl": "resolved-curl",
        }

    def resolve(self, host: str) -> axis_host_https.HostHttpsProbe:
        return axis_host_https.resolve_host_https_probe(
            host,
            url="https://localhost:3000/",
            timeout_seconds=15,
            executable_lookup=self.executables.get,
            python_executable="active-python",
        )

    @staticmethod
    def contract_text(probe: axis_host_https.HostHttpsProbe) -> str:
        command_text = " ".join(probe.command)
        if "-EncodedCommand" not in probe.command:
            return command_text
        script = base64.b64decode(probe.command[-1]).decode("utf-16le")
        return f"{command_text} {script}"

    def test_registry_covers_every_supported_axis_host_kind(self) -> None:
        self.assertEqual(
            frozenset({"windows", "wsl", "darwin", "linux"}),
            axis_host_https.SUPPORTED_HOST_KINDS,
        )
        for host in axis_host_https.SUPPORTED_HOST_KINDS:
            with self.subTest(host=host):
                self.assertIsInstance(self.resolve(host), axis_host_https.HostHttpsProbe)

    def test_adapters_never_disable_tls_or_override_the_trust_store(self) -> None:
        for host in axis_host_https.SUPPORTED_HOST_KINDS:
            with self.subTest(host=host):
                command = self.contract_text(self.resolve(host)).lower()
                self.assertNotIn("insecure", command)
                self.assertNotIn("skipcertificatecheck", command)
                self.assertNotIn("no-check-certificate", command)
                self.assertNotIn("create_unverified_context", command)
                self.assertNotIn("cafile", command)
                self.assertNotIn("cacert", command)

    def test_adapters_reject_redirects_at_the_https_boundary(self) -> None:
        windows = self.contract_text(self.resolve("windows"))
        macos = self.resolve("darwin").command
        linux = self.contract_text(self.resolve("linux"))

        self.assertIn("MaximumRedirection 0", windows)
        self.assertNotIn("--location", macos)
        self.assertIn("NoRedirect", linux)

    def test_windows_family_uses_the_resolved_host_executable(self) -> None:
        for host in ("windows", "wsl"):
            with self.subTest(host=host):
                probe = self.resolve(host)
                self.assertEqual(self.executables["powershell.exe"], probe.command[0])
                self.assertIn("-EncodedCommand", probe.command)
                script = base64.b64decode(probe.command[-1]).decode("utf-16le")
                self.assertIn("Invoke-WebRequest", script)
                self.assertIn("-Uri 'https://localhost:3000/'", script)

    def test_windows_adapter_escapes_the_url_as_data_before_encoding(self) -> None:
        probe = axis_host_https.resolve_host_https_probe(
            "windows",
            url="https://localhost:3000/path'part",
            timeout_seconds=15,
            executable_lookup=self.executables.get,
            python_executable="active-python",
        )

        script = base64.b64decode(probe.command[-1]).decode("utf-16le")
        self.assertIn("-Uri 'https://localhost:3000/path''part'", script)

    def test_macos_uses_the_resolved_host_executable(self) -> None:
        probe = self.resolve("darwin")

        self.assertEqual(self.executables["curl"], probe.command[0])
        self.assertNotIn("-k", probe.command)
        self.assertNotIn("--insecure", probe.command)

    def test_linux_uses_the_active_python_default_ssl_trust(self) -> None:
        probe = self.resolve("linux")

        self.assertEqual("active-python", probe.command[0])
        self.assertIn("urllib.request", " ".join(probe.command))
        self.assertIn("ssl.create_default_context", " ".join(probe.command))
        self.assertIn("default SSL trust", probe.boundary)

    def test_adapter_fails_closed_when_its_required_host_client_is_missing(self) -> None:
        with self.assertRaisesRegex(
            axis_host_https.HostHttpsProbeUnavailable,
            "PowerShell",
        ):
            axis_host_https.resolve_host_https_probe(
                "windows",
                url="https://localhost:3000/",
                timeout_seconds=15,
                executable_lookup=lambda _name: None,
                python_executable="active-python",
            )


if __name__ == "__main__":
    unittest.main()
