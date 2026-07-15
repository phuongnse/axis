from __future__ import annotations

import hashlib
import io
import os
import shutil
import sys
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis_setup  # noqa: E402


class TestSetupPlatform(unittest.TestCase):
    def test_normalizes_supported_operating_systems_and_architectures(self) -> None:
        cases = {
            ("Linux", "x86_64"): ("linux", "x64"),
            ("Linux", "aarch64"): ("linux", "arm64"),
            ("Darwin", "AMD64"): ("darwin", "x64"),
            ("Darwin", "arm64"): ("darwin", "arm64"),
            ("Windows", "AMD64"): ("windows", "x64"),
            ("Windows", "ARM64"): ("windows", "arm64"),
        }

        for source, expected in cases.items():
            with self.subTest(source=source):
                actual = axis_setup.detect_platform(system=source[0], machine=source[1])
                self.assertEqual(expected, (actual.os, actual.arch))

    def test_rejects_unsupported_architecture_with_actionable_detail(self) -> None:
        with self.assertRaisesRegex(axis_setup.SetupError, "unsupported architecture.*riscv64"):
            axis_setup.detect_platform(system="Linux", machine="riscv64")

    def test_rejects_musl_linux_instead_of_selecting_glibc_artifacts(self) -> None:
        with self.assertRaisesRegex(axis_setup.SetupError, "glibc Linux"):
            axis_setup.detect_platform(system="Linux", machine="x86_64", libc="musl")

    def test_injected_linux_platform_does_not_probe_the_host_libc(self) -> None:
        with mock.patch.object(axis_setup.platform, "libc_ver", side_effect=AssertionError("host probe")):
            detected = axis_setup.detect_platform(system="Linux", machine="x86_64")

        self.assertEqual(axis_setup.SetupPlatform("linux", "x64"), detected)

    def test_selects_portable_asset_names(self) -> None:
        windows = axis_setup.SetupPlatform("windows", "x64")
        linux_arm = axis_setup.SetupPlatform("linux", "arm64")
        mac_arm = axis_setup.SetupPlatform("darwin", "arm64")

        self.assertEqual("dotnet-sdk-8.0.423-win-x64.zip", axis_setup.asset_name("dotnet", windows))
        self.assertEqual("node-v22.23.1-win-x64.zip", axis_setup.asset_name("node", windows))
        self.assertEqual("gh_2.96.0_linux_arm64.tar.gz", axis_setup.asset_name("gh", linux_arm))
        self.assertEqual("lychee-arm64-macos.tar.gz", axis_setup.asset_name("lychee", mac_arm))

    def test_reports_unpublished_lychee_platform_instead_of_using_a_package_manager(self) -> None:
        with self.assertRaisesRegex(axis_setup.SetupError, "Lychee.*macOS x64"):
            axis_setup.asset_name("lychee", axis_setup.SetupPlatform("darwin", "x64"))


class TestManagedToolPaths(unittest.TestCase):
    def test_uses_platform_native_user_data_roots(self) -> None:
        home = Path("/users/alice")

        linux = axis_setup.managed_tools_root(
            platform_spec=axis_setup.SetupPlatform("linux", "x64"),
            env={"XDG_DATA_HOME": "/xdg/data"},
            home=home,
        )
        mac = axis_setup.managed_tools_root(
            platform_spec=axis_setup.SetupPlatform("darwin", "arm64"),
            env={},
            home=home,
        )
        windows = axis_setup.managed_tools_root(
            platform_spec=axis_setup.SetupPlatform("windows", "x64"),
            env={"LOCALAPPDATA": r"C:\Users\alice\AppData\Local"},
            home=home,
        )

        self.assertEqual(Path("/xdg/data/axis/tools"), linux)
        self.assertEqual(home / "Library" / "Application Support" / "Axis" / "tools", mac)
        self.assertEqual(Path(r"C:\Users\alice\AppData\Local") / "Axis" / "tools", windows)

    def test_axis_tools_dir_overrides_the_native_location(self) -> None:
        root = axis_setup.managed_tools_root(
            platform_spec=axis_setup.SetupPlatform("linux", "x64"),
            env={"AXIS_TOOLS_DIR": "/custom/axis-tools"},
            home=Path("/unused"),
        )

        self.assertEqual(Path("/custom/axis-tools"), root)

    def test_managed_executable_uses_the_pinned_version_layout(self) -> None:
        root = Path("/tools")

        node = axis_setup.managed_executable(
            "node",
            platform_spec=axis_setup.SetupPlatform("linux", "x64"),
            root=root,
        )
        dotnet = axis_setup.managed_executable(
            "dotnet",
            platform_spec=axis_setup.SetupPlatform("windows", "arm64"),
            root=root,
        )

        self.assertEqual(root / "node" / "22.23.1" / "bin" / "node", node)
        self.assertEqual(root / "dotnet" / "8.0.423" / "dotnet.exe", dotnet)


class TestVerifiedArtifacts(unittest.TestCase):
    def test_rejects_checksum_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            artifact = Path(temp) / "tool.zip"
            artifact.write_bytes(b"untrusted")

            with self.assertRaisesRegex(axis_setup.SetupError, "checksum mismatch"):
                axis_setup.verify_checksum(artifact, "sha256", "0" * 64)

    def test_accepts_matching_sha512_checksum(self) -> None:
        payload = b"verified"
        with tempfile.TemporaryDirectory() as temp:
            artifact = Path(temp) / "tool.tar.gz"
            artifact.write_bytes(payload)

            axis_setup.verify_checksum(artifact, "sha512", hashlib.sha512(payload).hexdigest())

    def test_rejects_zip_path_traversal(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "bad.zip"
            with zipfile.ZipFile(archive, "w") as handle:
                handle.writestr("../escaped", "bad")

            with self.assertRaisesRegex(axis_setup.SetupError, "unsafe archive member"):
                axis_setup.extract_verified_archive(archive, root / "target")

            self.assertFalse((root / "escaped").exists())

    def test_allows_archive_symlinks_that_resolve_inside_the_payload(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "node.tar.gz"
            with tarfile.open(archive, "w:gz") as handle:
                payload = b"npm"
                target = tarfile.TarInfo("node/lib/npm.js")
                target.size = len(payload)
                handle.addfile(target, io.BytesIO(payload))
                link = tarfile.TarInfo("node/bin/npm")
                link.type = tarfile.SYMTYPE
                link.linkname = "../lib/npm.js"
                handle.addfile(link)

            axis_setup.extract_verified_archive(archive, root / "target")

            self.assertEqual(b"npm", (root / "target" / "node" / "bin" / "npm").read_bytes())

    def test_rejects_special_tar_members_before_extraction(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "bad.tar.gz"
            with tarfile.open(archive, "w:gz") as handle:
                fifo = tarfile.TarInfo("payload/fifo")
                fifo.type = tarfile.FIFOTYPE
                handle.addfile(fifo)

            with self.assertRaisesRegex(axis_setup.SetupError, "unsafe archive member type"):
                axis_setup.extract_verified_archive(archive, root / "target")

    def test_fails_closed_when_python_has_no_tar_data_filter(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "safe.tar.gz"
            with tarfile.open(archive, "w:gz") as handle:
                payload = b"tool"
                member = tarfile.TarInfo("payload/tool")
                member.size = len(payload)
                handle.addfile(member, io.BytesIO(payload))

            with (
                mock.patch.object(tarfile.TarFile, "extractall", side_effect=TypeError("no filter")),
                self.assertRaisesRegex(axis_setup.SetupError, "data extraction filter"),
            ):
                axis_setup.extract_verified_archive(archive, root / "target")

    def test_node_resolution_requires_the_official_shasums_entry(self) -> None:
        platform_spec = axis_setup.SetupPlatform("linux", "x64")

        with self.assertRaisesRegex(axis_setup.SetupError, "SHASUMS256"):
            axis_setup.resolve_node_artifact(platform_spec, fetch_text=lambda _url: "other  file.zip\n")

    def test_dotnet_resolution_selects_the_archive_and_official_sha512(self) -> None:
        platform_spec = axis_setup.SetupPlatform("windows", "x64")
        metadata = {
            "releases": [
                {
                    "sdk": {
                        "version": "8.0.423",
                        "files": [
                            {
                                "name": "dotnet-sdk-win-x64.exe",
                                "rid": "win-x64",
                                "url": "https://example.invalid/dotnet-sdk-8.0.423-win-x64.exe",
                                "hash": "1" * 128,
                            },
                            {
                                "name": "dotnet-sdk-win-x64.zip",
                                "rid": "win-x64",
                                "url": "https://example.invalid/dotnet-sdk-8.0.423-win-x64.zip",
                                "hash": "2" * 128,
                            },
                        ],
                    }
                }
            ]
        }

        artifact = axis_setup.resolve_dotnet_artifact(platform_spec, fetch_json=lambda _url: metadata)

        self.assertEqual("dotnet-sdk-8.0.423-win-x64.zip", artifact.name)
        self.assertEqual("sha512", artifact.checksum_algorithm)
        self.assertEqual("2" * 128, artifact.checksum)

    def test_github_resolution_requires_an_official_asset_digest(self) -> None:
        platform_spec = axis_setup.SetupPlatform("linux", "x64")
        release = {
            "assets": [
                {
                    "name": "gh_2.96.0_linux_amd64.tar.gz",
                    "browser_download_url": "https://example.invalid/gh.tar.gz",
                    "digest": None,
                }
            ]
        }

        with self.assertRaisesRegex(axis_setup.SetupError, "published sha256 digest"):
            axis_setup.resolve_github_artifact("gh", platform_spec, fetch_json=lambda _url: release)

    def test_install_extracts_a_verified_portable_archive_into_the_versioned_root(self) -> None:
        platform_spec = axis_setup.SetupPlatform("windows", "x64")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.zip"
            with zipfile.ZipFile(source, "w") as handle:
                handle.writestr("gh_2.96.0_windows_amd64/bin/gh.exe", b"portable-gh")
            checksum = hashlib.sha256(source.read_bytes()).hexdigest()
            artifact = axis_setup.Artifact(
                "gh",
                "2.96.0",
                "gh_2.96.0_windows_amd64.zip",
                "https://example.invalid/gh.zip",
                "sha256",
                checksum,
            )

            installed = axis_setup.install_artifact(
                artifact,
                platform_spec=platform_spec,
                root=root / "tools",
                downloader=lambda _url, destination: shutil.copyfile(source, destination),
            )

            self.assertEqual(root / "tools" / "gh" / "2.96.0" / "bin" / "gh.exe", installed)
            self.assertEqual(b"portable-gh", installed.read_bytes())

    def test_install_replaces_an_invalid_existing_version(self) -> None:
        platform_spec = axis_setup.SetupPlatform("windows", "x64")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.zip"
            with zipfile.ZipFile(source, "w") as handle:
                handle.writestr("gh_2.96.0_windows_amd64/bin/gh.exe", b"verified")
            artifact = axis_setup.Artifact(
                "gh",
                "2.96.0",
                "gh_2.96.0_windows_amd64.zip",
                "https://example.invalid/gh.zip",
                "sha256",
                hashlib.sha256(source.read_bytes()).hexdigest(),
            )
            existing = root / "tools" / "gh" / "2.96.0" / "bin" / "gh.exe"
            existing.parent.mkdir(parents=True)
            existing.write_bytes(b"corrupt")

            axis_setup.install_artifact(
                artifact,
                platform_spec=platform_spec,
                root=root / "tools",
                downloader=lambda _url, destination: shutil.copyfile(source, destination),
            )

            self.assertEqual(b"verified", existing.read_bytes())

    def test_install_restores_the_previous_version_when_atomic_publish_fails(self) -> None:
        platform_spec = axis_setup.SetupPlatform("windows", "x64")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.zip"
            with zipfile.ZipFile(source, "w") as handle:
                handle.writestr("gh_2.96.0_windows_amd64/bin/gh.exe", b"verified")
            artifact = axis_setup.Artifact(
                "gh",
                "2.96.0",
                "gh_2.96.0_windows_amd64.zip",
                "https://example.invalid/gh.zip",
                "sha256",
                hashlib.sha256(source.read_bytes()).hexdigest(),
            )
            existing = root / "tools" / "gh" / "2.96.0" / "bin" / "gh.exe"
            existing.parent.mkdir(parents=True)
            existing.write_bytes(b"previous")
            original_rename = Path.rename

            def fail_publish(path: Path, target: Path):
                if path.name == "staged" and target.name == "2.96.0":
                    raise KeyboardInterrupt("publish interrupted")
                return original_rename(path, target)

            with (
                mock.patch.object(Path, "rename", new=fail_publish),
                self.assertRaisesRegex(KeyboardInterrupt, "publish interrupted"),
            ):
                axis_setup.install_artifact(
                    artifact,
                    platform_spec=platform_spec,
                    root=root / "tools",
                    downloader=lambda _url, destination: shutil.copyfile(source, destination),
                )

            self.assertEqual(b"previous", existing.read_bytes())

    def test_install_restores_when_interrupted_after_the_backup_rename(self) -> None:
        platform_spec = axis_setup.SetupPlatform("windows", "x64")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.zip"
            with zipfile.ZipFile(source, "w") as handle:
                handle.writestr("gh_2.96.0_windows_amd64/bin/gh.exe", b"verified")
            artifact = axis_setup.Artifact(
                "gh",
                "2.96.0",
                "gh_2.96.0_windows_amd64.zip",
                "https://example.invalid/gh.zip",
                "sha256",
                hashlib.sha256(source.read_bytes()).hexdigest(),
            )
            final_root = root / "tools" / "gh" / "2.96.0"
            existing = final_root / "bin" / "gh.exe"
            existing.parent.mkdir(parents=True)
            existing.write_bytes(b"previous")
            original_rename = Path.rename

            def interrupt_backup(path: Path, target: Path):
                result = original_rename(path, target)
                if path == final_root:
                    raise KeyboardInterrupt("backup interrupted")
                return result

            with (
                mock.patch.object(Path, "rename", new=interrupt_backup),
                self.assertRaisesRegex(KeyboardInterrupt, "backup interrupted"),
            ):
                axis_setup.install_artifact(
                    artifact,
                    platform_spec=platform_spec,
                    root=root / "tools",
                    downloader=lambda _url, destination: shutil.copyfile(source, destination),
                )

            self.assertEqual(b"previous", existing.read_bytes())

    def test_install_recovers_a_stranded_previous_version_before_downloading(self) -> None:
        platform_spec = axis_setup.SetupPlatform("windows", "x64")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "tools"
            final_root = root / "gh" / "2.96.0"
            previous_root = final_root.parent / ".2.96.0.axis-previous"
            previous = previous_root / "bin" / "gh.exe"
            previous.parent.mkdir(parents=True)
            previous.write_bytes(b"previous")
            artifact = axis_setup.Artifact(
                "gh",
                "2.96.0",
                "gh_2.96.0_windows_amd64.zip",
                "https://example.invalid/gh.zip",
                "sha256",
                "0" * 64,
            )

            with self.assertRaisesRegex(OSError, "network unavailable"):
                axis_setup.install_artifact(
                    artifact,
                    platform_spec=platform_spec,
                    root=root,
                    downloader=lambda _url, _destination: (_ for _ in ()).throw(OSError("network unavailable")),
                )

            self.assertEqual(b"previous", (final_root / "bin" / "gh.exe").read_bytes())
            self.assertFalse(previous_root.exists())

    def test_fetch_json_normalizes_invalid_publisher_payloads(self) -> None:
        with mock.patch.object(axis_setup, "fetch_text", return_value="not-json"):
            with self.assertRaisesRegex(axis_setup.SetupError, "invalid JSON"):
                axis_setup.fetch_json("https://example.invalid/release.json")

        with mock.patch.object(axis_setup, "fetch_text", return_value="[]"):
            with self.assertRaisesRegex(axis_setup.SetupError, "JSON object"):
                axis_setup.fetch_json("https://example.invalid/release.json")

    def test_extract_normalizes_a_malformed_zip(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            archive = Path(temp) / "bad.zip"
            archive.write_bytes(b"not-a-zip")

            with self.assertRaisesRegex(axis_setup.SetupError, "invalid archive"):
                axis_setup.extract_verified_archive(archive, Path(temp) / "target")

    def test_download_rejects_an_https_to_http_redirect(self) -> None:
        class RedirectedResponse(io.BytesIO):
            def __enter__(self):
                return self

            def __exit__(self, *_args):
                self.close()

            def geturl(self) -> str:
                return "http://mirror.invalid/tool.zip"

        with tempfile.TemporaryDirectory() as temp:
            with (
                mock.patch.object(axis_setup.urllib.request, "urlopen", return_value=RedirectedResponse(b"tool")),
                self.assertRaisesRegex(axis_setup.SetupError, "redirected to non-HTTPS"),
            ):
                axis_setup.download_file("https://example.invalid/tool.zip", Path(temp) / "tool.zip")

    def test_download_rejects_non_https_urls(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaisesRegex(axis_setup.SetupError, "HTTPS"):
                axis_setup.download_file("http://example.invalid/tool.zip", Path(temp) / "tool.zip")


class TestSetupProfiles(unittest.TestCase):
    def test_profiles_are_cumulative_and_keep_system_tools_external(self) -> None:
        self.assertEqual(("dotnet", "node"), axis_setup.managed_tools_for_profile("build"))
        self.assertEqual(("dotnet", "node"), axis_setup.managed_tools_for_profile("local-dev"))
        self.assertEqual(("dotnet", "node", "lychee", "gh"), axis_setup.managed_tools_for_profile("review"))
        self.assertNotIn("docker", axis_setup.managed_tools_for_profile("review"))
        self.assertNotIn("openssl", axis_setup.managed_tools_for_profile("review"))

    def test_install_requires_yes_when_input_is_not_interactive(self) -> None:
        stream = mock.Mock(spec=io.TextIOBase)
        stream.isatty.return_value = False

        with self.assertRaisesRegex(axis_setup.SetupError, "--yes"):
            axis_setup.confirm_install(("dotnet", "node"), assume_yes=False, stdin=stream)

    def test_interactive_install_reads_the_supplied_stream(self) -> None:
        stream = io.StringIO("yes\n")
        stream.isatty = lambda: True  # type: ignore[attr-defined]

        with mock.patch("builtins.print"):
            axis_setup.confirm_install(("dotnet",), assume_yes=False, stdin=stream)

    def test_plan_lists_local_dev_owned_actions_without_mutating(self) -> None:
        plan = axis_setup.setup_plan(
            profile="local-dev",
            install_user_tools=True,
            browsers=False,
            platform_spec=axis_setup.SetupPlatform("windows", "x64"),
        )

        self.assertIn("install missing pinned user-local tools: .NET SDK 8.0.423, Node.js 22.23.1", plan)
        self.assertIn("install Playwright Chromium", plan)
        self.assertIn("generate local HTTPS certificates", plan)
        self.assertIn("install repository pre-push hook", plan)
        self.assertIn("diagnose Docker Engine, Compose, and OpenSSL without changing OS services", plan)
        self.assertLess(
            plan.index("diagnose Docker Engine, Compose, and OpenSSL without changing OS services"),
            plan.index("restore locked .NET dependencies"),
        )
        self.assertLess(
            plan.index("diagnose Docker Engine, Compose, and OpenSSL without changing OS services"),
            plan.index("install missing pinned user-local tools: .NET SDK 8.0.423, Node.js 22.23.1"),
        )

    def test_plan_reports_review_tools_without_a_portable_artifact(self) -> None:
        plan = axis_setup.setup_plan(
            profile="review",
            install_user_tools=True,
            browsers=False,
            platform_spec=axis_setup.SetupPlatform("windows", "arm64"),
        )

        self.assertTrue(any("Lychee 0.23.0 requires external installation" in step for step in plan))


if __name__ == "__main__":
    unittest.main()
