from __future__ import annotations

import argparse
import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis  # noqa: E402
import axis_setup  # noqa: E402


def setup_args(**overrides: object) -> argparse.Namespace:
    values: dict[str, object] = {
        "profile": "build",
        "browsers": False,
        "plan_only": False,
        "install_user_tools": False,
        "trust_local_ca": False,
        "yes": False,
    }
    values.update(overrides)
    return argparse.Namespace(**values)


class TestPortableSetupCli(unittest.TestCase):
    def test_build_profile_rejects_local_ca_trust(self) -> None:
        with (
            mock.patch.object(axis.axis_setup, "detect_platform") as detect_platform,
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.setup(setup_args(trust_local_ca=True)))

        detect_platform.assert_not_called()
        self.assertIn("requires --profile local-dev", stderr.getvalue())

    def test_command_exists_does_not_treat_a_relative_repo_file_as_an_executable(self) -> None:
        with (
            mock.patch.object(axis, "resolve_exe", return_value="README.md"),
            mock.patch.object(axis.shutil, "which", return_value=None),
        ):
            self.assertFalse(axis.command_exists("missing-tool"))

    def test_preflight_reuses_strict_doctor_for_the_selected_profile(self) -> None:
        with mock.patch.object(axis, "doctor", return_value=0) as doctor:
            self.assertEqual(0, axis.setup_preflight("build"))

        called = doctor.call_args.args[0]
        self.assertEqual("build", called.profile)
        self.assertTrue(called.strict)

    def test_resolve_exe_uses_a_managed_tool_when_path_has_no_match(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            managed = Path(temp) / "dotnet"
            managed.write_text("", encoding="utf-8")
            with (
                mock.patch.object(axis.shutil, "which", return_value=None),
                mock.patch.object(axis.axis_setup, "managed_executable", return_value=managed),
            ):
                self.assertEqual(str(managed), axis.resolve_exe("dotnet"))

    def test_setup_treats_an_old_github_cli_as_missing(self) -> None:
        with mock.patch.object(
            axis,
            "command_version_line",
            return_value=(True, "gh version 2.40.0", "/usr/bin/gh"),
        ):
            self.assertFalse(axis.setup_tool_ready("gh"))

    def test_setup_treats_node_with_an_unresolved_npm_as_missing(self) -> None:
        with (
            mock.patch.object(axis, "node_version_status", return_value=(True, "v22.23.1")),
            mock.patch.object(axis, "_command_version", return_value=("FAIL", "npm not found")),
        ):
            self.assertFalse(axis.setup_tool_ready("node"))

    def test_setup_rejects_node_when_no_paired_toolchain_environment_resolves(self) -> None:
        with (
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(axis, "node_version_status", return_value=(True, "v22.23.1")),
            mock.patch.object(axis, "_command_version", return_value=("OK", "10.9.8")),
        ):
            self.assertFalse(axis.setup_tool_ready("node"))

    def test_frontend_toolchain_pairs_managed_node_with_its_adjacent_npm(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            managed_bin = Path(temp) / "managed" / "bin"
            managed_bin.mkdir(parents=True)
            (managed_bin / "node").write_text("", encoding="utf-8")
            (managed_bin / "npm").write_text("", encoding="utf-8")

            def fake_version(name: str, *_args: str, env: dict[str, str] | None = None):
                if env and env["PATH"].split(axis.os.pathsep)[0] == str(managed_bin):
                    version = "v22.23.1" if name == "node" else "10.9.8"
                    return True, version, str(managed_bin / name)
                if name == "node":
                    return True, "v22.23.1", str(managed_bin / "node")
                return True, "10.9.8", "/usr/bin/npm"

            with (
                mock.patch.object(axis, "required_node_major", return_value=(True, "22")),
                mock.patch.object(axis, "command_version_line", side_effect=fake_version),
                mock.patch.object(axis, "_nvm_node_bin_dirs", return_value=[managed_bin]),
            ):
                env = axis.frontend_toolchain_env()

        self.assertEqual(str(managed_bin), env["PATH"].split(axis.os.pathsep)[0])

    def test_plan_only_prints_the_portable_plan_without_checks_or_mutations(self) -> None:
        platform_spec = axis_setup.SetupPlatform("windows", "arm64")
        with (
            mock.patch.object(axis.axis_setup, "detect_platform", return_value=platform_spec),
            mock.patch.object(axis, "setup_tool_ready") as tool_ready,
            mock.patch.object(axis.axis_setup, "install_tool") as install_tool,
            mock.patch.object(axis, "run") as run,
            mock.patch.object(axis, "run_frontend_npm") as run_npm,
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(
                0,
                axis.setup(
                    setup_args(
                        profile="review",
                        plan_only=True,
                        install_user_tools=True,
                    )
                ),
            )

        self.assertIn("platform=windows-arm64", stdout.getvalue())
        self.assertIn("GitHub CLI 2.96.0", stdout.getvalue())
        self.assertIn("stable user command", stdout.getvalue())
        tool_ready.assert_not_called()
        install_tool.assert_not_called()
        run.assert_not_called()
        run_npm.assert_not_called()

    def test_local_dev_plan_explains_that_host_browser_trust_is_opt_in(self) -> None:
        with (
            mock.patch.object(
                axis.axis_setup,
                "detect_platform",
                return_value=axis_setup.SetupPlatform("linux", "x64"),
            ),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(
                0,
                axis.setup(setup_args(profile="local-dev", plan_only=True)),
            )

        self.assertIn("host browser trust is opt-in", stdout.getvalue())
        self.assertIn("--trust-local-ca", stdout.getvalue())

    def test_build_profile_preserves_locked_dependency_restore(self) -> None:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0)

        def fake_npm(command: list[str], **_kwargs):
            calls.append(["npm", *command])
            return axis.subprocess.CompletedProcess(command, 0)

        with (
            mock.patch.object(axis.axis_setup, "detect_platform", return_value=axis_setup.SetupPlatform("linux", "x64")),
            mock.patch.object(axis, "setup_external_preflight", return_value=0),
            mock.patch.object(axis, "setup_preflight", return_value=0),
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "run_frontend_npm", side_effect=fake_npm),
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.setup(setup_args()))

        self.assertEqual(
            [["dotnet", "restore", "Axis.sln"], ["npm", "ci"]],
            calls,
        )

    def test_local_dev_profile_owns_certificates_and_hooks_without_host_browser(self) -> None:
        calls: list[str] = []
        with (
            mock.patch.object(axis.axis_setup, "detect_platform", return_value=axis_setup.SetupPlatform("darwin", "arm64")),
            mock.patch.object(axis, "setup_external_preflight", return_value=0),
            mock.patch.object(axis, "setup_preflight", return_value=0),
            mock.patch.object(
                axis,
                "run",
                return_value=axis.subprocess.CompletedProcess([], 0),
            ),
            mock.patch.object(
                axis,
                "run_frontend_npm",
                side_effect=lambda args, **_kwargs: calls.append(" ".join(args))
                or axis.subprocess.CompletedProcess(args, 0),
            ),
            mock.patch.object(axis, "local_dev_certs", side_effect=lambda _args: calls.append("certs") or 0),
            mock.patch.object(
                axis,
                "local_dev_host_trust_status",
                return_value=(
                    "WARN",
                    "host browser trust is not configured; run "
                    "`python scripts/axis.py local-dev trust-certs`",
                ),
                create=True,
            ),
            mock.patch.object(axis, "install_hooks", side_effect=lambda _args: calls.append("hooks") or 0),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(0, axis.setup(setup_args(profile="local-dev")))

        self.assertEqual(["ci", "certs", "hooks"], calls)
        self.assertIn("host browser trust is not configured", stdout.getvalue())
        self.assertIn("local-dev trust-certs", stdout.getvalue())

    def test_external_preflight_explains_stale_docker_group_session(self) -> None:
        with (
            mock.patch.object(axis, "doctor", return_value=0),
            mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"),
            mock.patch.object(axis, "_docker_info_ok", return_value=False),
            mock.patch.object(
                axis,
                "_docker_group_session_hint",
                return_value="Docker group membership is configured but missing from this shell; start a new login shell",
                create=True,
            ),
            mock.patch.object(axis, "require_docker_compose") as compose,
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.setup_external_preflight("local-dev"))

        compose.assert_not_called()
        self.assertIn("new login shell", stderr.getvalue())

    def test_docker_group_hint_distinguishes_configured_from_active_membership(self) -> None:
        handler = getattr(axis, "_docker_group_session_hint", None)
        self.assertTrue(callable(handler))

        stale = handler(
            socket_group_id=998,
            configured_group_ids={1000, 998},
            active_group_ids={1000},
        )
        active = handler(
            socket_group_id=998,
            configured_group_ids={1000, 998},
            active_group_ids={1000, 998},
        )

        self.assertIn("new login shell", stale)
        self.assertIsNone(active)

    def test_local_dev_profile_can_explicitly_trust_the_generated_root_ca(self) -> None:
        calls: list[str] = []
        with (
            mock.patch.object(axis.axis_setup, "detect_platform", return_value=axis_setup.SetupPlatform("windows", "x64")),
            mock.patch.object(axis, "setup_external_preflight", return_value=0),
            mock.patch.object(axis, "setup_preflight", return_value=0),
            mock.patch.object(axis, "run", return_value=axis.subprocess.CompletedProcess([], 0)),
            mock.patch.object(
                axis,
                "run_frontend_npm",
                side_effect=lambda args, **_kwargs: calls.append(" ".join(args))
                or axis.subprocess.CompletedProcess(args, 0),
            ),
            mock.patch.object(axis, "local_dev_certs", side_effect=lambda _args: calls.append("certs") or 0),
            mock.patch.object(axis, "local_dev_trust_certs", side_effect=lambda _args: calls.append("trust") or 0),
            mock.patch.object(axis, "install_hooks", side_effect=lambda _args: calls.append("hooks") or 0),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(
                0,
                axis.setup(setup_args(profile="local-dev", trust_local_ca=True, yes=True)),
            )

        self.assertEqual(["ci", "certs", "trust", "hooks"], calls)

    def test_install_user_tools_installs_only_missing_profile_tools_before_preflight(self) -> None:
        ready = {"dotnet": False, "node": True}
        installed: list[str] = []

        def fake_install(tool: str, **_kwargs):
            installed.append(tool)
            ready[tool] = True
            return Path("/managed") / tool

        with (
            mock.patch.object(axis.axis_setup, "detect_platform", return_value=axis_setup.SetupPlatform("linux", "x64")),
            mock.patch.object(axis, "setup_tool_ready", side_effect=lambda tool: ready[tool]),
            mock.patch.object(axis, "setup_external_preflight", return_value=0),
            mock.patch.object(axis.axis_setup, "confirm_install") as confirm,
            mock.patch.object(axis.axis_setup, "install_tool", side_effect=fake_install),
            mock.patch.object(axis, "setup_preflight", return_value=0),
            mock.patch.object(axis, "run", return_value=axis.subprocess.CompletedProcess([], 0)),
            mock.patch.object(axis, "run_frontend_npm", return_value=axis.subprocess.CompletedProcess([], 0)),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.setup(setup_args(install_user_tools=True, yes=True)))

        confirm.assert_called_once()
        self.assertEqual(["dotnet"], installed)

    def test_review_setup_exposes_an_existing_managed_gh_command(self) -> None:
        managed = Path("/managed/gh")
        exposed = Path("/users/alice/.local/bin/gh")
        with (
            mock.patch.object(axis.axis_setup, "detect_platform", return_value=axis_setup.SetupPlatform("linux", "x64")),
            mock.patch.object(axis, "setup_tool_ready", return_value=True),
            mock.patch.object(axis, "setup_external_preflight", return_value=0),
            mock.patch.object(axis.axis_setup, "managed_executable", return_value=managed),
            mock.patch.object(axis.shutil, "which", return_value=None),
            mock.patch.object(axis.axis_setup, "expose_managed_command", return_value=exposed) as expose,
            mock.patch.object(axis, "setup_preflight", return_value=0),
            mock.patch.object(axis, "run", return_value=axis.subprocess.CompletedProcess([], 0)),
            mock.patch.object(axis, "run_frontend_npm", return_value=axis.subprocess.CompletedProcess([], 0)),
            mock.patch.object(axis, "local_dev_certs", return_value=0),
            mock.patch.object(axis, "install_hooks", return_value=0),
            mock.patch.object(axis, "local_dev_host_trust_status", return_value=("OK", "trusted")),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(
                0,
                axis.setup(setup_args(profile="review", install_user_tools=True, yes=True)),
            )

        expose.assert_called_once_with("gh", platform_spec=axis_setup.SetupPlatform("linux", "x64"))
        self.assertIn("exposed: /users/alice/.local/bin/gh", stdout.getvalue())

    def test_external_preflight_runs_before_user_local_tool_installation(self) -> None:
        events: list[str] = []
        with (
            mock.patch.object(axis.axis_setup, "detect_platform", return_value=axis_setup.SetupPlatform("linux", "x64")),
            mock.patch.object(axis, "setup_tool_ready", return_value=False),
            mock.patch.object(axis, "setup_external_preflight", side_effect=lambda _profile: events.append("external") or 0),
            mock.patch.object(axis.axis_setup, "confirm_install"),
            mock.patch.object(axis.axis_setup, "install_tool", side_effect=lambda tool, **_kwargs: events.append(f"install:{tool}") or Path("/managed") / tool),
            mock.patch.object(axis, "setup_preflight", side_effect=lambda _profile: events.append("full") or 1),
            contextlib.redirect_stdout(io.StringIO()),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            self.assertEqual(1, axis.setup(setup_args(install_user_tools=True, yes=True)))

        self.assertEqual("external", events[0])
        self.assertLess(events.index("external"), events.index("install:dotnet"))
        self.assertLess(events.index("install:node"), events.index("full"))

    def test_external_preflight_failure_stops_before_confirmation_and_download(self) -> None:
        with (
            mock.patch.object(axis.axis_setup, "detect_platform", return_value=axis_setup.SetupPlatform("linux", "x64")),
            mock.patch.object(axis, "setup_tool_ready", return_value=False),
            mock.patch.object(axis, "setup_external_preflight", return_value=1),
            mock.patch.object(axis.axis_setup, "confirm_install") as confirm,
            mock.patch.object(axis.axis_setup, "install_tool") as install_tool,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(1, axis.setup(setup_args(install_user_tools=True, yes=True)))

        confirm.assert_not_called()
        install_tool.assert_not_called()

    def test_install_stops_before_download_when_a_missing_tool_has_no_verified_artifact(self) -> None:
        with (
            mock.patch.object(axis.axis_setup, "detect_platform", return_value=axis_setup.SetupPlatform("windows", "arm64")),
            mock.patch.object(axis, "setup_tool_ready", return_value=False),
            mock.patch.object(axis.axis_setup, "install_tool") as install_tool,
            contextlib.redirect_stdout(io.StringIO()),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            rc = axis.setup(setup_args(profile="review", install_user_tools=True, yes=True))

        self.assertEqual(1, rc)
        self.assertIn("no verified portable artifact", stderr.getvalue())
        install_tool.assert_not_called()


if __name__ == "__main__":
    unittest.main()
