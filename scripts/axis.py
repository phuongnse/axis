#!/usr/bin/env python3
"""Repository maintenance CLI.

Python is the default entrypoint for repository maintenance. Documented repo
workflows go through this CLI; ecosystem-native tools are implementation
details behind the wrapper.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib
import importlib.util
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from contextlib import contextmanager
from pathlib import Path
from typing import Iterable, TextIO

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = ROOT / "scripts"
REQUIRED_DOTNET_SDK_MAJOR = "8"
REQUIRED_RENOVATE_VALIDATOR_VERSION = "42.99.0"
MINIMUM_CODERABBIT_CLI_VERSION = "0.6.0"
VERSION_PROBE_TIMEOUT_SECONDS = 15
PLAYWRIGHT_BROWSER_PROBE_TIMEOUT_SECONDS = 20
DOCKER_PROBE_TIMEOUT_SECONDS = 20
TOOL_VERSIONS_DOC = "docs/playbooks/scripts.md#tool-versions"
TECH_STACK_DOC = "docs/TECH_STACK.md"
GLOBAL_JSON_PATH = ROOT / "global.json"
NVMRC_PATH = ROOT / "frontend" / ".nvmrc"
LOCAL_DEV_COMPOSE_FILE = ROOT / "docker-compose.yml"
RENOVATE_CONFIG_PATH = ROOT / ".github" / "renovate.json5"
LOCAL_DEV_ENV_FILE = ROOT / ".env.local"
LOCAL_DEV_PROJECT_NAME = "axis"
LOCAL_DEV_POSTGRES_VOLUME = f"{LOCAL_DEV_PROJECT_NAME}_postgres_data"
LOCAL_DEV_BROWSER_BASE_URL = "https://localhost:3000"
LOCAL_DEV_API_BASE_URL = "https://localhost:5281"
LOCAL_DEV_SMOKE_SERVICES = ("api", "web")
LOCAL_DEV_BROWSER_HOME = ROOT / ".dev-browser"
API_PROJECT = ROOT / "src" / "Axis.Api" / "Axis.Api.csproj"
FRONTEND_DIR = ROOT / "frontend"
LOCAL_CERT_DIR = ROOT / ".dev-certs"
LOCAL_ROOT_CA_KEY = LOCAL_CERT_DIR / "rootCA-key.pem"
LOCAL_ROOT_CA_PEM = LOCAL_CERT_DIR / "rootCA.pem"
LOCAL_ROOT_CA_CER = LOCAL_CERT_DIR / "rootCA.cer"
LOCALHOST_KEY = LOCAL_CERT_DIR / "localhost-key.pem"
LOCALHOST_CSR = LOCAL_CERT_DIR / "localhost.csr"
LOCALHOST_EXT = LOCAL_CERT_DIR / "localhost.ext"
LOCALHOST_CERT = LOCAL_CERT_DIR / "localhost.pem"
LOCAL_DEV_SERVICE_SHELL: dict[str, str] = {
    "api": "bash",
    "web": "sh",
    "postgres": "sh",
    "redis": "sh",
    "maildev": "sh",
    "otel-lgtm": "sh",
    "e2e": "bash",
}
LOCAL_DEV_DEFAULT_SHELL = "sh"

if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis_repo  # noqa: E402
import axis_setup  # noqa: E402
import doc_drift_domains  # noqa: E402
from axis_frontend_policy import (  # noqa: E402
    check_frontend_quality,
    frontend_component_file_name_issues,
    frontend_form_schema_type_issues,
    frontend_public_route_navigation_issues,
    frontend_quality_issues,
    frontend_route_access_group_issues,
    frontend_tailwind_opacity_issues,
    frontend_test_async_boundary_issues,
    frontend_transient_handoff_issues,
    frontend_ui_system_issues,
)


class CheckError(RuntimeError):
    """Raised when a command fails."""


def configure_cli_text_streams() -> None:
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8", errors="replace")


def rel(path: Path) -> str:
    return str(path.relative_to(ROOT)).replace("\\", "/")


def path_label(path: Path) -> str:
    try:
        return rel(path)
    except ValueError:
        return str(path)


def rel_from(path: Path, root: Path) -> str:
    return str(path.relative_to(root)).replace("\\", "/")


def exe(name: str) -> str:
    return resolve_exe(name)


def env_path(env: dict[str, str] | None = None) -> str | None:
    return env.get("PATH") if env and "PATH" in env else None


def resolve_exe(name: str, *, env: dict[str, str] | None = None) -> str:
    try:
        managed = axis_setup.managed_executable(name)
    except axis_setup.SetupError:
        managed = None
    if managed is not None and managed.is_file():
        return str(managed)
    path = env_path(env)
    if os.name == "nt" and not name.endswith(".exe"):
        cmd = shutil.which(f"{name}.cmd", path=path)
        if cmd:
            return cmd
    found = shutil.which(name, path=path)
    return found or name


def command_exists(name: str, *, env: dict[str, str] | None = None) -> bool:
    resolved = resolve_exe(name, env=env)
    if Path(resolved).is_file():
        return True
    path = env_path(env)
    return shutil.which(name, path=path) is not None or shutil.which(f"{name}.cmd", path=path) is not None


def run(
    args: list[str],
    *,
    cwd: Path = ROOT,
    check: bool = True,
    capture: bool = False,
    env: dict[str, str] | None = None,
    timeout: float | None = None,
) -> subprocess.CompletedProcess[str]:
    merged_env = os.environ.copy()
    merged_env["PYTHONDONTWRITEBYTECODE"] = "1"
    if env:
        merged_env.update(env)
    try:
        result = subprocess.run(
            args,
            cwd=cwd,
            text=True,
            encoding="utf-8",
            errors="replace",
            stdout=subprocess.PIPE if capture else None,
            stderr=subprocess.PIPE if capture else None,
            check=False,
            env=merged_env,
            timeout=timeout,
        )
    except subprocess.TimeoutExpired as exc:
        stdout = exc.stdout if isinstance(exc.stdout, str) else ""
        stderr = exc.stderr if isinstance(exc.stderr, str) else ""
        message = f"{' '.join(args)} timed out after {timeout:g} seconds" if timeout else f"{' '.join(args)} timed out"
        result = subprocess.CompletedProcess(args, 124, stdout=stdout, stderr=stderr or message)
    if check and result.returncode != 0:
        raise CheckError(f"{' '.join(args)} failed with exit code {result.returncode}")
    return result


def run_optional(
    args: list[str],
    *,
    cwd: Path = ROOT,
    env: dict[str, str] | None = None,
    timeout: float | None = None,
) -> subprocess.CompletedProcess[str] | None:
    try:
        return run(args, cwd=cwd, capture=True, check=False, env=env, timeout=timeout)
    except OSError:
        return None


def git(args: list[str], *, capture: bool = True, check: bool = True) -> str:
    result = run([exe("git"), *args], cwd=ROOT, capture=capture, check=check)
    return result.stdout if result.stdout is not None else ""


def ref_exists(ref: str) -> bool:
    return run([exe("git"), "rev-parse", "--verify", ref], capture=True, check=False).returncode == 0


def diff_range() -> str:
    base = os.environ.get("BASE_BRANCH", "main")
    candidates = [os.environ.get("BASE_REF"), f"origin/{base}", base]
    attempted: list[str] = []
    for candidate in candidates:
        if not candidate:
            continue
        attempted.append(candidate)
        if not ref_exists(candidate):
            continue
        result = run([exe("git"), "merge-base", candidate, "HEAD"], capture=True, check=False)
        if result.returncode == 0 and result.stdout.strip():
            return f"{result.stdout.strip()}...HEAD"
    tried = ", ".join(attempted)
    raise CheckError(f"diff_range: cannot determine diff base (tried {tried}); set BASE_REF or fetch the base branch")


def git_lines(args: list[str], *, label: str) -> list[str]:
    result = run([exe("git"), *args], cwd=ROOT, capture=True, check=False)
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        raise CheckError(f"{label}: git {' '.join(args)} failed: {detail}")
    return [line.strip().replace("\\", "/") for line in result.stdout.splitlines() if line.strip()]


def unique_paths(paths: Iterable[str]) -> list[str]:
    seen: set[str] = set()
    ordered: list[str] = []
    for path in paths:
        normalized = path.replace("\\", "/")
        if normalized in seen:
            continue
        seen.add(normalized)
        ordered.append(normalized)
    return ordered


def working_tree_paths() -> list[str]:
    return unique_paths(
        [
            *git_lines(["diff", "--name-only", "--cached"], label="working_tree_paths staged"),
            *git_lines(["diff", "--name-only"], label="working_tree_paths unstaged"),
            *git_lines(["ls-files", "--others", "--exclude-standard"], label="working_tree_paths untracked"),
        ]
    )


def changed_paths(range_spec: str | None = None) -> list[str]:
    range_spec = range_spec or diff_range()
    return unique_paths(
        [
            *git_lines(["diff", "--name-only", range_spec], label=f"changed_paths {range_spec}"),
            *working_tree_paths(),
        ]
    )


def changed_paths_since(ref: str) -> list[str]:
    if not ref_exists(f"{ref}^{{commit}}"):
        raise CheckError(f"changed-path scope: git ref not found: {ref}")
    return unique_paths(
        [
            *git_lines(["diff", "--name-only", f"{ref}..HEAD"], label=f"changed_paths_since {ref}..HEAD"),
            *working_tree_paths(),
        ]
    )


def changed_name_status(range_spec: str | None = None) -> list[list[str]]:
    range_spec = range_spec or diff_range()
    rows: list[list[str]] = []
    for args, label in (
        (["diff", "--name-status", range_spec], f"changed_name_status {range_spec}"),
        (["diff", "--name-status", "--cached"], "changed_name_status staged"),
        (["diff", "--name-status"], "changed_name_status unstaged"),
    ):
        rows.extend(line.split("\t") for line in git_lines(args, label=label))
    rows.extend(["A", path] for path in git_lines(["ls-files", "--others", "--exclude-standard"], label="changed_name_status untracked"))

    deduped: dict[str, list[str]] = {}
    for row in rows:
        if not row:
            continue
        key = row[-1].replace("\\", "/")
        deduped[key] = [part.replace("\\", "/") for part in row]
    return list(deduped.values())


def module_main(script_name: str, args: list[str], *, stdin_text: str | None = None) -> int:
    path = SCRIPTS / script_name
    module_name = f"_axis_{re.sub(r'[^A-Za-z0-9_]', '_', script_name)}"
    spec = importlib.util.spec_from_file_location(module_name, path)
    if spec is None or spec.loader is None:
        raise CheckError(f"Cannot load {script_name}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module

    old_argv = sys.argv
    old_stdin = sys.stdin
    try:
        sys.argv = [str(path), *args]
        if stdin_text is not None:
            import io

            sys.stdin = io.StringIO(stdin_text)
        spec.loader.exec_module(module)
        main = getattr(module, "main", None)
        if main is None:
            raise CheckError(f"{script_name} has no main()")
        return int(main())
    finally:
        sys.argv = old_argv
        sys.stdin = old_stdin


def run_module_check(script_name: str, args: list[str]) -> int:
    rc = module_main(script_name, args)
    return 0 if rc == 0 else 1


def iter_files(root: Path, suffixes: tuple[str, ...]) -> Iterable[Path]:
    return axis_repo.iter_files(root, suffixes)


def git_ls_files(pattern: str | None = None) -> list[str]:
    args = ["ls-files"]
    if pattern is not None:
        args.append(pattern)
    return [line for line in git(args).splitlines() if line.strip()]


def repo_files(pattern: str | None = None) -> list[str]:
    args = ["ls-files", "--cached", "--others", "--exclude-standard"]
    if pattern is not None:
        args.extend(["--", pattern])
    return unique_paths(git_lines(args, label=f"repo_files {pattern or '*'}"))


TEXT_ENCODING_SUFFIXES = {
    ".cs",
    ".cshtml",
    ".csproj",
    ".css",
    ".dockerignore",
    ".editorconfig",
    ".env",
    ".gitattributes",
    ".gitignore",
    ".graphql",
    ".html",
    ".http",
    ".js",
    ".json",
    ".jsonc",
    ".jsx",
    ".md",
    ".mjs",
    ".props",
    ".ps1",
    ".py",
    ".runsettings",
    ".scss",
    ".sh",
    ".sln",
    ".sql",
    ".svg",
    ".targets",
    ".ts",
    ".tsx",
    ".txt",
    ".xml",
    ".yaml",
    ".yml",
}
TEXT_ENCODING_FILENAMES = {
    ".editorconfig",
    ".gitattributes",
    ".gitignore",
    "Dockerfile",
    "Makefile",
    "pre-push",
}
TEXT_ENCODING_SKIP_PARTS = {".git", "bin", "coverage", "dist", "node_modules", "obj"}
UTF8_BOM = b"\xef\xbb\xbf"


def mojibake_marker(text: str) -> str:
    return text.encode("utf-8").decode("cp1252", errors="ignore")


MOJIBAKE_MARKERS = (
    "\ufffd",
    mojibake_marker("’"),
    mojibake_marker("“"),
    mojibake_marker("”"),
    mojibake_marker("–"),
    mojibake_marker("—"),
    mojibake_marker("→"),
    mojibake_marker("←"),
    mojibake_marker("✓"),
    mojibake_marker("✅"),
    mojibake_marker("⚠"),
    mojibake_marker("⏳"),
)


def should_check_text_encoding(path: str) -> bool:
    normalized = path.replace("\\", "/")
    if any(part in TEXT_ENCODING_SKIP_PARTS for part in normalized.split("/")):
        return False
    p = Path(normalized)
    return p.suffix.lower() in TEXT_ENCODING_SUFFIXES or p.name in TEXT_ENCODING_FILENAMES


def text_encoding_issues(paths: Iterable[Path], *, root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    for path in sorted(paths):
        if not path.is_file():
            continue
        normalized = str(path.relative_to(root)).replace("\\", "/")
        if not should_check_text_encoding(normalized):
            continue

        data = path.read_bytes()
        if data.startswith(UTF8_BOM):
            issues.append(f"{normalized}: UTF-8 BOM found - save as UTF-8 without BOM")

        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError as exc:
            issues.append(f"{normalized}: invalid UTF-8 byte at offset {exc.start}")
            continue

        if b"\r" in data:
            issues.append(f"{normalized}: CRLF/CR line ending found - use LF")

        for line_number, line in enumerate(text.splitlines(), 1):
            if any(marker in line for marker in MOJIBAKE_MARKERS):
                snippet = line.strip()
                if len(snippet) > 160:
                    snippet = f"{snippet[:157]}..."
                issues.append(f"{normalized}:{line_number}: mojibake marker found: {snippet}")
    return issues


def run_text_encoding_check(paths: list[str], *, label: str = "check-text-encoding") -> int:
    candidates = [ROOT / path for path in paths if should_check_text_encoding(path)]
    issues = text_encoding_issues(candidates, root=ROOT)
    if issues:
        print(f"{label} FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        print("\nUse UTF-8 without BOM and LF line endings for tracked text files.", file=sys.stderr)
        return 1
    print(f"{label}: OK ({len(candidates)} files scanned)")
    return 0


def check_text_encoding(_args: argparse.Namespace | None = None) -> int:
    return run_text_encoding_check(repo_files())


TEST_ATTRIBUTE_RE = re.compile(r"^\s*\[(?:Xunit[.])?(?:Fact|Theory)(?:Attribute)?(?:\s*[(]|\s*\])")
TEST_METHOD_RE = re.compile(
    r"\bpublic\s+"
    r"(?:(?:static|async|virtual|override|new|sealed)\s+)*"
    r"(?:void|(?:System[.]Threading[.]Tasks[.])?(?:Task|ValueTask)(?:<[^>]+>)?)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*[(]"
)
TEST_NAME_RE = re.compile(r"^[A-Z][A-Za-z0-9]*_[A-Z][A-Za-z0-9]*_[A-Z][A-Za-z0-9]*$")


def check_test_naming(_args: argparse.Namespace | None = None) -> int:
    issues: list[str] = []
    test_count = 0

    for path in sorted(iter_files(ROOT / "tests", (".cs",))):
        pending_attribute_line: int | None = None
        declaration = ""

        for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if TEST_ATTRIBUTE_RE.match(line):
                if pending_attribute_line is not None:
                    issues.append(
                        f"{rel(path)}:{pending_attribute_line}: [Fact]/[Theory] is not followed "
                        "by a supported public void/Task/ValueTask test method"
                    )
                pending_attribute_line = line_number
                declaration = line.split("]", 1)[1] if "]" in line else ""
            elif pending_attribute_line is not None:
                declaration = f"{declaration} {line.strip()}"
            else:
                continue

            method = TEST_METHOD_RE.search(declaration)
            if method is None:
                continue

            test_count += 1
            name = method.group("name")
            if not TEST_NAME_RE.fullmatch(name):
                issues.append(
                    f"{rel(path)}:{line_number}: {name} must match "
                    "{Subject}_{Condition}_{ExpectedOutcome} with exactly three PascalCase segments"
                )
            pending_attribute_line = None
            declaration = ""

        if pending_attribute_line is not None:
            issues.append(
                f"{rel(path)}:{pending_attribute_line}: [Fact]/[Theory] is not followed "
                "by a supported public void/Task/ValueTask test method"
            )

    if issues:
        print("check-test-naming FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1
    print(f"check-test-naming: OK ({test_count} tests scanned)")
    return 0


def check_test_project_classification(_args: argparse.Namespace | None = None) -> int:
    failed = False
    for project in repo_files("tests/**/*.csproj"):
        name = Path(project).stem
        allowed = (
            re.fullmatch(r"Axis\..*\.(Domain|Application)\.Tests", name)
            or re.fullmatch(r"Axis\..*\.Infrastructure\.Tests", name)
            or name in {"Axis.Api.Tests", "Axis.Architecture.Tests", "Axis.Testing"}
        )
        if not allowed:
            print(f"check-test-project-classification: {project} is not classified", file=sys.stderr)
            failed = True
    if failed:
        print(
            "check-test-project-classification: FAIL - rename the project to match the "
            "Unit/Integration/Architecture convention or update this guard",
            file=sys.stderr,
        )
        return 1
    print("check-test-project-classification: OK")
    return 0


def test_unit(args: argparse.Namespace) -> int:
    rc = check_dotnet_sdk()
    if rc != 0:
        return rc
    rc = check_test_project_classification()
    if rc != 0:
        return rc
    projects = [
        p
        for p in repo_files("tests/**/*.csproj")
        if re.search(r"/Axis\..*\.(Domain|Application)\.Tests/Axis\..*\.(Domain|Application)\.Tests\.csproj$", p)
    ]
    if not projects:
        print("test-unit: no unit test projects found", file=sys.stderr)
        return 1
    for project in projects:
        print()
        print(f"> dotnet test {project}")
        result = run([exe("dotnet"), "test", project, "--nologo", *args.dotnet_args], check=False)
        if result.returncode != 0:
            return result.returncode
    return 0


def check_vulnerable_packages(_args: argparse.Namespace | None = None) -> int:
    rc = check_dotnet_sdk()
    if rc != 0:
        return rc
    solution_path = ROOT / "Axis.sln"
    result = run(
        [exe("dotnet"), "list", str(solution_path), "package", "--vulnerable", "--include-transitive"],
        capture=True,
        check=False,
    )
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr)
    if result.returncode != 0:
        print("check-vulnerable-packages: FAIL - dotnet vulnerable package scan failed", file=sys.stderr)
        return result.returncode
    if "has the following vulnerable packages" in result.stdout:
        print("check-vulnerable-packages: FAIL - vulnerable NuGet packages found", file=sys.stderr)
        return 1
    print("check-vulnerable-packages: OK")
    return 0


def check_frontend_vulnerable_packages(_args: argparse.Namespace | None = None) -> int:
    rc = check_frontend_toolchain()
    if rc != 0:
        return rc

    result = run_frontend_npm(["audit", "--audit-level=high"])
    if result.returncode != 0:
        print(
            "check-frontend-vulnerable-packages: FAIL - npm reported a high or critical vulnerability",
            file=sys.stderr,
        )
        return result.returncode

    print("check-frontend-vulnerable-packages: OK")
    return 0


def scan_text_files(
    roots: list[Path],
    suffixes: tuple[str, ...],
    pattern: str,
    *,
    exclude: Iterable[str] = (),
) -> list[str]:
    rx = re.compile(pattern)
    excluded = tuple(exclude)
    hits: list[str] = []
    for root in roots:
        for path in iter_files(root, suffixes):
            normalized = rel(path)
            if any(part in normalized for part in excluded):
                continue
            try:
                text = path.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                continue
            for idx, line in enumerate(text.splitlines(), 1):
                if rx.search(line):
                    hits.append(f"{normalized}:{idx}:{line}")
    return hits


def check_ef_domain_mapping(_args: argparse.Namespace | None = None) -> int:
    issues: list[str] = []
    for hit in scan_text_files(
        [ROOT / "src", ROOT / "tests"],
        (".cs",),
        r'EF[.]Property<[^>]+>\s*[(][^,]+,\s*"_[A-Za-z0-9]+',
        exclude=("Generated/",),
    ):
        issues.append(
            "EF.Property query against private/shadow field hides a persistence concern "
            f"behind a magic string. Model the relationship explicitly: {hit}"
        )
    for hit in scan_text_files(
        [ROOT / "src", ROOT / "tests"],
        (".cs",),
        r"PrimitiveCollection<List<Guid>>",
        exclude=("Generated/",),
    ):
        issues.append(f"PrimitiveCollection<List<Guid>> stores relationship ids as an array. Use an entity/join table instead: {hit}")
    if issues:
        for issue in issues:
            print(f"check-ef-domain-mapping FAIL: {issue}", file=sys.stderr)
        print("\nSee docs/playbooks/testing.md#database-rules", file=sys.stderr)
        return 1
    print("check-ef-domain-mapping: OK")
    return 0


def check_frontend_api_contracts(_args: argparse.Namespace | None = None) -> int:
    pattern = re.compile(r"(^|\s)(export\s+)?(interface|type)\s+[A-Za-z0-9_]*(Request|Response|Dto)\b")
    generated_contract_pattern = re.compile(r"(components|operations)\[['\"]")
    issues: list[str] = []
    root = ROOT / "frontend" / "src"
    for path in iter_files(root, (".ts", ".tsx")):
        normalized = rel(path)
        if normalized.endswith("frontend/src/lib/api-types.ts") or normalized.endswith("frontend/src/routeTree.gen.ts"):
            continue
        text = path.read_text(encoding="utf-8")
        lines = text.splitlines()
        for idx, line in enumerate(lines, 1):
            if not pattern.search(line):
                continue
            statement = line
            for continuation in lines[idx : min(idx + 8, len(lines))]:
                if ";" in statement or "{" in statement:
                    break
                statement = f"{statement}\n{continuation}"
            if generated_contract_pattern.search(statement):
                continue
            issues.append(f"{normalized}:{idx}:{line}")
    if issues:
        for hit in issues:
            print(
                "check-frontend-api-contracts FAIL: Hand-authored frontend API model. "
                f"Import/alias from generated api-types.ts instead: {hit}",
                file=sys.stderr,
            )
        print("\nRun python scripts/axis.py generate api-contracts after API contract changes.", file=sys.stderr)
        return 1
    print("check-frontend-api-contracts: OK")
    return 0


def ui_baseline_payload(root: Path = ROOT) -> dict[str, object]:
    frontend_root = root / "frontend"
    config_path = frontend_root / "components.json"
    theme_path = frontend_root / "src" / "index.css"
    ui_root = frontend_root / "src" / "components" / "ui"
    required = (config_path, theme_path)
    missing = [str(path.relative_to(root)).replace("\\", "/") for path in required if not path.is_file()]
    if missing:
        raise CheckError(f"missing UI baseline source: {', '.join(missing)}")

    try:
        config = json.loads(config_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as exc:
        raise CheckError(f"invalid frontend/components.json: {exc}") from exc
    if not isinstance(config, dict):
        raise CheckError("invalid frontend/components.json: root value must be an object")

    support_paths = [
        frontend_root / "src" / "hooks" / "use-mobile.ts",
        frontend_root / "src" / "lib" / "utils.ts",
    ]
    paths = [
        config_path,
        theme_path,
        *(path for path in support_paths if path.is_file()),
        *sorted(ui_root.rglob("*.tsx")),
    ]
    files = {
        str(path.relative_to(frontend_root)).replace("\\", "/"): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in paths
    }
    return {
        "schemaVersion": 2,
        "registry": "@shadcn",
        "style": config.get("style"),
        "files": files,
    }


def ui_baseline_issues(root: Path = ROOT) -> list[str]:
    baseline_path = root / "frontend" / "ui-baseline.json"
    if not baseline_path.is_file():
        return [
            "frontend/ui-baseline.json: approved UI baseline is missing; "
            "create it only after registry/default review and sign-off"
        ]

    try:
        approved = json.loads(baseline_path.read_text(encoding="utf-8"))
        current = ui_baseline_payload(root)
    except (json.JSONDecodeError, OSError, CheckError) as exc:
        return [f"frontend/ui-baseline.json: cannot validate approved UI baseline: {exc}"]
    if not isinstance(approved, dict):
        return ["frontend/ui-baseline.json: root value must be an object"]

    issues: list[str] = []
    for field in ("schemaVersion", "registry", "style"):
        if approved.get(field) != current[field]:
            issues.append(
                f"frontend/ui-baseline.json: `{field}` is {approved.get(field)!r}, "
                f"current value is {current[field]!r}"
            )

    approved_files = approved.get("files")
    if not isinstance(approved_files, dict):
        return ["frontend/ui-baseline.json: `files` must be an object of path-to-SHA256 entries"]

    exceptions = approved.get("exceptions")
    if not isinstance(exceptions, dict):
        issues.append("frontend/ui-baseline.json: `exceptions` must be an object")
        exceptions = {}
    for path, decision in sorted(exceptions.items()):
        if path not in approved_files:
            issues.append(f"frontend/ui-baseline.json: exception `{path}` is not a tracked baseline file")
        if not isinstance(decision, dict) or any(
            not isinstance(decision.get(field), str) or not decision[field].strip()
            for field in ("reason", "signOff")
        ):
            issues.append(
                f"frontend/ui-baseline.json: exception `{path}` requires non-empty `reason` and `signOff`"
            )

    current_files = current["files"]
    assert isinstance(current_files, dict)
    for path in sorted(set(approved_files) | set(current_files)):
        if path not in approved_files:
            issues.append(f"frontend/{path}: UI baseline has an unreviewed tracked file")
        elif path not in current_files:
            issues.append(f"frontend/{path}: approved UI baseline file is missing")
        elif approved_files[path] != current_files[path]:
            issues.append(f"frontend/{path}: approved UI baseline drift")
    return issues


def write_ui_baseline(root: Path = ROOT) -> None:
    baseline_path = root / "frontend" / "ui-baseline.json"
    exceptions: dict[str, object] = {}
    if baseline_path.is_file():
        try:
            existing = json.loads(baseline_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError) as exc:
            raise CheckError(f"cannot preserve existing UI baseline: {exc}") from exc
        if not isinstance(existing, dict):
            raise CheckError("cannot preserve existing UI baseline: root value must be an object")
        existing_exceptions = existing.get("exceptions")
        if not isinstance(existing_exceptions, dict):
            raise CheckError("cannot preserve existing UI baseline: `exceptions` must be an object")
        exceptions = existing_exceptions
    payload = ui_baseline_payload(root)
    payload["exceptions"] = exceptions
    baseline_path.write_text(
        f"{json.dumps(payload, indent=2, sort_keys=True)}\n",
        encoding="utf-8",
    )
    print(f"ui-baseline: wrote {baseline_path.relative_to(root)}")


def check_ui_baseline(_args: argparse.Namespace | None = None) -> int:
    issues = ui_baseline_issues()
    if issues:
        print("check-ui-baseline FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        print(
            "\nReview registry/theme changes and required sign-off before running "
            "`python scripts/axis.py frontend ui-baseline --write`.",
            file=sys.stderr,
        )
        return 1
    print("check-ui-baseline: OK")
    return 0



NAVIGATION_RE = re.compile(r"^> \*\*Navigation\*\*: .*\[[^\]]+\]\([^)]+\)")
NAVIGATION_LINK_RE = re.compile(r"\[([^\]]+)\]\(([^)]+)\)")
NAVIGATION_LABEL_RE = re.compile(r"^(?:[A-Za-z0-9._-]+/)*[A-Za-z0-9._-]+[.]md(?:#[A-Za-z0-9._-]+)?$")


def doc_navigation_line_issues(path: Path, line: str) -> list[str]:
    issues: list[str] = []
    if any(token in line for token in ("<-", "←", "|")) or " . " in line:
        issues.append(f"{rel(path)}: navigation uses non-standard separators or arrows")

    links = NAVIGATION_LINK_RE.findall(line)
    if not links:
        issues.append(f"{rel(path)}: navigation block must include at least one markdown link")
        return issues

    expected_separator_count = max(len(links) - 1, 0)
    if line.count(" · ") != expected_separator_count:
        issues.append(f"{rel(path)}: navigation links must be separated with ` · `")

    for label, target in links:
        if not NAVIGATION_LABEL_RE.match(label):
            issues.append(f"{rel(path)}: navigation link label must be a repo markdown path: `{label}`")
        if not re.search(r"[.]md(?:#[-A-Za-z0-9_]+)?$", target):
            issues.append(f"{rel(path)}: navigation target must point to a markdown file: `{target}`")

    return issues


def check_doc_navigation(_args: argparse.Namespace | None = None) -> int:
    issues: list[str] = []
    files = sorted((ROOT / "docs").rglob("*.md"))
    for path in files:
        text = path.read_text(encoding="utf-8")
        lines = text.splitlines()
        if not lines or not lines[0].startswith("# "):
            issues.append(f"{rel(path)}: first line must be an H1 heading")
            continue
        window = lines[1:6]
        nav_lines = [line for line in window if line.startswith("> **Navigation**:")]
        if not nav_lines:
            issues.append(f"{rel(path)}: missing `> **Navigation**:` block immediately after the H1")
            continue
        nav_line = nav_lines[0]
        if not NAVIGATION_RE.search(nav_line):
            issues.append(f"{rel(path)}: navigation block must include at least one markdown link")
            continue
        issues.extend(doc_navigation_line_issues(path, nav_line))
    if issues:
        print("check-doc-navigation FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1
    print(f"check-doc-navigation: OK ({len(files)} files scanned)")
    return 0


PLAYBOOK_DEFAULT_MAX_LINES = 100
PATTERN_ROUTER_MAX_LINES = 100
DOC_SIZE_BUDGETS: dict[str, int] = {}


def doc_size_budget_issues(*, root: Path = ROOT) -> list[str]:
    candidates = [root / "AGENTS.md", root / "docs" / "ARCHITECTURE.md"]
    playbooks = sorted((root / "docs" / "playbooks").glob("*.md"))
    candidates.extend(playbooks)

    issues: list[str] = []
    for path in candidates:
        if not path.is_file():
            continue
        normalized = rel_from(path, root)
        limit = DOC_SIZE_BUDGETS.get(normalized, PLAYBOOK_DEFAULT_MAX_LINES)
        line_count = len(path.read_text(encoding="utf-8").splitlines())
        if line_count > limit:
            issues.append(
                f"{normalized}: {line_count} lines exceeds {limit}-line docs budget; "
                "split by topic or move repeatable workflow into a repo skill"
            )
    return issues


def check_doc_size_budgets(_args: argparse.Namespace | None = None) -> int:
    issues = doc_size_budget_issues()
    if issues:
        print("check-doc-size-budgets FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1
    print("check-doc-size-budgets: OK")
    return 0


NON_PYTHON_UTILITY_SCRIPT_SUFFIXES = {".mjs", ".js", ".ps1", ".sh", ".cmd", ".bat"}
DOCS_UTILITY_SCRIPT_ROOTS = (
    Path("docs/scripts"),
    Path("docs/diagrams"),
)


def non_python_utility_script_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []

    def local_rel(path: Path) -> str:
        return str(path.relative_to(root)).replace("\\", "/")

    scripts_dir = root / "scripts"
    for path in sorted(scripts_dir.iterdir()) if scripts_dir.exists() else []:
        if not path.is_file():
            continue
        if path.suffix.lower() == ".py":
            if os.name != "nt" and path.stat().st_mode & 0o111:
                issues.append(
                    f"{local_rel(path)}: top-level Python scripts must not be executable; "
                    "run them through scripts/axis.py"
                )
            continue
        issues.append(
            f"{local_rel(path)}: top-level scripts must be Python entrypoints; "
            "native tooling belongs beside its owning package"
        )

    for utility_root in DOCS_UTILITY_SCRIPT_ROOTS:
        full_root = root / utility_root
        if not full_root.exists():
            continue
        for path in sorted(full_root.rglob("*")):
            if path.is_file() and path.suffix.lower() in NON_PYTHON_UTILITY_SCRIPT_SUFFIXES:
                issues.append(
                    f"{local_rel(path)}: docs-level utility scripts must be Python; "
                    "native tooling belongs beside its owning package"
                )

    hook = scripts_dir / "hooks" / "pre-push"
    if hook.is_file():
        text = hook.read_text(encoding="utf-8", errors="ignore")
        first_line = text.splitlines()[0] if text.splitlines() else ""
        if "python" not in first_line.lower():
            issues.append(f"{local_rel(hook)}: pre-push hook must be a Python entrypoint")
        if "axis.py" not in text:
            issues.append(f"{local_rel(hook)}: pre-push hook must delegate to scripts/axis.py")
        if os.name != "nt" and hook.stat().st_mode & 0o111:
            issues.append(
                f"{local_rel(hook)}: committed hook source must not be executable; "
                "install-hooks writes the executable copy under .git/hooks"
            )
    return issues


def check_scripts_standard(_args: argparse.Namespace | None = None) -> int:
    issues = non_python_utility_script_issues()
    if issues:
        print("check-scripts-standard FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1
    print("check-scripts-standard: OK")
    return 0


SKILL_NAME_RE = re.compile(r"^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")
REPO_SKILLS_DIR = ".agents/skills"
FRONTMATTER_RE = re.compile(r"\A---\n(?P<header>.*?)\n---\n", re.DOTALL)
SKILL_MAX_LINES = 80
SKILL_AMBIGUOUS_WORD_RE = re.compile(
    r"\b(best[- ]effort|if you have time|nice to have|maybe|probably|hopefully)\b",
    re.IGNORECASE,
)
SKILL_REPO_REF_RE = re.compile(
    r"`(?P<target>(?:AGENTS\.md|\.agents/skills/[A-Za-z0-9._/#-]+|\.github/[A-Za-z0-9._/#-]+|"
    r"docs/[A-Za-z0-9._/#-]+|scripts/[A-Za-z0-9._/#-]+|tests/[A-Za-z0-9._/#-]+|"
    r"frontend/[A-Za-z0-9._/#-]+))`"
)
SKILL_MD_LINK_RE = re.compile(r"\[[^\]]+\]\((?P<target>[^)]+)\)")
SKILL_REQUIRED_HEADINGS = ("## Goal", "## Hard gates", "## Inputs", "## Workflow", "## Output")
SKILL_ALIAS_RE = re.compile(r"(?<![A-Za-z0-9_-])\$(?P<name>axis-[a-z0-9-]+)(?![A-Za-z0-9_-])")
SKILL_REQUIRES_RE = re.compile(r"[*][*]Requires[*][*]")
SKILL_HANDOFF_WORD_RE = re.compile(r"\b(?:requires?|delegat(?:e|es|ed|ing)|returns?\s+to)\b", re.IGNORECASE)
SKILL_TYPED_HANDOFF_RE = re.compile(r"[*][*](?:Requires|Delegates|Returns to)[*][*]")
SKILL_CATALOG_LINK_RE = re.compile(r"\]\(\./(?P<name>axis-[a-z0-9-]+)/SKILL[.]md(?:#[^)]+)?\)")
SKILL_DUPLICATE_INSTRUCTION_MIN_LENGTH = 80


def simple_yaml_value(text: str, key: str) -> str:
    match = re.search(rf"(?m)^\s*{re.escape(key)}:\s*(?P<value>.+?)\s*$", text)
    if match is None:
        return ""
    value = match.group("value").strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in {"'", '"'}:
        return value[1:-1]
    return value


def markdown_anchor_slug(text: str) -> str:
    text = re.sub(r"`([^`]*)`", r"\1", text.strip().lower())
    text = re.sub(r"<[^>]+>", "", text)
    text = re.sub(r"[^\w\s-]", "", text)
    text = re.sub(r"\s+", "-", text)
    return text.strip("-")


def markdown_anchor_slugs(path: Path) -> set[str]:
    slugs: set[str] = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        match = re.match(r"^(?P<marks>#{1,6})\s+(?P<title>.+?)\s*#*\s*$", line)
        if match is not None:
            slugs.add(markdown_anchor_slug(match.group("title")))
    return slugs


def skill_reference_target(target: str, *, skill_dir: Path, root: Path) -> tuple[Path, str] | None:
    target = urllib.parse.unquote(target.strip())
    if not target or target.startswith(("#", "http://", "https://", "mailto:")):
        return None
    if any(token in target for token in ("{", "}", "*")):
        return None

    path_part = target.split("#", 1)[0].split("?", 1)[0]
    if not path_part:
        return None

    normalized = path_part.replace("\\", "/")
    repo_prefixes = ("AGENTS.md", f"{REPO_SKILLS_DIR}/", ".github/", "docs/", "scripts/", "tests/", "frontend/")
    if normalized.startswith(repo_prefixes):
        return root / normalized, target
    return (skill_dir / normalized).resolve(), target


def repo_skill_reference_issues(skill_md: Path, text: str, *, root: Path) -> list[str]:
    issues: list[str] = []
    seen: set[str] = set()
    skill_dir = skill_md.parent
    candidates = [match.group("target") for match in SKILL_REPO_REF_RE.finditer(text)]
    candidates.extend(match.group("target") for match in SKILL_MD_LINK_RE.finditer(text))

    for target in candidates:
        resolved = skill_reference_target(target, skill_dir=skill_dir, root=root)
        if resolved is None:
            continue
        path, display = resolved
        key = f"{path}#{display}"
        if key in seen:
            continue
        seen.add(key)

        path_without_anchor = Path(str(path).split("#", 1)[0])
        if not path_without_anchor.exists():
            issues.append(
                f"{rel_from(skill_md, root)}: referenced path `{display.split('#', 1)[0]}` does not exist"
            )
            continue

        if "#" in display and path_without_anchor.suffix.lower() == ".md":
            anchor = display.split("#", 1)[1]
            if anchor and anchor not in markdown_anchor_slugs(path_without_anchor):
                issues.append(f"{rel_from(skill_md, root)}: referenced anchor `{display}` does not exist")

    return issues


def repo_skill_raw_command_issues(skill_md: Path, text: str, *, root: Path) -> list[str]:
    issues: list[str] = []
    fence_lang: str | None = None
    for idx, line in enumerate(text.splitlines(), 1):
        fence_match = re.match(r"^```([A-Za-z0-9_-]*)", line.strip())
        if fence_match:
            fence_lang = None if fence_lang is not None else fence_match.group(1).lower()
            continue

        if fence_lang in {"bash", "sh", "shell", "console"}:
            message = raw_doc_command_message(line)
            if message is not None:
                issues.append(
                    f"{rel_from(skill_md, root)}:{idx}: raw skill workflow command `{line.strip()}` - {message}"
                )

        for fragment in re.findall(r"`([^`]+)`", line):
            message = raw_doc_command_message(fragment)
            if message is not None:
                issues.append(f"{rel_from(skill_md, root)}:{idx}: raw skill workflow command `{fragment}` - {message}")
    return issues


def repo_skill_catalog_issues(skills_root: Path, skill_names: set[str], *, root: Path) -> list[str]:
    catalog_path = skills_root / "README.md"
    if not catalog_path.is_file():
        return [f"{rel_from(catalog_path, root)}: responsibility catalog is missing"]

    counts: dict[str, int] = {}
    for match in SKILL_CATALOG_LINK_RE.finditer(catalog_path.read_text(encoding="utf-8")):
        name = match.group("name")
        counts[name] = counts.get(name, 0) + 1

    issues: list[str] = []
    for name in sorted(skill_names):
        count = counts.get(name, 0)
        if count == 0:
            issues.append(f"{rel_from(catalog_path, root)}: missing responsibility entry for `{name}`")
        elif count > 1:
            issues.append(f"{rel_from(catalog_path, root)}: `{name}` must have exactly one responsibility entry")
    for name in sorted(set(counts) - skill_names):
        issues.append(f"{rel_from(catalog_path, root)}: catalog references unknown skill `{name}`")
    return issues


def repo_skill_duplicate_instruction_issues(
    records: list[tuple[Path, str]], *, root: Path
) -> list[str]:
    locations: dict[str, list[tuple[Path, int]]] = {}
    for skill_md, text in records:
        in_fence = False
        for idx, line in enumerate(text.splitlines(), 1):
            if line.strip().startswith("```"):
                in_fence = not in_fence
                continue
            normalized = re.sub(r"\s+", " ", line.strip())
            if (
                in_fence
                or not normalized.startswith("- ")
                or len(normalized) < SKILL_DUPLICATE_INSTRUCTION_MIN_LENGTH
            ):
                continue
            locations.setdefault(normalized, []).append((skill_md, idx))

    issues: list[str] = []
    for instruction, duplicates in sorted(locations.items()):
        if len(duplicates) < 2:
            continue
        owner_path, owner_line = duplicates[0]
        owner = f"{rel_from(owner_path, root)}:{owner_line}"
        for duplicate_path, duplicate_line in duplicates[1:]:
            issues.append(
                f"{rel_from(duplicate_path, root)}:{duplicate_line}: duplicate substantive instruction; "
                f"move it to one owner and link `{owner}`: {instruction}"
            )
    return issues


def repo_skill_required_cycle_issues(records: list[tuple[Path, str]]) -> list[str]:
    skill_names = {path.parent.name for path, _text in records}
    graph: dict[str, set[str]] = {name: set() for name in skill_names}
    for skill_md, text in records:
        source = skill_md.parent.name
        for line in text.splitlines():
            if SKILL_REQUIRES_RE.search(line) is None:
                continue
            graph[source].update(
                match.group("name")
                for match in SKILL_ALIAS_RE.finditer(line)
                if match.group("name") in skill_names
            )

    issues: list[str] = []
    visited: set[str] = set()
    visiting: list[str] = []
    reported: set[frozenset[str]] = set()

    def visit(name: str) -> None:
        if name in visiting:
            cycle = visiting[visiting.index(name) :] + [name]
            key = frozenset(cycle)
            if key not in reported:
                reported.add(key)
                issues.append(
                    f"{REPO_SKILLS_DIR}: recursive **Requires** handoff: {' -> '.join(cycle)}; "
                    "make one edge a delegate/return or reuse current prerequisite evidence"
                )
            return
        if name in visited:
            return
        visiting.append(name)
        for target in sorted(graph[name]):
            visit(target)
        visiting.pop()
        visited.add(name)

    for name in sorted(graph):
        visit(name)
    return issues


def repo_skill_issues(*, root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    skills_root = root / REPO_SKILLS_DIR.replace("/", os.sep)
    if not skills_root.exists():
        return issues

    skill_dirs = sorted(path for path in skills_root.iterdir() if path.is_dir())
    skill_names = {path.name for path in skill_dirs}
    records: list[tuple[Path, str]] = []

    for skill_dir in skill_dirs:
        skill_path = rel_from(skill_dir, root)

        skill_name = skill_dir.name
        if SKILL_NAME_RE.fullmatch(skill_name) is None:
            issues.append(f"{skill_path}: skill folder name must be lowercase letters, digits, and hyphens")

        legacy_adapter = skill_dir / "agents"
        if legacy_adapter.is_dir():
            issues.append(
                f"{skill_path}: remove legacy agents/ vendor metadata; "
                f"repo skills use {REPO_SKILLS_DIR}/<name>/SKILL.md only"
            )

        skill_md = skill_dir / "SKILL.md"
        if not skill_md.is_file():
            issues.append(f"{skill_path}: missing SKILL.md")
            continue

        text = skill_md.read_text(encoding="utf-8")
        records.append((skill_md, text))
        if "TODO" in text or "[TODO" in text:
            issues.append(f"{rel_from(skill_md, root)}: remove template TODO text before committing")
        line_count = len(text.splitlines())
        if line_count > SKILL_MAX_LINES:
            issues.append(
                f"{rel_from(skill_md, root)}: keep SKILL.md concise ({line_count} lines > {SKILL_MAX_LINES})"
            )
        for idx, line in enumerate(text.splitlines(), 1):
            if SKILL_AMBIGUOUS_WORD_RE.search(line):
                issues.append(
                    f"{rel_from(skill_md, root)}:{idx}: replace ambiguous best-effort wording with a concrete action"
                )
            if (
                SKILL_ALIAS_RE.search(line)
                and SKILL_HANDOFF_WORD_RE.search(line)
                and SKILL_TYPED_HANDOFF_RE.search(line) is None
            ):
                issues.append(
                    f"{rel_from(skill_md, root)}:{idx}: type the skill handoff as "
                    "**Requires**, **Delegates**, or **Returns to**"
                )
        issues.extend(repo_skill_raw_command_issues(skill_md, text, root=root))

        frontmatter = FRONTMATTER_RE.match(text)
        if frontmatter is None:
            issues.append(f"{rel_from(skill_md, root)}: missing YAML frontmatter delimited by ---")
            continue

        header = frontmatter.group("header")
        header_keys = set(re.findall(r"(?m)^([A-Za-z0-9_-]+):", header))
        unsupported_keys = sorted(header_keys - {"name", "description"})
        if unsupported_keys:
            issues.append(
                f"{rel_from(skill_md, root)}: frontmatter supports only `name` and `description`; "
                f"remove {unsupported_keys}"
            )
        declared_name = simple_yaml_value(header, "name")
        description = simple_yaml_value(header, "description")
        if declared_name != skill_name:
            issues.append(
                f"{rel_from(skill_md, root)}: frontmatter name must match folder name "
                f"({skill_name})"
            )
        if not description:
            issues.append(f"{rel_from(skill_md, root)}: frontmatter description is required")
        elif len(description) < 40:
            issues.append(f"{rel_from(skill_md, root)}: frontmatter description is too vague")

        body = text[frontmatter.end() :]
        if re.search(r"(?m)^#\s+\S", body) is None:
            issues.append(f"{rel_from(skill_md, root)}: body must contain a Markdown H1")
        for heading in SKILL_REQUIRED_HEADINGS:
            if re.search(rf"(?m)^{re.escape(heading)}\s*$", body) is None:
                issues.append(
                    f"{rel_from(skill_md, root)}: missing required section `{heading}`"
                )
        if "../reference.md" not in text:
            issues.append(f"{rel_from(skill_md, root)}: must link the universal `../reference.md` contract")
        for alias in sorted({match.group("name") for match in SKILL_ALIAS_RE.finditer(text)}):
            if alias not in skill_names:
                issues.append(f"{rel_from(skill_md, root)}: unknown skill alias `${alias}`")
        issues.extend(repo_skill_reference_issues(skill_md, text, root=root))

    issues.extend(repo_skill_catalog_issues(skills_root, skill_names, root=root))
    issues.extend(repo_skill_duplicate_instruction_issues(records, root=root))
    issues.extend(repo_skill_required_cycle_issues(records))
    return issues


def check_repo_skills(_args: argparse.Namespace | None = None) -> int:
    issues = repo_skill_issues()
    if issues:
        print("check-repo-skills FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1
    print("check-repo-skills: OK")
    return 0


def check_policy_tests(_args: argparse.Namespace | None = None) -> int:
    return run(
        [
            sys.executable,
            "-m",
            "unittest",
            "discover",
            "-s",
            "scripts/tests",
            "-p",
            "test_*.py",
        ],
        check=False,
    ).returncode


def parse_added_lines(diff_text: str, include: callable[[str], bool]) -> list[tuple[str, str]]:
    current = ""
    rows: list[tuple[str, str]] = []
    for line in diff_text.splitlines():
        if line.startswith("+++ b/"):
            current = line[6:].replace("\\", "/")
            continue
        if not current or not include(current):
            continue
        if line.startswith("+") and not line.startswith("+++ "):
            rows.append((current, line[1:]))
    return rows


def untracked_added_lines(include: callable[[str], bool]) -> list[tuple[str, str]]:
    rows: list[tuple[str, str]] = []
    for path in git_lines(["ls-files", "--others", "--exclude-standard"], label="untracked_added_lines"):
        if not include(path):
            continue
        full_path = ROOT / path
        if not full_path.is_file():
            continue
        try:
            lines = full_path.read_text(encoding="utf-8", errors="replace").splitlines()
        except OSError:
            continue
        rows.extend((path, line) for line in lines)
    return rows


def added_lines(range_spec: str, include: callable[[str], bool]) -> Iterable[tuple[str, str]]:
    rows: list[tuple[str, str]] = []
    result = run([exe("git"), "diff", "--unified=0", range_spec], capture=True, check=False)
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        raise CheckError(f"added_lines: git diff failed for {range_spec}: {detail}")
    rows.extend(parse_added_lines(result.stdout, include))

    for args, label in (
        (["diff", "--unified=0", "--cached"], "added_lines staged"),
        (["diff", "--unified=0"], "added_lines unstaged"),
    ):
        local_result = run([exe("git"), *args], capture=True, check=False)
        if local_result.returncode != 0:
            detail = (local_result.stderr or local_result.stdout or "").strip()
            raise CheckError(f"{label}: git {' '.join(args)} failed: {detail}")
        rows.extend(parse_added_lines(local_result.stdout, include))

    rows.extend(untracked_added_lines(include))
    return rows


def any_changed(paths: list[str], pattern: str) -> bool:
    rx = re.compile(pattern)
    return any(rx.search(path) for path in paths)


def docs_changed_under(paths: list[str], prefix: str) -> bool:
    return any(path.startswith(prefix) for path in paths)


def fail(issues: list[str], message: str) -> None:
    issues.append(message)
    print(f"check-doc-drift FAIL: {message}", file=sys.stderr)


DOC_DRIFT_ADDED_LINE_RULES = [
    (
        r"(?:/mnt/[a-z]/(?:[^`\s]+/)*projects/|[A-Za-z]:\\(?:Users|projects)\\|/Users/[^`\s]+/|/home/[^`\s]+/)",
        lambda p: (p.startswith("docs/") or p.startswith("scripts/"))
        and not p.startswith("scripts/tests/")
        and p.endswith((".md", ".py", ".ps1", ".sh", ".yml", ".yaml")),
        "Machine-specific local path introduced - use <repo-root> or state 'from the repo root'",
    ),
    (
        r"(?:^|[`>\s])docker compose\s+(?:--profile\s+\S+\s+)?(?:up|down|start|stop|logs|ps|restart|exec|run|build|pull)\b",
        lambda p: p.startswith("docs/") and p.endswith(".md"),
        "Raw Docker Compose command introduced in docs - add/use a scripts/axis.py local-dev command or a focused Axis wrapper",
    ),
    (
        r"GetAwaiter[(][)][.]GetResult[(][)]",
        lambda p: p.startswith("src/") and p.endswith(".cs"),
        "Sync-over-async introduced - make the caller async instead",
    ),
    (
        r'"(Host=|Server=|Data Source=)',
        lambda p: p.startswith("src/") and p.endswith(".cs"),
        "Hardcoded connection string introduced - use configuration/options",
    ),
    (
        r"DateTime[.]Now",
        lambda p: (p.startswith("src/") or p.startswith("tests/")) and p.endswith(".cs"),
        "DateTime.Now introduced - use DateTimeOffset.UtcNow / TimeProvider",
    ),
    (
        r"[.]Produces<object>|Results[.](Ok|Json|Created|Accepted)[(]new[ ]*[{]",
        lambda p: p.startswith("src/Axis.Api/Endpoints/") and p.endswith(".cs"),
        "Endpoint returns object/anonymous JSON - use a named Application-layer DTO (docs/ENFORCEMENT.md)",
    ),
    (
        r"\bSkip\s*=",
        lambda p: p.startswith("tests/") and p.endswith(".cs"),
        "Skipped test introduced - fix or remove the test instead",
    ),
    (
        r"[.]EnsureCreated(?:Async)?[(]",
        lambda p: (p.startswith("src/") or p.startswith("tests/")) and p.endswith(".cs"),
        "Database setup must use the owning DbContext migration chain, not EnsureCreated",
    ),
    (
        r"\b(?:TODO|FIXME|NotImplementedException|stub)\b|(?<![:\w-])placeholder(?!\s*=)(?![:\w-])",
        lambda p: (p.startswith("src/") or p.startswith("tests/") or p.startswith("frontend/src/"))
        and "/obj/" not in p
        and "/node_modules/" not in p,
        "New TODO/FIXME/stub marker introduced - resolve or open an issue",
    ),
]

DOC_COMMAND_DOC_PATHS = {"AGENTS.md", "README.md", "CONTRIBUTING.md"}

RAW_DOC_COMMAND_PATTERNS = [
    (
        re.compile(r"^(?:cd\s+\S+\s+&&\s+)?dotnet\s+(?:restore|build|test|format|run|ef)\b"),
        "use `python scripts/axis.py dotnet ...`",
    ),
    (
        re.compile(r"^(?:cd\s+\S+\s+&&\s+)?npm\s+(?:ci|run|test|install)\b"),
        "use `python scripts/axis.py frontend ...`",
    ),
    (
        re.compile(r"^npx\b"),
        "use an approved project wrapper, or document external tool setup in the owning playbook",
    ),
    (
        re.compile(r"^docker\s+(?:compose|info)\b"),
        "use `python scripts/axis.py local-dev ...` or `python scripts/axis.py check docker`",
    ),
    (re.compile(r"^openssl\b"), "use `python scripts/axis.py local-dev certs`"),
    (re.compile(r"^python\s+docs/scripts/"), "use an approved project wrapper"),
    (re.compile(r"^lychee\s+--version\b"), "use `python scripts/axis.py check markdown-links` or `python scripts/axis.py doctor`"),
    (re.compile(r"^cargo\s+install\s+lychee\b"), "install tools externally, then verify through `python scripts/axis.py doctor`"),
]

def is_command_doc(path: str) -> bool:
    return path in DOC_COMMAND_DOC_PATHS or (path.startswith("docs/") and path.endswith(".md"))


def normalize_doc_command_fragment(fragment: str) -> str:
    text = fragment.strip()
    text = re.sub(r"^(?:[$>]|\#)\s*", "", text)
    text = re.sub(r"^cd\s+frontend\s+&&\s+", "cd frontend && ", text)
    return text


def raw_doc_command_message(fragment: str) -> str | None:
    normalized = normalize_doc_command_fragment(fragment)
    if not normalized or normalized.startswith("python scripts/axis.py "):
        return None
    for pattern, replacement in RAW_DOC_COMMAND_PATTERNS:
        if pattern.search(normalized):
            return replacement
    return None


def documented_raw_command_issues(paths: Iterable[str] | None = None, *, root: Path = ROOT) -> list[str]:
    candidates = paths or repo_files()
    issues: list[str] = []
    for path in sorted(candidates):
        normalized = path.replace("\\", "/")
        if not is_command_doc(normalized):
            continue
        full_path = root / normalized
        if not full_path.is_file():
            continue
        text = full_path.read_text(encoding="utf-8", errors="replace")
        fence_lang: str | None = None
        for idx, line in enumerate(text.splitlines(), 1):
            fence_match = re.match(r"^```([A-Za-z0-9_-]*)", line.strip())
            if fence_match:
                fence_lang = None if fence_lang is not None else fence_match.group(1).lower()
                continue

            if fence_lang in {"bash", "sh", "shell", "console"}:
                message = raw_doc_command_message(line)
                if message is not None:
                    issues.append(f"{normalized}:{idx}: raw documented command `{line.strip()}` - {message}")

            for fragment in re.findall(r"`([^`]+)`", line):
                message = raw_doc_command_message(fragment)
                if message is not None:
                    issues.append(f"{normalized}:{idx}: raw documented command `{fragment}` - {message}")
    return issues


def doc_drift_added_line_issues(rows: Iterable[tuple[str, str]]) -> list[str]:
    issues: list[str] = []
    for path, line in rows:
        for pattern, include, message in DOC_DRIFT_ADDED_LINE_RULES:
            if include(path) and re.search(pattern, line):
                issues.append(f"{message}: {path}: {line}")
    return issues


def missing_handler_test_issues(changes: Iterable[list[str]], *, root: Path | None = None) -> list[str]:
    root = root or ROOT
    issues: list[str] = []
    for parts in changes:
        status = parts[0]
        if status == "D" or not (status in {"A", "M"} or status.startswith("R")):
            continue
        handler = (parts[2] if status.startswith("R") and len(parts) > 2 else parts[1]).replace("\\", "/")
        if not re.match(r"^src/Modules/.*/(Commands|Queries)/.*Handler[.]cs$", handler):
            continue
        module_match = re.match(r"src/Modules/([^/]+)/", handler)
        if not module_match:
            continue
        module = module_match.group(1)
        subdir = "Commands" if "/Commands/" in handler else "Queries"
        handler_name = Path(handler).stem
        test_file = root / "tests" / "Modules" / module / f"Axis.{module}.Application.Tests" / subdir / f"{handler_name}Tests.cs"
        if not test_file.is_file():
            relative_test = str(test_file.relative_to(root)).replace("\\", "/")
            issues.append(f"Handler {handler} - create {relative_test}")
    return issues


def endpoint_mediator_hits() -> list[str]:
    hits: list[str] = []
    for ep in sorted((ROOT / "src" / "Axis.Api" / "Endpoints").glob("*.cs")):
        method = ""
        count = 0
        for line in ep.read_text(encoding="utf-8").splitlines():
            if "Task<IResult>" in line:
                if method and count > 1:
                    hits.append(f"{rel(ep)} :: {method} ({count} mediator calls)")
                method = line.strip()
                count = 0
                continue
            if re.search(r"[.]Send[(]|[.]Publish[(]", line):
                count += 1
        if method and count > 1:
            hits.append(f"{rel(ep)} :: {method} ({count} mediator calls)")
    return hits


GOVERNANCE_ENTRY_DOCS = [
    Path("AGENTS.md"),
    Path("CONTRIBUTING.md"),
    Path(".github/PULL_REQUEST_TEMPLATE.md"),
]

GOVERNANCE_COMMANDS_OWNED_BY_AGENT_CHECKLIST = [
    "python scripts/axis.py check policy-tests",
    "python scripts/axis.py check doc-drift",
    "python scripts/axis.py check markdown-links",
]

ENFORCEMENT_LEDGER_HEADER = [
    "Finding class",
    "Rule owner",
    "Trigger / scope",
    "Mechanism",
    "Proof / gap",
    "Status",
]

ENFORCEMENT_ALLOWED_STATUSES = {
    "Enforced",
    "Partial",
    "Review-only",
}

ENFORCEMENT_TRUTH_REQUIRED_SNIPPETS = [
    (
        Path(".github/workflows/build-and-test.yml"),
        [
            ("pull_request:", "CI workflow runs for pull requests"),
            ("run: python scripts/axis.py check pr", "PR metadata guard runs in CI"),
            ("run: python scripts/axis.py ready-review --policy-only", "shared ready-review policy profile runs in CI"),
            ("run: python scripts/axis.py check docker", "Docker endpoint is available for Testcontainers in CI through the Axis wrapper"),
            ("run: python scripts/axis.py check vulnerable-packages", "vulnerable package gate runs in CI"),
            ("run: python scripts/axis.py check test-naming", ".NET test naming gate runs in CI"),
            ("dotnet-version: 8.0.x", ".NET CI setup uses the documented SDK major"),
            ("run: python scripts/axis.py dotnet build -- --no-restore", ".NET build runs in CI through the Axis wrapper"),
            ("run: python scripts/axis.py dotnet format --check", ".NET format gate runs in CI through the Axis wrapper"),
            ("python scripts/axis.py dotnet test -- --no-build", "full .NET test suite runs in CI through the Axis wrapper"),
            ("node-version-file: frontend/.nvmrc", "frontend CI setup uses the documented Node source"),
            ("run: python scripts/axis.py frontend install", "frontend dependencies install through the Axis wrapper"),
            ("run: python scripts/axis.py check frontend-vulnerable-packages", "frontend dependency vulnerability gate runs in CI"),
            ("run: python scripts/axis.py frontend gen-api-types --check", "frontend API type generation runs in CI through the Axis wrapper"),
            ("run: python scripts/axis.py check ui-baseline", "approved frontend UI baseline is checked in CI"),
            ("run: python scripts/axis.py frontend ci", "frontend typecheck/lint runs in CI through the Axis wrapper"),
            ("run: python scripts/axis.py frontend test", "frontend tests run in CI through the Axis wrapper"),
            ("uses: lycheeverse/lychee-action", "markdown link check runs in CI"),
            ("lycheeVersion: v0.23.0", "markdown link check pins the documented Lychee version"),
            ("args: --config ./lychee.toml './**/*.md'", "markdown link check uses shared lychee config"),
            ("BASE_BRANCH: main", "doc drift compares against main"),
        ],
    ),
    (
        Path("scripts/axis.py"),
        [
            ('step(".NET SDK", lambda: check_dotnet_sdk())', "local verify checks the documented .NET SDK before dotnet commands"),
            ('step("frontend toolchain", lambda: check_frontend_toolchain())', "local verify checks the documented Node source before npm commands"),
            ('step("frontend vulnerable packages", lambda: check_frontend_vulnerable_packages())', "local verify audits changed frontend dependency manifests"),
            ("dotnet_projects_for_changed_paths(paths)", "local verify routes .NET work by changed project paths"),
            ('step("policy gate tests", lambda: check_policy_tests())', "local verify runs policy gate tests when scripts change"),
            ('step("doc navigation", lambda: check_doc_navigation())', "local verify runs docs checks when docs change"),
            ('step("markdown links (changed files)",', "local verify runs markdown link checks for changed markdown paths"),
            ('def pre_push(args: argparse.Namespace) -> int:', "pre-push quick gate is implemented in Python"),
            ('return ready_review(argparse.Namespace(since=None, policy_only=False))', "pre-push can opt into ready-review with AXIS_PRE_PUSH_FULL"),
            ('def ready_review(args: argparse.Namespace) -> int:', "ready-review owns local review-boundary verification"),
            ('gates.append(("doc drift", lambda: check_doc_drift(None)))', "ready-review and CI share the doc-drift policy profile"),
            ("for issue in governance_owner_boundary_issues():", "doc drift checks governance owner boundaries"),
            ("for issue in enforcement_ledger_issues():", "doc drift checks enforcement ledger rows"),
            ("for issue in enforcement_truth_audit_issues():", "doc drift checks enforcement truth wiring"),
            ("for issue in documented_raw_command_issues():", "doc drift checks documented repo commands go through Axis"),
        ],
    ),
    (
        Path("global.json"),
        [
            ('"version": "8.0.100"', "global.json selects the documented .NET SDK major"),
            ('"rollForward": "latestFeature"', "global.json allows latest installed .NET 8 feature band without selecting newer majors"),
        ],
    ),
    (
        Path("scripts/hooks/pre-push"),
        [
            ('root / "scripts" / "axis.py"), "pre-push"', "pre-push delegates to scripts/axis.py pre-push"),
        ],
    ),
    (
        Path("Directory.Build.props"),
        [
            ("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", "build treats warnings as errors"),
            ('<PackageReference Include="Microsoft.VisualStudio.Threading.Analyzers"', "async-safety analyzer package is wired"),
        ],
    ),
    (
        Path(".editorconfig"),
        [
            ("dotnet_diagnostic.CA2016.severity = warning", "CA2016 dropped CancellationToken analyzer is escalated"),
        ],
    ),
    (
        Path("tests/Api/Axis.Api.Tests/Contracts/OpenApiDocumentTests.cs"),
        [
            ("OpenApiDocument_WhenGeneratedFromRunningApi_MatchesCommittedSnapshot", "OpenAPI snapshot test exists"),
            ("openapi.json drifted from the API", "OpenAPI test fails on committed contract drift"),
            ("SerializeAsJson(OpenApiSpecVersion.OpenApi3_0)", "OpenAPI test serializes the running contract"),
            ("committed.Should().Be(", "OpenAPI test compares committed contract to the running contract"),
        ],
    ),
    (
        Path("frontend/package.json"),
        [
            ('"gen:api-types": "openapi-typescript ../openapi.json -o src/lib/api-types.ts"', "frontend API types generate from committed openapi.json"),
        ],
    ),
    (
        Path("Axis.sln"),
        [
            ("tests\\Architecture\\Axis.Architecture.Tests\\Axis.Architecture.Tests.csproj", "architecture fitness tests are included in Axis.sln"),
        ],
    ),
]


def governance_owner_boundary_issues(*, root: Path | None = None) -> list[str]:
    """Keep governance entry docs from duplicating enforceable rule mechanics."""
    root = root or ROOT
    issues: list[str] = []

    for relative in GOVERNANCE_ENTRY_DOCS:
        path = root / relative
        if not path.is_file():
            continue

        try:
            lines = path.read_text(encoding="utf-8").splitlines()
        except UnicodeDecodeError:
            continue

        normalized = relative.as_posix()
        for idx, line in enumerate(lines, 1):
            for command in GOVERNANCE_COMMANDS_OWNED_BY_AGENT_CHECKLIST:
                if command in line:
                    issues.append(
                        f"{normalized}:{idx}: governance doc restates `{command}`. "
                        "Link to agent-checklist.md#review-verification instead."
                    )

            if "Design Gate" not in line:
                continue

            line_lower = line.lower()
            mentions_machine_gate = (
                "ci gate" in line_lower
                or "machine gate" in line_lower
                or "machine-enforced" in line_lower
                or "automated gate" in line_lower
            )
            if mentions_machine_gate and "not " not in line_lower and "not-a-" not in line_lower:
                issues.append(
                    f"{normalized}:{idx}: Design Gate is a review artifact, not a machine/CI gate. "
                    "Move deterministic enforcement to scripts/tests and docs/ENFORCEMENT.md."
                )

    return issues


def normalized_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="ignore").replace("\r\n", "\n").replace("\r", "\n")


def enforcement_truth_audit_issues(*, root: Path | None = None) -> list[str]:
    """Verify committed CI/script wiring still supports registry enforcement claims."""
    root = root or ROOT
    issues: list[str] = []

    for relative, requirements in ENFORCEMENT_TRUTH_REQUIRED_SNIPPETS:
        path = root / relative
        normalized = relative.as_posix()
        if not path.is_file():
            issues.append(f"{normalized}: enforcement truth audit missing required file")
            continue

        text = normalized_text(path)
        for snippet, description in requirements:
            if snippet not in text:
                issues.append(f"{normalized}: enforcement truth audit missing {description}: `{snippet}`")

    workflow = root / ".github" / "workflows" / "build-and-test.yml"
    if workflow.is_file():
        workflow_text = normalized_text(workflow)
        if workflow_text.count("- 'openapi.json'") < 2:
            issues.append(
                ".github/workflows/build-and-test.yml: enforcement truth audit requires "
                "`openapi.json` to trigger both backend and frontend CI filters"
            )

    return issues


def markdown_table_cells(line: str) -> list[str]:
    if not line.lstrip().startswith("|"):
        return []
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def markdown_table_separator(cells: list[str]) -> bool:
    return bool(cells) and all(re.fullmatch(r":?-{3,}:?", cell.replace(" ", "")) for cell in cells)


def plain_markdown_cell(value: str) -> str:
    return re.sub(r"[*_`]", "", value).strip()


def enforcement_ledger_issues(*, root: Path | None = None) -> list[str]:
    """Validate docs/ENFORCEMENT.md as the single rule registry."""
    root = root or ROOT
    path = root / "docs" / "ENFORCEMENT.md"
    normalized = "docs/ENFORCEMENT.md"

    if not path.is_file():
        return [f"{normalized}: missing rule registry"]

    lines = path.read_text(encoding="utf-8").splitlines()
    try:
        ledger_start = next(idx for idx, line in enumerate(lines) if line.strip() == "## Ledger")
    except StopIteration:
        return [f"{normalized}: missing ## Ledger rule registry"]

    header_idx = None
    for idx in range(ledger_start + 1, len(lines)):
        cells = markdown_table_cells(lines[idx])
        if cells:
            header_idx = idx
            break

    if header_idx is None:
        return [f"{normalized}: ## Ledger must contain a markdown table"]

    header = markdown_table_cells(lines[header_idx])
    if header != ENFORCEMENT_LEDGER_HEADER:
        return [
            f"{normalized}:{header_idx + 1}: ledger header must be "
            f"`{' | '.join(ENFORCEMENT_LEDGER_HEADER)}`"
        ]

    issues: list[str] = []
    row_count = 0
    for idx in range(header_idx + 1, len(lines)):
        line = lines[idx]
        cells = markdown_table_cells(line)
        if not cells:
            if row_count:
                break
            continue
        if markdown_table_separator(cells):
            continue

        row_count += 1
        if len(cells) != len(ENFORCEMENT_LEDGER_HEADER):
            issues.append(
                f"{normalized}:{idx + 1}: ledger row must have "
                f"{len(ENFORCEMENT_LEDGER_HEADER)} cells"
            )
            continue

        row = dict(zip(ENFORCEMENT_LEDGER_HEADER, cells))
        for field, value in row.items():
            if not value:
                issues.append(f"{normalized}:{idx + 1}: ledger `{field}` cell is empty")

        status = plain_markdown_cell(row["Status"])
        if status not in ENFORCEMENT_ALLOWED_STATUSES:
            issues.append(
                f"{normalized}:{idx + 1}: unknown ledger status `{row['Status']}`; "
                f"use one of {sorted(ENFORCEMENT_ALLOWED_STATUSES)}"
            )
            continue

        owner = plain_markdown_cell(row["Rule owner"]).lower()
        mechanism = plain_markdown_cell(row["Mechanism"]).lower()
        proof = plain_markdown_cell(row["Proof / gap"]).lower()
        combined = f"{owner} {mechanism} {proof}"

        if status == "Enforced":
            evidence_markers = ("ci", "test", "analyzer", "compiler", "build", "workflow")
            if not any(marker in combined for marker in evidence_markers):
                issues.append(
                    f"{normalized}:{idx + 1}: Enforced row needs CI/build/tooling proof "
                    "or a negative test"
                )
        elif status == "Partial":
            if "known gap" not in proof:
                issues.append(f"{normalized}:{idx + 1}: Partial row must name a known gap")
        elif status == "Review-only":
            if re.search(r"\b(gate|enforced)\b|fail(?:s|ed)? the pr|\bci\b", combined):
                issues.append(f"{normalized}:{idx + 1}: Review-only row must not use gate/enforced language")

    if row_count == 0:
        issues.append(f"{normalized}: ## Ledger must contain at least one rule row")

    return issues


def check_doc_drift(_args: argparse.Namespace | None = None) -> int:
    range_spec = diff_range()
    issues: list[str] = []
    skip_checkers = set(getattr(_args, "skip_checkers", ()) or ())

    discovery = doc_drift_domains.validate_discovery()
    if discovery:
        print("doc-drift-domains: discovery failed:", file=sys.stderr)
        for issue in discovery:
            print(f"  - {issue}", file=sys.stderr)
        issues.extend(discovery)

    paths = changed_paths(range_spec)
    if not paths:
        if not issues:
            print(f"check-doc-drift: no diff in {range_spec} - skip")
        return 1 if issues else 0

    domain_errors = doc_drift_domains.check_readme_api_status(paths)
    for err in domain_errors:
        fail(issues, err)

    all_added_lines = added_lines(range_spec, lambda _path: True)
    for issue in doc_drift_added_line_issues(all_added_lines):
        fail(issues, issue)

    for issue in documented_raw_command_issues():
        fail(issues, issue)

    for hit in endpoint_mediator_hits():
        fail(
            issues,
            "Endpoint handler calls the mediator more than once - move orchestration into a single "
            f"command/handler or saga (docs/ENFORCEMENT.md): {hit}",
        )

    for issue in missing_handler_test_issues(changed_name_status(range_spec)):
        fail(issues, issue)

    for issue in governance_owner_boundary_issues():
        fail(issues, issue)

    for issue in enforcement_ledger_issues():
        fail(issues, issue)

    for issue in enforcement_truth_audit_issues():
        fail(issues, issue)

    spec_target = ROOT / "docs" / "ARCHITECTURE.md"
    if spec_target.is_file():
        spec_rx = re.compile(r"Not yet|\bplanned\b|Will be|To be implemented|Coming soon|in the future")
        for idx, line in enumerate(spec_target.read_text(encoding="utf-8").splitlines(), 1):
            if spec_rx.search(line):
                fail(issues, f"Speculation in reference doc - move to an owning use-case file: {rel(spec_target)}:{idx}:{line}")

    lesson_rx = re.compile(r"\*\*Lesson|[Ll]esson [(]|[Ll]esson[)]")
    lesson_files = list((ROOT / "docs" / "playbooks").rglob("*.md"))
    lesson_files.extend(ROOT / name for name in ("AGENTS.md", "docs/ARCHITECTURE.md"))
    for path in lesson_files:
        if not path.is_file():
            continue
        for idx, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if lesson_rx.search(line):
                fail(
                    issues,
                    "Incident/lesson framing in practice doc - generalize the rule, move specifics to "
                    f"the owning use-case or PR retro (docs-style.md): {rel(path)}:{idx}:{line}",
                )

    for migration in (ROOT / "src" / "Modules").glob("**/Migrations/*.cs"):
        if migration.name.endswith(".Designer.cs") or "Snapshot" in migration.name:
            continue
        if not migration.with_name(f"{migration.stem}.Designer.cs").is_file():
            fail(issues, f"EF migration missing .Designer.cs - regenerate with dotnet ef: {rel(migration)}")

    checkers = [
        ("check-text-encoding", check_text_encoding),
        ("check-scripts-standard", check_scripts_standard),
        ("check-repo-skills", check_repo_skills),
        ("check-ef-domain-mapping", check_ef_domain_mapping),
        ("check-frontend-api-contracts", check_frontend_api_contracts),
        ("check-ui-baseline", check_ui_baseline),
        ("check-frontend-quality", check_frontend_quality),
        ("check-use-case-docs.py", lambda _=None: run_module_check("check-use-case-docs.py", ["--check"])),
        ("check-foundation-docs.py", lambda _=None: run_module_check("check-foundation-docs.py", ["--check"])),
        ("check-doc-link-targets.py", lambda _=None: run_module_check("check-doc-link-targets.py", ["--check"])),
        ("check-doc-navigation", check_doc_navigation),
        ("check-doc-size-budgets", check_doc_size_budgets),
        ("check-doc-code-fences.py", lambda _=None: run_module_check("check-doc-code-fences.py", ["--check"])),
        ("check-local-dev-docs.py", lambda _=None: run_module_check("check-local-dev-docs.py", ["--check"])),
    ]
    for name, checker in checkers:
        if name in skip_checkers:
            continue
        if checker() != 0:
            issues.append(f"{name} failed")

    if any_changed(paths, r"^docker-compose[.]yml$") and not docs_changed_under(paths, "docs/playbooks/local-dev.md"):
        fail(issues, "local compose file changed but docs/playbooks/local-dev.md not updated in this PR")

    if issues:
        print("\nSee docs/playbooks/agent-checklist.md", file=sys.stderr)
        return 1
    print(f"check-doc-drift: OK ({range_spec})")
    return 0


def command_version_line(name: str, *version_args: str, env: dict[str, str] | None = None) -> tuple[bool, str, str]:
    command = resolve_exe(name, env=env)
    resolved = shutil.which(command) or shutil.which(name, path=env_path(env))
    if resolved is None:
        return False, f"{name} not found in PATH", command

    result = run_optional([command, *version_args], env=env, timeout=VERSION_PROBE_TIMEOUT_SECONDS)
    if result is None:
        return False, f"{name} not executable from PATH", resolved
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        return False, detail or f"{name} exited with {result.returncode}", resolved

    first_line = (result.stdout or result.stderr or "").strip().splitlines()
    return True, first_line[0] if first_line else "available", resolved


def version_major(version_line: str) -> str | None:
    match = re.search(r"\b[vV]?([0-9]+)(?:[.][0-9]+)*\b", version_line)
    return match.group(1) if match else None


def required_node_major() -> tuple[bool, str]:
    if not NVMRC_PATH.is_file():
        return False, f"missing {rel(NVMRC_PATH)}"
    text = NVMRC_PATH.read_text(encoding="utf-8").strip()
    match = re.fullmatch(r"v?([0-9]+)(?:[.][0-9]+)*", text)
    if match is None:
        return False, f"{rel(NVMRC_PATH)} must contain a Node major or semver version"
    return True, match.group(1)


def _path_with_prefix(bin_dir: Path) -> str:
    entry = str(bin_dir)
    current = os.environ.get("PATH", "")
    parts = [part for part in current.split(os.pathsep) if part]
    if entry in parts:
        return current
    return os.pathsep.join([entry, *parts]) if parts else entry


def _version_sort_key(version_text: str) -> tuple[int, ...]:
    return tuple(int(part) for part in re.findall(r"\d+", version_text))


def _npm_exists_in_bin_dir(bin_dir: Path) -> bool:
    return any((bin_dir / name).is_file() for name in ("npm", "npm.cmd", "npm.exe"))


def _node_exists_in_bin_dir(bin_dir: Path) -> bool:
    return (bin_dir / "node").is_file() or (bin_dir / "node.exe").is_file()


def _home_dir() -> Path | None:
    try:
        return Path.home()
    except RuntimeError:
        return None


def _nvm_unix_roots() -> list[Path]:
    roots: list[Path] = []
    nvm_dir = os.environ.get("NVM_DIR")
    if nvm_dir:
        roots.append(Path(nvm_dir) / "versions" / "node")
    home = _home_dir()
    if home is not None:
        roots.append(home / ".nvm" / "versions" / "node")
    return roots


def _nvm_windows_roots() -> list[Path]:
    if os.name != "nt":
        return []
    roots: list[Path] = []
    for env_name in ("NVM_HOME", "NVM_DIR"):
        value = os.environ.get(env_name)
        if value:
            roots.append(Path(value))
    appdata = os.environ.get("APPDATA")
    if appdata:
        roots.append(Path(appdata) / "nvm")
    return roots


def _windows_git_usr_bin_dirs() -> list[Path]:
    dirs: list[Path] = []
    for env_name in ("ProgramFiles", "ProgramFiles(x86)"):
        base = os.environ.get(env_name)
        if base:
            dirs.append(Path(base) / "Git" / "usr" / "bin")
    localappdata = os.environ.get("LOCALAPPDATA") or os.environ.get("LocalAppData")
    if localappdata:
        dirs.append(Path(localappdata) / "Programs" / "Git" / "usr" / "bin")
    return dirs


def find_openssl() -> str | None:
    for name in (exe("openssl"), "openssl"):
        resolved = shutil.which(name)
        if resolved:
            return resolved
    if os.name != "nt":
        return None
    for usr_bin in _windows_git_usr_bin_dirs():
        candidate = usr_bin / "openssl.exe"
        if candidate.is_file():
            return str(candidate)
    return None


def _node_version_label(bin_dir: Path) -> str:
    if bin_dir.name == "bin" and bin_dir.parent.name.startswith("v"):
        return bin_dir.parent.name
    return bin_dir.name


def _node_toolchain_bin_dirs(expected_major: str) -> list[Path]:
    seen: set[Path] = set()
    candidates: list[Path] = []

    def add(bin_dir: Path) -> None:
        resolved = bin_dir.resolve()
        if resolved in seen or not _node_exists_in_bin_dir(resolved) or not _npm_exists_in_bin_dir(resolved):
            return
        seen.add(resolved)
        candidates.append(resolved)

    try:
        add(axis_setup.managed_bin_dir("node"))
    except axis_setup.SetupError:
        pass

    for root in _nvm_unix_roots():
        if not root.is_dir():
            continue
        for version_dir in root.iterdir():
            if version_dir.is_dir() and version_major(version_dir.name) == expected_major:
                add(version_dir / "bin")

    for root in _nvm_windows_roots():
        if not root.is_dir():
            continue
        for version_dir in root.iterdir():
            if version_dir.is_dir() and version_major(version_dir.name) == expected_major:
                add(version_dir)

    nvm_symlink = os.environ.get("NVM_SYMLINK")
    if nvm_symlink:
        add(Path(nvm_symlink))

    home = _home_dir()
    if home is not None:
        add(home / ".volta" / "bin")

    return sorted(candidates, key=lambda path: _version_sort_key(_node_version_label(path)), reverse=True)


def _nvm_node_bin_dirs(expected_major: str) -> list[Path]:
    return _node_toolchain_bin_dirs(expected_major)


def frontend_toolchain_env() -> dict[str, str]:
    expected_ok, expected_or_error = required_node_major()
    if not expected_ok:
        return {}

    expected = expected_or_error
    node_ok, node_version, _node_resolved = command_version_line("node", "--version")
    npm_ok, _npm_version, _npm_resolved = command_version_line("npm", "--version")
    if node_ok and npm_ok and version_major(node_version) == expected:
        return {}

    for bin_dir in _nvm_node_bin_dirs(expected):
        env = {"PATH": _path_with_prefix(bin_dir)}
        node_ok, node_version, _node_resolved = command_version_line("node", "--version", env=env)
        npm_ok, _npm_version, _npm_resolved = command_version_line("npm", "--version", env=env)
        if node_ok and npm_ok and version_major(node_version) == expected:
            return env

    return {}


def global_json_sdk_major() -> tuple[bool, str]:
    if not GLOBAL_JSON_PATH.is_file():
        return False, f"missing {path_label(GLOBAL_JSON_PATH)}"
    try:
        data = json.loads(GLOBAL_JSON_PATH.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        return False, f"{path_label(GLOBAL_JSON_PATH)} is invalid JSON: {exc}"

    sdk = data.get("sdk")
    if not isinstance(sdk, dict):
        return False, f"{path_label(GLOBAL_JSON_PATH)} must contain an sdk object"
    version = sdk.get("version")
    if not isinstance(version, str) or not version.strip():
        return False, f"{path_label(GLOBAL_JSON_PATH)} must contain sdk.version"

    major = version_major(version)
    if major is None:
        return False, f"{path_label(GLOBAL_JSON_PATH)} sdk.version `{version}` is not a semver version"
    return True, major


def dotnet_sdk_status() -> tuple[bool, str]:
    source_ok, source_major_or_error = global_json_sdk_major()
    if not source_ok:
        return False, f"{source_major_or_error}; .NET SDK {REQUIRED_DOTNET_SDK_MAJOR}.x is required per {TECH_STACK_DOC}"
    if source_major_or_error != REQUIRED_DOTNET_SDK_MAJOR:
        return (
            False,
            f"{path_label(GLOBAL_JSON_PATH)} selects .NET SDK {source_major_or_error}.x; "
            f"expected {REQUIRED_DOTNET_SDK_MAJOR}.x per {TECH_STACK_DOC}",
        )

    ok, version_line, resolved = command_version_line("dotnet", "--version")
    if not ok:
        return (
            False,
            f"{version_line}; .NET SDK {REQUIRED_DOTNET_SDK_MAJOR}.x is required per "
            f"{TECH_STACK_DOC} and {path_label(GLOBAL_JSON_PATH)}",
        )

    major = version_major(version_line)
    if major != REQUIRED_DOTNET_SDK_MAJOR:
        return (
            False,
            f"found `{version_line or 'unknown'}` at {resolved}; "
            f"expected .NET SDK {REQUIRED_DOTNET_SDK_MAJOR}.x per "
            f"{TECH_STACK_DOC} and {path_label(GLOBAL_JSON_PATH)}",
        )
    return True, f"{version_line} ({resolved}); expected major {REQUIRED_DOTNET_SDK_MAJOR} from {path_label(GLOBAL_JSON_PATH)}"


def check_dotnet_sdk(_args: argparse.Namespace | None = None) -> int:
    ok, detail = dotnet_sdk_status()
    if not ok:
        print(f"dotnet-sdk: FAIL - {detail}", file=sys.stderr)
        return 1
    print(f"dotnet-sdk: OK ({detail})")
    return 0


def node_version_status(env: dict[str, str] | None = None) -> tuple[bool, str]:
    expected_ok, expected_or_error = required_node_major()
    if not expected_ok:
        return False, f"{expected_or_error}; fix the documented Node source before running frontend commands"
    expected = expected_or_error
    env = frontend_toolchain_env() if env is None else env

    ok, version_line, resolved = command_version_line("node", "--version", env=env)
    if not ok:
        return False, f"{version_line}; Node {expected}.x is required per {rel(NVMRC_PATH)}"

    major = version_major(version_line)
    if major != expected:
        return (
            False,
            f"found `{version_line or 'unknown'}` at {resolved}; "
            f"expected Node {expected}.x from {rel(NVMRC_PATH)}",
        )
    return True, f"{version_line} ({resolved}); expected major {expected} from {rel(NVMRC_PATH)}"


def check_frontend_toolchain(_args: argparse.Namespace | None = None) -> int:
    env = frontend_toolchain_env()
    node_ok, node_detail = node_version_status(env)
    if not node_ok:
        print(f"frontend-toolchain: FAIL - {node_detail}", file=sys.stderr)
        return 1

    npm_status, npm_detail = _command_version("npm", "--version", env=env)
    if npm_status != "OK":
        print(f"frontend-toolchain: FAIL - {npm_detail}; npm must resolve beside Node", file=sys.stderr)
        return 1

    print(f"frontend-toolchain: OK ({node_detail}; npm {npm_detail})")
    return 0


def playwright_chromium_status(env: dict[str, str] | None = None) -> tuple[bool, str]:
    env = frontend_toolchain_env() if env is None else env
    probe = run(
        [
            resolve_exe("node", env=env),
            "--input-type=module",
            "-e",
            (
                "import { chromium } from '@playwright/test';"
                "import { existsSync } from 'node:fs';"
                "const path = chromium.executablePath();"
                "if (!existsSync(path)) {"
                "  console.error(path);"
                "  process.exit(1);"
                "}"
                "console.log(path);"
            ),
        ],
        cwd=FRONTEND_DIR,
        capture=True,
        check=False,
        env=env,
        timeout=PLAYWRIGHT_BROWSER_PROBE_TIMEOUT_SECONDS,
    )
    detail = (probe.stdout or probe.stderr or "").strip().splitlines()
    browser_path = detail[-1] if detail else "Playwright Chromium executable not found"
    if probe.returncode == 0:
        return True, browser_path
    return (
        False,
        f"{browser_path}; run `python scripts/axis.py frontend install-browsers`",
    )


def check_playwright_browsers(_args: argparse.Namespace | None = None) -> int:
    rc = check_frontend_toolchain()
    if rc != 0:
        return rc

    ok, detail = playwright_chromium_status()
    if not ok:
        print(f"playwright-browsers: FAIL - {detail}", file=sys.stderr)
        return 1
    print(f"playwright-browsers: OK (chromium: {detail})")
    return 0


def find_lychee() -> str | None:
    resolved = resolve_exe("lychee")
    return resolved if Path(resolved).is_file() else shutil.which("lychee")


def lychee_version_status(lychee: str) -> tuple[bool, str]:
    result = run_optional([lychee, "--version"], timeout=VERSION_PROBE_TIMEOUT_SECONDS)
    if result is None:
        return False, f"{lychee} is not executable"
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        return False, detail or f"{lychee} --version exited with {result.returncode}"

    first_line = (result.stdout or result.stderr or "").strip().splitlines()
    version_line = first_line[0] if first_line else ""
    expected = f"lychee {axis_setup.LYCHEE_VERSION}"
    if version_line != expected:
        return (
            False,
            f"found `{version_line or 'unknown'}` at {lychee}; expected `{expected}`",
        )
    return True, f"{version_line} ({lychee})"


def run_lychee_markdown_check(lychee: str, paths: list[str] | None = None) -> subprocess.CompletedProcess[str]:
    targets = paths if paths else ["./**/*.md"]
    return run([lychee, "--config", "./lychee.toml", *targets], capture=True, check=False)


def emit_captured_process(result: subprocess.CompletedProcess[str]) -> None:
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr)


def check_markdown_links(_args: argparse.Namespace | None = None) -> int:
    return check_markdown_links_for_paths(None)


def check_markdown_links_for_paths(paths: list[str] | None) -> int:
    lychee = find_lychee()
    if lychee is None:
        print(
            f"check-markdown-links: Lychee {axis_setup.LYCHEE_VERSION} is required, "
            "but `lychee` was not found in PATH. See docs/playbooks/scripts.md#tool-versions.",
            file=sys.stderr,
        )
        return 1
    version_ok, version_detail = lychee_version_status(lychee)
    if not version_ok:
        print(
            f"check-markdown-links: Lychee {axis_setup.LYCHEE_VERSION} is required; {version_detail}. "
            "Install the documented version or put it earlier in PATH. "
            "See docs/playbooks/scripts.md#tool-versions.",
            file=sys.stderr,
        )
        return 1
    result = run_lychee_markdown_check(lychee, paths)
    emit_captured_process(result)
    return result.returncode


def coderabbit_cli_status() -> tuple[bool, str]:
    ok, version_line, resolved = command_version_line("coderabbit", "--version")
    if not ok:
        return False, f"{version_line}; CodeRabbit CLI is required for the local pre-PR review checkpoint. See {TOOL_VERSIONS_DOC}."

    version_match = re.search(r"\b([0-9]+(?:[.][0-9]+)+)\b", version_line)
    if version_match is None:
        return False, f"found `{version_line or 'unknown'}` at {resolved}; expected version >= {MINIMUM_CODERABBIT_CLI_VERSION}"

    if _version_sort_key(version_match.group(1)) < _version_sort_key(MINIMUM_CODERABBIT_CLI_VERSION):
        return (
            False,
            f"found `{version_line or 'unknown'}` at {resolved}; expected version >= {MINIMUM_CODERABBIT_CLI_VERSION}",
        )

    return True, f"{version_line} ({resolved}); expected >= {MINIMUM_CODERABBIT_CLI_VERSION}"


def coderabbit_doctor_status(*, strict: bool) -> tuple[str, str]:
    if strict:
        ok, detail = coderabbit_cli_status()
        return ("OK" if ok else "FAIL", detail)

    resolved = shutil.which(resolve_exe("coderabbit")) or shutil.which("coderabbit")
    if resolved is None:
        return (
            "FAIL",
            f"coderabbit not found in PATH; CodeRabbit CLI is required for the local pre-PR review checkpoint. See {TOOL_VERSIONS_DOC}.",
        )

    suffix = Path(resolved).suffix.lower()
    if os.name == "nt" and suffix in {".cmd", ".bat"}:
        return (
            "WARN",
            f"{resolved}; version check skipped for Windows command shim. Run `python scripts/axis.py check coderabbit-cli` before PR review.",
        )

    ok, detail = coderabbit_cli_status()
    return ("OK" if ok else "FAIL", detail)


def check_coderabbit_cli(_args: argparse.Namespace | None = None) -> int:
    ok, detail = coderabbit_cli_status()
    if not ok:
        print(f"coderabbit-cli: FAIL - {detail}", file=sys.stderr)
        return 1
    print(f"coderabbit-cli: OK ({detail})")
    return 0


def dotnet_command(args: argparse.Namespace) -> int:
    rc = check_dotnet_sdk()
    if rc != 0:
        return rc

    command = args.dotnet_command
    dotnet_args = list(args.dotnet_args)
    if dotnet_args and dotnet_args[0] == "--":
        dotnet_args = dotnet_args[1:]
    if command == "restore":
        return run([exe("dotnet"), "restore", "Axis.sln", *dotnet_args], check=False).returncode
    if command == "build":
        return run([exe("dotnet"), "build", "Axis.sln", "--nologo", *dotnet_args], check=False).returncode
    if command == "test":
        target = "Axis.sln"
        if dotnet_args and Path(dotnet_args[0]).suffix.lower() in {".csproj", ".sln"}:
            target = dotnet_args.pop(0)
            if dotnet_args and dotnet_args[0] == "--":
                dotnet_args = dotnet_args[1:]
        return run([exe("dotnet"), "test", target, "--nologo", *dotnet_args], check=False).returncode
    if command == "format":
        format_args = ["format", "Axis.sln"]
        if args.check:
            format_args.append("--verify-no-changes")
        format_args.extend(dotnet_args)
        return run([exe("dotnet"), *format_args], check=False).returncode
    if command == "run-api":
        return run([exe("dotnet"), "run", "--project", str(API_PROJECT), *dotnet_args], check=False).returncode
    if command == "ef":
        return run([exe("dotnet"), "ef", *dotnet_args], check=False).returncode
    raise CheckError(f"Unknown dotnet command: {command}")


def run_frontend_npm(
    npm_args: list[str],
    *,
    cwd: Path = FRONTEND_DIR,
    env_overrides: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    env = frontend_toolchain_env()
    if env_overrides:
        env.update(env_overrides)
    return run([resolve_exe("npm", env=env), *npm_args], cwd=cwd, env=env, check=False)


def passthrough_args(raw_args: list[str]) -> list[str]:
    args = list(raw_args)
    if args and args[0] == "--":
        return args[1:]
    return args


def frontend_command(args: argparse.Namespace) -> int:
    command = args.frontend_command
    if command == "ui-baseline":
        if args.write:
            write_ui_baseline()
            return 0
        return check_ui_baseline()

    rc = check_frontend_toolchain()
    if rc != 0:
        return rc

    if command == "install":
        return run_frontend_npm(["ci"]).returncode
    if command == "install-browsers":
        return run_frontend_npm(["exec", "--", "playwright", "install", "chromium"]).returncode
    if command == "ci":
        return run_frontend_npm(["run", "ci"]).returncode
    if command == "test":
        return run_frontend_npm(["run", "test"]).returncode
    if command == "gen-api-types":
        generated_path = FRONTEND_DIR / "src" / "lib" / "api-types.ts"
        original = generated_path.read_bytes() if generated_path.exists() else None
        result = run_frontend_npm(["run", "gen:api-types"])
        if result.returncode != 0 or not args.check:
            return result.returncode
        generated = generated_path.read_bytes() if generated_path.exists() else None
        if generated != original:
            if original is None:
                generated_path.unlink(missing_ok=True)
            else:
                generated_path.write_bytes(original)
            print(
                "frontend gen-api-types: frontend/src/lib/api-types.ts is stale - "
                "run `python scripts/axis.py frontend gen-api-types` and commit the result",
                file=sys.stderr,
            )
            return 1
        return 0
    if command == "script":
        npm_args = ["run", args.script_name]
        script_args = passthrough_args(args.script_args)
        if script_args:
            npm_args.append("--")
            npm_args.extend(script_args)
        return run_frontend_npm(npm_args).returncode
    raise CheckError(f"Unknown frontend command: {command}")


def check_docker(_args: argparse.Namespace | None = None) -> int:
    if _docker_info_ok():
        print("check-docker: OK (docker info works)")
        return 0
    print("check-docker: FAIL - docker info failed; no reachable Docker endpoint detected", file=sys.stderr)
    return 1


DOTNET_SOLUTION_LEVEL_RE = re.compile(
    r"^(Directory[.].*|Axis[.]sln$|global[.]json$|[.]editorconfig$|[.]github/workflows/build-and-test[.]yml$)"
)


def is_dotnet_path(path: str) -> bool:
    return path.startswith(("src/", "tests/")) or DOTNET_SOLUTION_LEVEL_RE.search(path) is not None


def is_frontend_path(path: str) -> bool:
    return path.startswith("frontend/") or path in {
        ".editorconfig",
        "openapi.json",
        ".github/workflows/build-and-test.yml",
    }


def is_markdown_link_path(path: str) -> bool:
    return path.endswith(".md") or path in {"lychee.toml", ".github/workflows/build-and-test.yml"}


def is_docs_path(path: str) -> bool:
    return path.startswith("docs/") or path in {
        "AGENTS.md",
        "README.md",
        "CONTRIBUTING.md",
        ".github/PULL_REQUEST_TEMPLATE.md",
    }


def nearest_csproj(path: str) -> str | None:
    relative = Path(path)
    if relative.suffix == ".csproj":
        return path

    current = ROOT / relative.parent
    while current != ROOT.parent:
        if current.exists():
            projects = sorted(current.glob("*.csproj"))
            if projects:
                return rel(projects[0])
        if current == ROOT:
            break
        current = current.parent
    return None


def related_test_project_for_source_project(project: str) -> str | None:
    project_name = Path(project).stem
    if project == "src/Axis.Api/Axis.Api.csproj":
        candidate = ROOT / "tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj"
    elif project.startswith("src/Modules/"):
        parts = Path(project).parts
        if len(parts) < 4:
            return None
        module_name = parts[2]
        candidate = ROOT / "tests" / "Modules" / module_name / f"{project_name}.Tests" / f"{project_name}.Tests.csproj"
    elif project.startswith("src/Shared/"):
        candidate = ROOT / f"tests/Shared/{project_name}.Tests/{project_name}.Tests.csproj"
    else:
        return None
    return rel(candidate) if candidate.is_file() else None


def dotnet_projects_for_changed_paths(paths: list[str]) -> tuple[list[str], list[str]]:
    build_projects: set[str] = set()
    test_projects: set[str] = set()

    for path in paths:
        if not path.startswith(("src/", "tests/")):
            continue
        project = nearest_csproj(path)
        if project is None:
            continue
        if project.startswith("tests/"):
            if project != "tests/Shared/Axis.Testing/Axis.Testing.csproj":
                test_projects.add(project)
            continue

        build_projects.add(project)
        related_test = related_test_project_for_source_project(project)
        if related_test is not None:
            test_projects.add(related_test)

    if any(path.startswith("src/") for path in paths):
        architecture_tests = "tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj"
        if (ROOT / architecture_tests).is_file():
            test_projects.add(architecture_tests)

    return sorted(build_projects), sorted(test_projects)


def dotnet_format_changed_paths(paths: list[str]) -> int:
    include = [
        path
        for path in paths
        if path.startswith(("src/", "tests/"))
        and Path(path).suffix.lower() in {".cs", ".csproj", ".props", ".targets"}
        and (ROOT / path).is_file()
    ]
    if not include:
        print("dotnet-format-changed: OK (no changed .NET files to format)")
        return 0
    return dotnet_command(
        argparse.Namespace(
            dotnet_command="format",
            check=True,
            dotnet_args=["--include", *include],
        )
    )


def dotnet_build_projects(projects: list[str]) -> int:
    for project in projects:
        result = run([exe("dotnet"), "build", project, "--nologo"], check=False)
        if result.returncode != 0:
            return result.returncode
    return 0


def dotnet_test_projects(projects: list[str]) -> int:
    for project in projects:
        result = run([exe("dotnet"), "test", project, "--nologo"], check=False)
        if result.returncode != 0:
            return result.returncode
    return 0


def verify_scope_paths(since: str | None = None) -> tuple[str, list[str]]:
    if since:
        return f"{since}..HEAD + working tree", changed_paths_since(since)
    working_tree = working_tree_paths()
    if working_tree:
        return "working tree", working_tree
    range_spec = diff_range()
    return range_spec, changed_paths(range_spec)


def verify(args: argparse.Namespace) -> int:
    scope, paths = verify_scope_paths(getattr(args, "since", None))
    failed: list[str] = []
    planned: list[str] = []
    plan_only = getattr(args, "plan_only", False)

    def step(name: str, fn: callable[[], int]) -> int:
        print()
        print(f"> {name}")
        planned.append(name)
        if plan_only:
            print(f"PLAN {name}")
            return 0
        try:
            rc = fn()
        except CheckError as exc:
            print(exc, file=sys.stderr)
            rc = 1
        if rc == 0:
            print(f"OK {name}")
        else:
            print(f"FAIL {name}")
            failed.append(name)
        return rc

    dotnet = any(is_dotnet_path(path) for path in paths)
    dotnet_solution_level = any(DOTNET_SOLUTION_LEVEL_RE.search(path) for path in paths)
    dotnet_test_naming = any(path.startswith("tests/") and path.endswith(".cs") for path in paths)
    dotnet_test_project_classification = any(path.startswith("tests/") and path.endswith(".csproj") for path in paths)
    dotnet_package_scan = any(
        path == "Directory.Packages.props" or (path.endswith(".csproj") and path.startswith(("src/", "tests/")))
        for path in paths
    )
    build_projects, test_projects = dotnet_projects_for_changed_paths(paths)

    frontend = any(is_frontend_path(path) for path in paths)
    frontend_package_scan = any(
        path in {"frontend/package.json", "frontend/package-lock.json"} for path in paths
    )
    renovate_config = ".github/renovate.json5" in paths
    frontend_api_types = "openapi.json" in paths or "frontend/src/lib/api-types.ts" in paths
    frontend_tests_only = frontend and all(
        path.startswith("frontend/tests/") or path.startswith("frontend/e2e/")
        for path in paths
        if is_frontend_path(path)
    )

    markdown_paths = [path for path in paths if path.endswith(".md") and (ROOT / path).is_file()]
    markdown_links_global = any(path in {"lychee.toml", ".github/workflows/build-and-test.yml"} for path in paths)
    markdown_links = any(is_markdown_link_path(path) for path in paths)
    docs = any(is_docs_path(path) for path in paths)
    use_case_docs = any(path.startswith("docs/use-cases/") for path in paths)
    foundation_docs = any(path.startswith("docs/foundations/") for path in paths)
    skills = any(path.startswith(f"{REPO_SKILLS_DIR}/") for path in paths)
    scripts_changed = any(path.startswith("scripts/") for path in paths)
    text_paths = [path for path in paths if (ROOT / path).is_file() and should_check_text_encoding(path)]
    api_surface_drift = any_changed(paths, r"^src/Axis[.]Api/Endpoints/") and not any_changed(paths, r"^openapi[.]json$")

    print(f"verify - changed-path scoped ({len(paths)} path(s), scope={scope})")
    print(
        "verify plan: "
        f"dotnet={dotnet} frontend={frontend} docs={docs} scripts={scripts_changed} "
        f"skills={skills} markdown-links={markdown_links} renovate-config={renovate_config}"
    )

    if not paths:
        print("verify: no changed paths against the diff base; nothing to run")
        return 0

    if text_paths:
        step("text encoding (changed files)", lambda: run_text_encoding_check(text_paths, label="check-text-encoding-changed"))

    if dotnet:
        if step(".NET SDK", lambda: check_dotnet_sdk()) == 0:
            if dotnet_test_naming:
                step(".NET test naming", lambda: check_test_naming())
            if dotnet_test_project_classification:
                step(".NET test project classification", lambda: check_test_project_classification())
            if dotnet_solution_level:
                step(".NET build (solution)", lambda: dotnet_command(argparse.Namespace(dotnet_command="build", dotnet_args=[])))
                step(".NET format (solution)", lambda: dotnet_command(argparse.Namespace(dotnet_command="format", check=True, dotnet_args=[])))
                step(".NET test (unit projects)", lambda: test_unit(argparse.Namespace(dotnet_args=[])))
            else:
                if build_projects:
                    step(".NET build (changed projects)", lambda: dotnet_build_projects(build_projects))
                step(".NET format (changed files)", lambda: dotnet_format_changed_paths(paths))
                if test_projects:
                    step(".NET test (related projects)", lambda: dotnet_test_projects(test_projects))
            if dotnet_package_scan:
                step(".NET vulnerable packages", lambda: check_vulnerable_packages())

    if frontend:
        if step("frontend toolchain", lambda: check_frontend_toolchain()) == 0:
            if frontend_package_scan:
                step("frontend vulnerable packages", lambda: check_frontend_vulnerable_packages())
            if frontend_api_types:
                step("frontend API types", lambda: frontend_command(argparse.Namespace(frontend_command="gen-api-types", check=True)))
            step("frontend ci (tsc + biome)", lambda: frontend_command(argparse.Namespace(frontend_command="ci")))
            if frontend_tests_only:
                changed_unit_tests = [
                    path.removeprefix("frontend/")
                    for path in paths
                    if path.startswith("frontend/tests/") and path.endswith((".ts", ".tsx"))
                ]
                changed_e2e_tests = [
                    path.removeprefix("frontend/")
                    for path in paths
                    if path.startswith("frontend/e2e/") and path.endswith((".ts", ".tsx"))
                ]
                if changed_unit_tests:
                    step(
                        "frontend test (changed test files)",
                        lambda: run_frontend_npm(["exec", "vitest", "run", *changed_unit_tests]).returncode,
                    )
                if changed_e2e_tests:
                    step(
                        "frontend e2e (changed test files)",
                        lambda: run_frontend_npm(["run", "test:e2e", "--", *changed_e2e_tests]).returncode,
                    )
            else:
                step("frontend test", lambda: frontend_command(argparse.Namespace(frontend_command="test")))

    if scripts_changed:
        step("scripts standard", lambda: check_scripts_standard())
        step("policy gate tests", lambda: check_policy_tests())

    if renovate_config:
        step("Renovate config", lambda: check_renovate_config(None))

    if skills:
        step("Repo skills", lambda: check_repo_skills())

    if docs:
        step("doc navigation", lambda: check_doc_navigation())
        step("doc size budgets", lambda: check_doc_size_budgets())
        step("doc code fences", lambda: run_module_check("check-doc-code-fences.py", []))
        if use_case_docs:
            step("use-case docs", lambda: run_module_check("check-use-case-docs.py", []))
        if foundation_docs:
            step("foundation docs", lambda: run_module_check("check-foundation-docs.py", []))

    if markdown_links and (markdown_paths or markdown_links_global):
        step(
            "markdown links (changed files)",
            lambda: check_markdown_links_for_paths(None if markdown_links_global else markdown_paths),
        )

    if api_surface_drift:
        print()
        print("WARN API surface changed (src/Axis.Api/Endpoints/) but openapi.json is unchanged.")
        print("  If you added or changed a route / request / response shape, regenerate the contract:")
        print("    python scripts/axis.py generate api-contracts")
        print("  then commit openapi.json + api-types.ts; CI's OpenApiDocumentTests fails otherwise.")

    print()
    if plan_only:
        print(f"verify: PLAN - {len(planned)} step(s); no commands run")
        return 0
    if not failed:
        print("verify: PASS")
        return 0
    print(f"verify: FAIL - {len(failed)} step(s): {' '.join(failed)}", file=sys.stderr)
    return 1


def ready_review_doc_drift_coverage(paths: list[str]) -> set[str]:
    covered: set[str] = set()
    if any(path.startswith("scripts/") for path in paths):
        covered.add("check-scripts-standard")
    if any(path.startswith(f"{REPO_SKILLS_DIR}/") for path in paths):
        covered.add("check-repo-skills")
    if any(is_docs_path(path) for path in paths):
        covered.update({"check-doc-navigation", "check-doc-size-budgets", "check-doc-code-fences.py"})
    if any(path.startswith("docs/use-cases/") for path in paths):
        covered.add("check-use-case-docs.py")
    if any(path.startswith("docs/foundations/") for path in paths):
        covered.add("check-foundation-docs.py")
    return covered


def ready_review_policy_gates(
    paths: list[str],
    *,
    policy_tests_covered: bool = False,
    doc_drift_covered: set[str] | None = None,
) -> list[tuple[str, callable[[], int]]]:
    gates: list[tuple[str, callable[[], int]]] = []
    if any(path.startswith("scripts/") for path in paths) and not policy_tests_covered:
        gates.append(("policy gate tests", check_policy_tests))
    if ".github/renovate.json5" in paths:
        gates.append(("Renovate config", lambda: check_renovate_config(None)))
    skip_checkers = set(doc_drift_covered or ())
    gates.append(
        (
            "doc drift",
            lambda: check_doc_drift(argparse.Namespace(skip_checkers=skip_checkers)),
        )
    )
    return gates


def run_ready_review_policy(
    paths: list[str],
    *,
    policy_tests_covered: bool = False,
    doc_drift_covered: set[str] | None = None,
) -> tuple[int, list[str]]:
    failed: list[str] = []
    executed: list[str] = []
    for name, checker in ready_review_policy_gates(
        paths,
        policy_tests_covered=policy_tests_covered,
        doc_drift_covered=doc_drift_covered,
    ):
        print()
        print(f"> ready-review: {name}")
        executed.append(name)
        try:
            rc = checker()
        except CheckError as exc:
            print(exc, file=sys.stderr)
            rc = 1
        if rc == 0:
            print(f"OK ready-review: {name}")
        else:
            print(f"FAIL ready-review: {name}")
            failed.append(name)
    return (0 if not failed else 1), executed


def ready_review(args: argparse.Namespace) -> int:
    if working_tree_paths():
        print(
            "ready-review: FAIL - create an intentional checkpoint commit before review-boundary verification",
            file=sys.stderr,
        )
        return 1

    try:
        _scope, paths = verify_scope_paths(getattr(args, "since", None))
    except CheckError as exc:
        print(exc, file=sys.stderr)
        return 1

    policy_only = bool(getattr(args, "policy_only", False))
    executed: list[str] = []
    if not policy_only:
        if verify(args) != 0:
            print("ready-review: FAIL - changed-path verification failed", file=sys.stderr)
            return 1
        executed.append("verify")

    scripts_changed = any(path.startswith("scripts/") for path in paths)
    policy_rc, policy_gates = run_ready_review_policy(
        paths,
        policy_tests_covered=not policy_only and scripts_changed,
        doc_drift_covered=set() if policy_only else ready_review_doc_drift_coverage(paths),
    )
    executed.extend(policy_gates)
    if policy_rc != 0:
        print("ready-review: FAIL - policy profile failed", file=sys.stderr)
        return 1

    if policy_only:
        print("ready-review: PASS (policy profile)")
        return 0

    print(f"ready-review: PASS ({', '.join(executed)})")
    return 0


def pre_push(args: argparse.Namespace) -> int:
    full = os.environ.get("AXIS_PRE_PUSH_FULL", "").lower() in {"1", "true", "yes", "on"}
    if full:
        print("pre-push: AXIS_PRE_PUSH_FULL is set; running ready-review.")
        return ready_review(argparse.Namespace(since=None, policy_only=False))

    range_spec = diff_range()
    paths = changed_paths(range_spec)
    dotnet = not paths or any(
        re.search(
            r"^(src/|tests/|Directory[.]|Axis[.]sln$|global[.]json$|[.]editorconfig$|[.]github/workflows/build-and-test[.]yml$)",
            p,
        )
        for p in paths
    )
    docs = not paths or any(re.search(r"^(AGENTS[.]md|README[.]md|docs/|[.]github/PULL_REQUEST_TEMPLATE[.]md)", p) for p in paths)
    skills = not paths or any(p.startswith(f"{REPO_SKILLS_DIR}/") for p in paths)
    scripts_changed = not paths or any(p.startswith("scripts/") for p in paths)
    renovate_config = not paths or ".github/renovate.json5" in paths
    failed: list[str] = []

    def step(name: str, fn: callable[[], int]) -> None:
        print()
        print(f"> {name}")
        try:
            rc = fn()
        except CheckError as exc:
            print(exc, file=sys.stderr)
            rc = 1
        if rc == 0:
            print(f"OK {name}")
        else:
            print(f"FAIL {name}")
            failed.append(name)

    print("pre-push: quick gate")
    print("  Runs cheap sanity checks before the network push.")
    print("  Run `python scripts/axis.py ready-review` before marking a PR ready for review.")

    if dotnet:
        step(".NET test naming", lambda: check_test_naming())
        step(".NET test project classification", lambda: check_test_project_classification())

    if docs:
        step("doc navigation", lambda: check_doc_navigation())
        step("doc size budgets", lambda: check_doc_size_budgets())

    if skills:
        step("Repo skills", lambda: check_repo_skills())

    if scripts_changed:
        step("scripts standard", lambda: check_scripts_standard())

    if renovate_config:
        step("Renovate config", lambda: check_renovate_config(None))

    print()
    if not failed:
        print("pre-push: PASS")
        return 0
    print(f"pre-push: FAIL - {len(failed)} step(s): {' '.join(failed)}", file=sys.stderr)
    return 1


def generate_api_contracts(_args: argparse.Namespace | None = None) -> int:
    for checker in (check_dotnet_sdk, check_frontend_toolchain):
        rc = checker()
        if rc != 0:
            return rc
    commands = [
        ([exe("dotnet"), "build", "src/Axis.Api/Axis.Api.csproj", "--nologo"], ROOT, None),
        (
            [
                exe("dotnet"),
                "tool",
                "run",
                "swagger",
                "tofile",
                "--output",
                str(ROOT / "openapi.json"),
                "bin/Debug/net8.0/Axis.Api.dll",
                "v1",
            ],
            ROOT / "src" / "Axis.Api",
            {"ASPNETCORE_ENVIRONMENT": "Testing", "DOTNET_ENVIRONMENT": "Testing"},
        ),
    ]
    for command, cwd, env in commands:
        result = run(command, cwd=cwd, env=env, check=False)
        if result.returncode != 0:
            return result.returncode
    return run_frontend_npm(["run", "gen:api-types"]).returncode


def install_hooks(_args: argparse.Namespace | None = None) -> int:
    current_hooks = run([exe("git"), "config", "--get", "core.hooksPath"], capture=True, check=False)
    if current_hooks.returncode not in (0, 1, 5):
        return current_hooks.returncode

    hooks_value = current_hooks.stdout.strip() if current_hooks.returncode == 0 else ""
    if hooks_value:
        repo_hooks_path = (ROOT / "scripts" / "hooks").resolve()
        hooks_path = Path(hooks_value)
        resolved_hooks_path = hooks_path if hooks_path.is_absolute() else (ROOT / hooks_path)
        normalized_hooks_value = hooks_value.replace("\\", "/").rstrip("/")
        is_repo_hooks_path = (
            normalized_hooks_value in {"scripts/hooks", "./scripts/hooks"}
            or resolved_hooks_path.resolve() == repo_hooks_path
        )
        if not is_repo_hooks_path:
            print(
                "install-hooks: refusing to overwrite existing core.hooksPath "
                f"({hooks_value}). Clear it explicitly or move the hook manually.",
                file=sys.stderr,
            )
            return 1

        unset_result = run([exe("git"), "config", "--unset-all", "core.hooksPath"], check=False)
        if unset_result.returncode not in (0, 5):
            return unset_result.returncode

    hook_path_result = run([exe("git"), "rev-parse", "--git-path", "hooks/pre-push"], capture=True, check=False)
    if hook_path_result.returncode != 0:
        return hook_path_result.returncode

    source = ROOT / "scripts" / "hooks" / "pre-push"
    target = Path(hook_path_result.stdout.strip())
    if not target.is_absolute():
        target = ROOT / target
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)
    if os.name != "nt":
        target.chmod(target.stat().st_mode | 0o111)

    print(f"Installed: {target} (pre-push runs python scripts/axis.py pre-push).")
    return 0


def _command_version(name: str, *version_args: str, env: dict[str, str] | None = None) -> tuple[str, str]:
    ok, version, resolved = command_version_line(name, *version_args, env=env)
    if not ok:
        return "FAIL", version
    return "OK", f"{version} ({resolved})"


def _python_module_version(module_name: str, package_name: str) -> tuple[str, str]:
    if importlib.util.find_spec(module_name) is None:
        return (
            "FAIL",
            f"{package_name} is not installed for {sys.executable}; install with "
            f"`{sys.executable} -m pip install {package_name}`",
        )

    try:
        module = importlib.import_module(module_name)
    except Exception as exc:  # pragma: no cover - import side effects are environment-specific
        return "FAIL", f"{package_name} import failed: {exc}"

    version = getattr(module, "__version__", "available")
    return "OK", f"{package_name} {version} ({sys.executable})"


def _http_ok(url: str, timeout_seconds: float = 1.5) -> bool:
    try:
        with urllib.request.urlopen(url, timeout=timeout_seconds) as response:
            return 200 <= response.status < 300
    except (OSError, urllib.error.URLError):
        return False


def _docker_host_ping_ok(docker_host: str | None) -> bool:
    if not docker_host:
        return False

    parsed = urllib.parse.urlparse(docker_host)
    if parsed.scheme != "tcp" or not parsed.netloc:
        return False

    return _http_ok(f"http://{parsed.netloc}/_ping")


def _docker_info_ok(env: dict[str, str] | None = None) -> bool:
    if shutil.which(exe("docker")) is None and shutil.which("docker") is None:
        return False
    result = run_optional([exe("docker"), "info"], env=env, timeout=DOCKER_PROBE_TIMEOUT_SECONDS)
    return result is not None and result.returncode == 0


def _docker_compose_ok() -> bool:
    if shutil.which(exe("docker")) is None and shutil.which("docker") is None:
        return False
    result = run_optional([exe("docker"), "compose", "version"], timeout=DOCKER_PROBE_TIMEOUT_SECONDS)
    return result is not None and result.returncode == 0


def compose_args(project_name: str, compose_file: Path, *args: str) -> list[str]:
    return [
        exe("docker"),
        "compose",
        "-p",
        project_name,
        "-f",
        str(compose_file),
        *args,
    ]


def local_dev_env_args() -> list[str]:
    if LOCAL_DEV_ENV_FILE.is_file():
        return ["--env-file", str(LOCAL_DEV_ENV_FILE)]
    return []


def local_dev_compose_args(*args: str) -> list[str]:
    return compose_args(
        LOCAL_DEV_PROJECT_NAME,
        LOCAL_DEV_COMPOSE_FILE,
        *local_dev_env_args(),
        *args,
    )


def local_dev_shell_argv(service: str, exec_command: list[str]) -> list[str]:
    command = exec_command[1:] if exec_command[:1] == ["--"] else exec_command
    if command:
        return command
    return [LOCAL_DEV_SERVICE_SHELL.get(service, LOCAL_DEV_DEFAULT_SHELL)]


def local_dev_smoke_env() -> dict[str, str]:
    env = {
        "E2E_API_URL": LOCAL_DEV_API_BASE_URL,
        "E2E_BASE_URL": LOCAL_DEV_BROWSER_BASE_URL,
        "E2E_BROWSER_HOME": str(LOCAL_DEV_BROWSER_HOME),
        "E2E_SKIP_WEB_SERVER": "1",
        "E2E_VERIFY_ORIGIN": LOCAL_DEV_BROWSER_BASE_URL,
    }
    if LOCAL_ROOT_CA_PEM.is_file():
        env["NODE_EXTRA_CA_CERTS"] = str(LOCAL_ROOT_CA_PEM)
    return env


def ensure_local_dev_smoke_browser_trust() -> int:
    if not LOCAL_ROOT_CA_PEM.is_file():
        print(
            "local-dev smoke: local root CA is missing. "
            "Run `python scripts/axis.py local-dev certs` first.",
            file=sys.stderr,
        )
        return 1

    fingerprint = hashlib.sha256(LOCAL_ROOT_CA_PEM.read_bytes()).hexdigest()
    marker = LOCAL_DEV_BROWSER_HOME / ".axis-root-ca.sha256"
    nss_database = LOCAL_DEV_BROWSER_HOME / ".pki" / "nssdb"

    if (
        (nss_database / "cert9.db").is_file()
        and marker.is_file()
        and marker.read_text(encoding="utf-8").strip() == fingerprint
    ):
        return 0

    LOCAL_DEV_BROWSER_HOME.mkdir(parents=True, exist_ok=True)
    LOCAL_DEV_BROWSER_HOME.chmod(0o700)

    build = run(local_dev_compose_args("--profile", "e2e", "build", "e2e"), check=False)
    if build.returncode != 0:
        return build.returncode

    user_args: list[str] = []
    getuid = getattr(os, "getuid", None)
    getgid = getattr(os, "getgid", None)
    if callable(getuid) and callable(getgid):
        user_args = ["--user", f"{getuid()}:{getgid()}"]

    trust = run(
        local_dev_compose_args(
            "--profile",
            "e2e",
            "run",
            "--rm",
            "--no-deps",
            *user_args,
            "--entrypoint",
            "sh",
            "-v",
            f"{LOCAL_DEV_BROWSER_HOME}:/browser-home",
            "e2e",
            "./scripts/import-browser-ca.sh",
            "/browser-home/.pki/nssdb",
            "/https/rootCA.pem",
        ),
        check=False,
    )
    if trust.returncode != 0:
        return trust.returncode
    if not (nss_database / "cert9.db").is_file():
        print("local-dev smoke: Chromium trust store was not created", file=sys.stderr)
        return 1

    marker.write_text(f"{fingerprint}\n", encoding="utf-8")
    return 0


def require_local_dev_smoke_services() -> int:
    result = run(
        local_dev_compose_args("ps", "--services", "--status", "running"),
        capture=True,
        check=False,
    )
    if result.returncode != 0:
        emit_captured_process(result)
        return result.returncode

    running = {line.strip() for line in (result.stdout or "").splitlines() if line.strip()}
    missing = [service for service in LOCAL_DEV_SMOKE_SERVICES if service not in running]
    if missing:
        print(
            "local-dev smoke: required services are not running: "
            f"{', '.join(missing)}. Run `python scripts/axis.py local-dev up` first.",
            file=sys.stderr,
        )
        return 1
    return 0


def local_dev_smoke(args: argparse.Namespace) -> int:
    rc = check_frontend_toolchain()
    if rc != 0:
        return rc

    ok, detail = playwright_chromium_status()
    if not ok:
        print(f"local-dev smoke: Playwright Chromium unavailable - {detail}", file=sys.stderr)
        return 1

    rc = require_local_dev_smoke_services()
    if rc != 0:
        return rc

    rc = ensure_local_dev_smoke_browser_trust()
    if rc != 0:
        return rc

    smoke_args = passthrough_args(getattr(args, "smoke_args", []))
    if not smoke_args:
        smoke_args = ["e2e/local-dev-smoke.pw.ts"]
    return run_frontend_npm(
        ["run", "test:e2e", "--", *smoke_args],
        env_overrides=local_dev_smoke_env(),
    ).returncode


def require_docker_compose(label: str) -> int:
    if _docker_compose_ok():
        return 0
    print(f"{label}: Docker Compose v2 is not available in this shell", file=sys.stderr)
    return 1


def run_required(command: list[str]) -> int:
    result = run(command, check=False)
    return result.returncode


def restrict_local_cert_permissions() -> None:
    if os.name == "nt":
        return
    LOCAL_CERT_DIR.chmod(0o700)
    LOCAL_ROOT_CA_KEY.chmod(0o600)
    LOCALHOST_KEY.chmod(0o600)


def local_dev_certs(_args: argparse.Namespace | None = None) -> int:
    openssl = find_openssl()
    if openssl is None:
        print(
            "local-dev certs: OpenSSL is not available in PATH or Git for Windows usr/bin",
            file=sys.stderr,
        )
        return 1

    LOCAL_CERT_DIR.mkdir(parents=True, exist_ok=True)
    LOCALHOST_EXT.write_text(
        "\n".join(
            [
                "authorityKeyIdentifier=keyid,issuer",
                "basicConstraints=CA:FALSE",
                "keyUsage=digitalSignature,keyEncipherment",
                "extendedKeyUsage=serverAuth",
                "subjectAltName=@alt_names",
                "",
                "[alt_names]",
                "DNS.1=localhost",
                "DNS.2=api",
                "DNS.3=web",
                "IP.1=127.0.0.1",
                "IP.2=::1",
                "",
            ]
        ),
        encoding="utf-8",
        newline="\n",
    )

    commands = [
        [openssl, "genrsa", "-out", str(LOCAL_ROOT_CA_KEY), "4096"],
        [
            openssl,
            "req",
            "-x509",
            "-new",
            "-nodes",
            "-key",
            str(LOCAL_ROOT_CA_KEY),
            "-sha256",
            "-days",
            "825",
            "-out",
            str(LOCAL_ROOT_CA_PEM),
            "-subj",
            "/CN=Axis Local Dev Root CA",
        ],
        [openssl, "x509", "-outform", "der", "-in", str(LOCAL_ROOT_CA_PEM), "-out", str(LOCAL_ROOT_CA_CER)],
        [openssl, "genrsa", "-out", str(LOCALHOST_KEY), "2048"],
        [
            openssl,
            "req",
            "-new",
            "-key",
            str(LOCALHOST_KEY),
            "-out",
            str(LOCALHOST_CSR),
            "-subj",
            "/CN=localhost",
        ],
        [
            openssl,
            "x509",
            "-req",
            "-in",
            str(LOCALHOST_CSR),
            "-CA",
            str(LOCAL_ROOT_CA_PEM),
            "-CAkey",
            str(LOCAL_ROOT_CA_KEY),
            "-CAcreateserial",
            "-out",
            str(LOCALHOST_CERT),
            "-days",
            "825",
            "-sha256",
            "-extfile",
            str(LOCALHOST_EXT),
        ],
    ]
    for command in commands:
        rc = run_required(command)
        if rc != 0:
            return rc

    restrict_local_cert_permissions()
    print(f"local-dev certs: generated files in {path_label(LOCAL_CERT_DIR)}")
    return 0


def local_dev(args: argparse.Namespace) -> int:
    if args.local_dev_command == "certs":
        return local_dev_certs(args)

    rc = require_docker_compose("local-dev")
    if rc != 0:
        return rc

    command = args.local_dev_command
    if command == "up":
        compose = ["up", "-d"]
        if args.build:
            compose.append("--build")
        compose.extend(args.services)
        return run(local_dev_compose_args(*compose), check=False).returncode

    if command == "down":
        compose = ["down", "--remove-orphans"]
        if args.volumes:
            compose.append("--volumes")
        return run(local_dev_compose_args(*compose), check=False).returncode

    if command in {"start", "stop", "restart"}:
        return run(local_dev_compose_args(command, *args.services), check=False).returncode

    if command == "recreate":
        if not args.services:
            print("local-dev recreate: name at least one service", file=sys.stderr)
            return 1
        return run(local_dev_compose_args("up", "-d", "--force-recreate", *args.services), check=False).returncode

    if command == "status":
        return run(local_dev_compose_args("ps"), check=False).returncode

    if command == "logs":
        compose = ["logs"]
        if args.follow:
            compose.append("-f")
        compose.extend(args.services)
        return run(local_dev_compose_args(*compose), check=False).returncode

    if command == "shell":
        shell_command = local_dev_shell_argv(args.service, args.exec_command)
        return run(
            local_dev_compose_args("exec", "-it", args.service, *shell_command),
            check=False,
        ).returncode

    if command == "psql":
        return run(
            local_dev_compose_args("exec", "postgres", "psql", "-U", "axis", "-d", args.database),
            check=False,
        ).returncode

    if command == "e2e":
        build = run(local_dev_compose_args("--profile", "e2e", "build", "e2e"), check=False)
        if build.returncode != 0:
            return build.returncode
        e2e_args = passthrough_args(getattr(args, "e2e_args", []))
        return run(
            local_dev_compose_args("--profile", "e2e", "run", "--rm", "--no-deps", "e2e", *e2e_args),
            check=False,
        ).returncode

    if command == "smoke":
        return local_dev_smoke(args)

    if command == "observability":
        obs_command = args.observability_command
        if obs_command == "up":
            return run(local_dev_compose_args("--profile", "observability", "up", "-d", "otel-lgtm"), check=False).returncode
        if obs_command == "stop":
            return run(local_dev_compose_args("--profile", "observability", "stop", "otel-lgtm"), check=False).returncode
        if obs_command == "status":
            return run(local_dev_compose_args("--profile", "observability", "ps", "otel-lgtm"), check=False).returncode
        if obs_command == "logs":
            compose = ["--profile", "observability", "logs"]
            if args.follow:
                compose.append("-f")
            compose.append("otel-lgtm")
            return run(local_dev_compose_args(*compose), check=False).returncode

    if command == "reset-db":
        down = run(local_dev_compose_args("down"), check=False)
        if down.returncode != 0:
            return down.returncode
        remove = run([exe("docker"), "volume", "rm", LOCAL_DEV_POSTGRES_VOLUME], check=False, capture=True)
        remove_output = "\n".join(part for part in [remove.stdout, remove.stderr] if part)
        if remove.returncode != 0 and "No such volume" not in remove_output:
            if remove_output:
                print(remove_output, file=sys.stderr)
            return remove.returncode
        return run(local_dev_compose_args("up", "-d"), check=False).returncode

    if command == "reset-all":
        down = run(local_dev_compose_args("down", "--volumes"), check=False)
        if down.returncode != 0:
            return down.returncode
        return run(local_dev_compose_args("up", "-d"), check=False).returncode

    raise CheckError(f"Unknown local-dev command: {command}")


def _wsl_docker_ok() -> bool:
    if os.name != "nt" or shutil.which("wsl.exe") is None:
        return False
    result = run_optional(
        ["wsl.exe", "bash", "-lc", "docker info >/dev/null 2>&1"],
        timeout=DOCKER_PROBE_TIMEOUT_SECONDS,
    )
    return result is not None and result.returncode == 0


def setup_tool_ready(tool: str) -> bool:
    if tool == "dotnet":
        return dotnet_sdk_status()[0]
    if tool == "node":
        return node_version_status()[0]
    if tool == "lychee":
        resolved = find_lychee()
        return resolved is not None and lychee_version_status(resolved)[0]
    if tool == "gh":
        ok, version_line, _resolved = command_version_line("gh", "--version")
        match = re.search(r"\bgh version ([0-9]+(?:[.][0-9]+)+)\b", version_line)
        return bool(
            ok
            and match is not None
            and _version_sort_key(match.group(1)) >= _version_sort_key(axis_setup.GH_VERSION)
        )
    raise CheckError(f"Unknown setup-managed tool: {tool}")


def setup_preflight(profile: str) -> int:
    normalized = "review" if profile == "all" else profile
    rc = doctor(argparse.Namespace(profile=profile, strict=True))
    if rc != 0:
        if normalized == "review" and not setup_tool_ready("lychee"):
            print("setup: rerun with --install-user-tools to install the pinned Lychee artifact", file=sys.stderr)
        return rc
    if normalized in {"local-dev", "review"} and find_openssl() is None:
        print(
            "setup: FAIL - OpenSSL is required for local HTTPS certificates; "
            "Axis will not install an OS package automatically",
            file=sys.stderr,
        )
        return 1
    return 0


def setup(args: argparse.Namespace) -> int:
    profile = getattr(args, "profile", "build")
    browsers = getattr(args, "browsers", False)
    install_user_tools = getattr(args, "install_user_tools", False)
    plan_only = getattr(args, "plan_only", False)
    try:
        platform_spec = axis_setup.detect_platform()
        plan = axis_setup.setup_plan(
            profile=profile,
            install_user_tools=install_user_tools,
            browsers=browsers,
            platform_spec=platform_spec,
        )
    except axis_setup.SetupError as exc:
        print(f"setup: FAIL - {exc}", file=sys.stderr)
        return 1

    print(f"axis setup (profile={profile}, platform={platform_spec.label})")
    if plan_only:
        for index, label in enumerate(plan, 1):
            print(f"{index}. {label}")
        print("setup plan: no checks, downloads, or repository mutations were performed")
        return 0

    if install_user_tools:
        managed_tools = axis_setup.managed_tools_for_profile(profile)
        missing = tuple(tool for tool in managed_tools if not setup_tool_ready(tool))
        try:
            for tool in missing:
                try:
                    axis_setup.asset_name(tool, platform_spec)
                except axis_setup.SetupError as exc:
                    raise axis_setup.SetupError(
                        f"no verified portable artifact is available for missing `{tool}`: {exc}"
                    ) from exc
            axis_setup.confirm_install(
                missing,
                assume_yes=getattr(args, "yes", False),
                stdin=sys.stdin,
            )
            for tool in missing:
                print(f"> install pinned user-local {tool} {axis_setup.tool_version(tool)}")
                installed = axis_setup.install_tool(tool, platform_spec=platform_spec)
                print(f"  installed: {installed}")
        except (OSError, axis_setup.SetupError) as exc:
            print(f"setup: FAIL - {exc}", file=sys.stderr)
            return 1

    rc = setup_preflight(profile)
    if rc != 0:
        return rc

    steps: list[tuple[str, callable[[], int]]] = [
        (
            "restore .NET dependencies",
            lambda: run([exe("dotnet"), "restore", "Axis.sln"], check=False).returncode,
        ),
        ("install frontend dependencies", lambda: run_frontend_npm(["ci"]).returncode),
    ]
    normalized = "review" if profile == "all" else profile
    if browsers or normalized in {"local-dev", "review"}:
        steps.append(
            (
                "install Playwright Chromium",
                lambda: run_frontend_npm(["exec", "--", "playwright", "install", "chromium"]).returncode,
            )
        )
    if normalized in {"local-dev", "review"}:
        steps.extend(
            [
                ("generate local HTTPS certificates", lambda: local_dev_certs(args)),
                ("install repository pre-push hook", lambda: install_hooks(args)),
            ]
        )

    for label, action in steps:
        print(f"> {label}")
        rc = action()
        if rc != 0:
            print(f"setup: FAIL - {label}", file=sys.stderr)
            return rc

    print("setup: OK")
    if normalized == "review":
        print("review authentication remains interactive: run `gh auth status` and `coderabbit auth status`")
    return 0


def doctor(args: argparse.Namespace) -> int:
    profile = getattr(args, "profile", "local-dev")
    profile_groups = {
        "core": {"core"},
        "build": {"core", "build"},
        "local-dev": {"core", "build", "local-dev"},
        "review": {"core", "build", "local-dev", "review"},
        "all": {"core", "build", "local-dev", "review"},
    }
    if profile not in profile_groups:
        raise CheckError(f"Unknown doctor profile: {profile}")

    groups = profile_groups[profile]
    rows: list[tuple[str, str, str]] = []

    def record(status: str, label: str, detail: str) -> None:
        rows.append((status, label, detail))

    record("OK", "repo", str(ROOT))
    record("OK", "os", f"{platform.system()} {platform.release()} ({platform.machine()})")
    record("OK", "python", f"{platform.python_version()} ({sys.executable})")

    python_in_path = shutil.which("python") or shutil.which("python3") or shutil.which("py")
    if python_in_path:
        record("OK", "python launcher", python_in_path)
    else:
        record("WARN", "python launcher", "not found in PATH; use `python3` on WSL/Linux or `py -3` on Windows")

    git_status, git_detail = _command_version("git", "--version")
    record(git_status, "git", git_detail)

    frontend_env: dict[str, str] | None = None
    node_ok = False
    if "build" in groups:
        dotnet_ok, dotnet_detail = dotnet_sdk_status()
        record("OK" if dotnet_ok else "FAIL", ".NET SDK", dotnet_detail)

        frontend_env = frontend_toolchain_env()
        node_ok, node_detail = node_version_status(frontend_env)
        record("OK" if node_ok else "FAIL", "node", node_detail)
        npm_status, npm_detail = _command_version("npm", "--version", env=frontend_env)
        record(npm_status, "npm", npm_detail)

        if os.name == "nt":
            npm_cmd = shutil.which("npm.cmd")
            npm_ps1 = shutil.which("npm.ps1")
            if npm_cmd:
                detail = f"native npm shim available ({npm_cmd})"
                if npm_ps1:
                    detail += f"; alternate npm launcher also detected ({npm_ps1})"
                record("OK", "npm adapter", detail)

    if "local-dev" in groups:
        if node_ok:
            chromium_ok, chromium_detail = playwright_chromium_status(frontend_env)
            record("OK" if chromium_ok else "WARN", "playwright chromium", chromium_detail)
        else:
            record("WARN", "playwright chromium", "Node must resolve before checking Playwright browser artifacts")

        openssl = find_openssl()
        if openssl:
            record("OK", "openssl", openssl)
        else:
            record("WARN", "openssl", "required for local-dev certs; install OpenSSL on PATH or Git for Windows")

        docker_status, docker_detail = _command_version("docker", "--version")
        if docker_status == "FAIL":
            record(
                "WARN",
                "docker cli",
                f"{docker_detail}; install Docker Engine and Compose in the active environment",
            )
        else:
            record(docker_status, "docker cli", docker_detail)

        docker_current = _docker_info_ok()
        docker_host = os.environ.get("DOCKER_HOST")
        docker_host_ping = _docker_host_ping_ok(docker_host)
        tcp_docker = _http_ok("http://127.0.0.1:2375/_ping") or _http_ok("http://localhost:2375/_ping")
        wsl_docker = _wsl_docker_ok()

        if docker_current:
            record("OK", "docker endpoint", "docker info works in this shell")
        elif docker_host and docker_host_ping:
            record("OK", "docker endpoint", f"DOCKER_HOST={docker_host} responds; Testcontainers can use it")
        elif docker_host:
            record("FAIL", "docker endpoint", f"DOCKER_HOST={docker_host} is set, but the daemon did not respond")
        elif tcp_docker:
            record(
                "WARN",
                "docker endpoint",
                "an exported Docker endpoint responds; set DOCKER_HOST for the current session",
            )
        elif wsl_docker:
            record(
                "WARN",
                "docker endpoint",
                "Docker works from another detected execution context; run the canonical command there",
            )
        else:
            record("FAIL", "docker endpoint", "docker info failed; no reachable Docker endpoint detected")

        if _docker_compose_ok():
            record("OK", "docker compose", "docker compose version works")
        elif wsl_docker:
            record("WARN", "docker compose", "Docker Compose is available from another execution context")
        else:
            record("FAIL", "docker compose", "Docker Compose v2 not available from this execution context")

    if "review" in groups:
        lychee = find_lychee()
        if lychee is None:
            record(
                "FAIL",
                "lychee",
                f"Lychee {axis_setup.LYCHEE_VERSION} is required; install it per {TOOL_VERSIONS_DOC}",
            )
        else:
            lychee_ok, lychee_detail = lychee_version_status(lychee)
            record("OK" if lychee_ok else "FAIL", "lychee", lychee_detail)

        coderabbit_status, coderabbit_detail = coderabbit_doctor_status(strict=getattr(args, "strict", False))
        record(coderabbit_status, "coderabbit", coderabbit_detail)

    print(f"axis doctor (profile={profile})")
    for status, label, detail in rows:
        print(f"[{status:<4}] {label}: {detail}")

    failures = [label for status, label, _detail in rows if status == "FAIL"]
    if failures and getattr(args, "strict", False):
        print(f"doctor: FAIL - {len(failures)} blocking issue(s): {', '.join(failures)}", file=sys.stderr)
        return 1

    if failures:
        print("doctor: completed with blocking findings; rerun with --strict to fail on them")
    else:
        print("doctor: OK")
    return 0


def check_pr(args: argparse.Namespace) -> int:
    module_args: list[str] = []
    if args.title is not None:
        module_args.extend(["--title", args.title])
    if args.body_file is not None:
        module_args.extend(["--body-file", str(args.body_file)])
    if args.branch is not None:
        module_args.extend(["--branch", args.branch])
    return module_main("check-pr.py", module_args)


def check_renovate_config(_args: argparse.Namespace | None = None) -> int:
    if not RENOVATE_CONFIG_PATH.is_file():
        print("check-renovate-config FAIL: missing .github/renovate.json5", file=sys.stderr)
        return 1

    rc = check_frontend_toolchain()
    if rc != 0:
        return rc

    return run(
        [
            exe("npx"),
            "--yes",
            "--package",
            f"renovate@{REQUIRED_RENOVATE_VALIDATOR_VERSION}",
            "--",
            "renovate-config-validator",
            "--strict",
            "--no-global",
            str(RENOVATE_CONFIG_PATH),
        ],
        check=False,
    ).returncode


def main(argv: list[str] | None = None) -> int:
    configure_cli_text_streams()

    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    setup_parser = sub.add_parser("setup", help="Prepare a portable repository development profile")
    setup_parser.add_argument(
        "--profile",
        choices=("build", "local-dev", "review", "all"),
        default="build",
        help="Cumulative setup profile (default: build)",
    )
    setup_parser.add_argument(
        "--browsers",
        action="store_true",
        help="Compatibility option: add Playwright Chromium to any profile",
    )
    setup_parser.add_argument(
        "--plan-only",
        action="store_true",
        help="Print the platform-specific setup plan without checks, downloads, or mutations",
    )
    setup_parser.add_argument(
        "--install-user-tools",
        action="store_true",
        help="Install missing pinned tools into the current user's Axis data directory",
    )
    setup_parser.add_argument(
        "--yes",
        action="store_true",
        help="Confirm user-local tool downloads without an interactive prompt",
    )
    setup_parser.set_defaults(func=setup)
    doctor_parser = sub.add_parser("doctor", help="Diagnose tools required by a selected workflow profile")
    doctor_parser.add_argument(
        "--profile",
        choices=("core", "build", "local-dev", "review", "all"),
        default="local-dev",
        help="Tool group to diagnose (default: local-dev)",
    )
    doctor_parser.add_argument("--strict", action="store_true", help="Exit non-zero when required tools in the selected profile are unavailable")
    doctor_parser.set_defaults(func=doctor)
    sub.add_parser("install-hooks", help="Install the repository-managed pre-push hook").set_defaults(func=install_hooks)
    sub.add_parser("pre-push", help="Run the pre-push policy profile").set_defaults(func=pre_push)
    ready_review_parser = sub.add_parser("ready-review", help="Verify a clean checkpoint before review")
    ready_review_parser.add_argument("--since", help="Scope expensive verification after this checkpoint")
    ready_review_parser.add_argument(
        "--policy-only",
        action="store_true",
        help="Run only the deterministic policy profile shared with CI",
    )
    ready_review_parser.set_defaults(func=ready_review)
    verify_parser = sub.add_parser("verify", help="Run checks selected from changed paths")
    verify_parser.add_argument("--since", help="Scope verification to changes after this checkpoint plus the working tree")
    verify_parser.add_argument(
        "--plan-only",
        action="store_true",
        help="Print the changed-path verification plan without running commands",
    )
    verify_parser.set_defaults(func=verify)

    dotnet_parser = sub.add_parser("dotnet", help="Run repository-standard .NET commands")
    dotnet_sub = dotnet_parser.add_subparsers(dest="dotnet_command", required=True)
    for dotnet_passthrough in ("restore", "build", "test"):
        parser_for_dotnet = dotnet_sub.add_parser(
            dotnet_passthrough,
            help=f"Run dotnet {dotnet_passthrough} through the repository wrapper",
        )
        parser_for_dotnet.add_argument("dotnet_args", nargs=argparse.REMAINDER)
        parser_for_dotnet.set_defaults(func=dotnet_command)
    dotnet_format = dotnet_sub.add_parser("format", help="Format the solution or verify formatting")
    dotnet_format.add_argument("--check", action="store_true", help="Fail when dotnet format would change files")
    dotnet_format.add_argument("dotnet_args", nargs=argparse.REMAINDER)
    dotnet_format.set_defaults(func=dotnet_command)
    dotnet_run_api = dotnet_sub.add_parser("run-api", help="Run the Axis API project")
    dotnet_run_api.add_argument("dotnet_args", nargs=argparse.REMAINDER)
    dotnet_run_api.set_defaults(func=dotnet_command)
    dotnet_ef = dotnet_sub.add_parser("ef", help="Run Entity Framework tooling")
    dotnet_ef.add_argument("dotnet_args", nargs=argparse.REMAINDER)
    dotnet_ef.set_defaults(func=dotnet_command)

    frontend_parser = sub.add_parser("frontend", help="Run repository-standard frontend commands")
    frontend_sub = frontend_parser.add_subparsers(dest="frontend_command", required=True)
    frontend_sub.add_parser("install", help="Install locked frontend dependencies with npm ci").set_defaults(func=frontend_command)
    frontend_sub.add_parser("install-browsers", help="Install Playwright Chromium").set_defaults(func=frontend_command)
    frontend_sub.add_parser("ci", help="Run frontend type-check and lint gates").set_defaults(func=frontend_command)
    frontend_sub.add_parser("test", help="Run frontend unit tests").set_defaults(func=frontend_command)
    frontend_gen_api = frontend_sub.add_parser("gen-api-types", help="Generate TypeScript API types from OpenAPI")
    frontend_gen_api.add_argument("--check", action="store_true", help="Fail if generated frontend API types are stale")
    frontend_gen_api.set_defaults(func=frontend_command)
    frontend_ui_baseline = frontend_sub.add_parser("ui-baseline", help="Check or write the approved UI baseline")
    frontend_ui_baseline.add_argument("--write", action="store_true", help="Write the reviewed approved UI baseline")
    frontend_ui_baseline.set_defaults(func=frontend_command)
    frontend_script = frontend_sub.add_parser("script", help="Run an allow-listed package script")
    frontend_script.add_argument("script_name")
    frontend_script.add_argument("script_args", nargs=argparse.REMAINDER)
    frontend_script.set_defaults(func=frontend_command)

    local_dev_parser = sub.add_parser("local-dev", help="Manage the Docker Compose local-development stack")
    local_dev_sub = local_dev_parser.add_subparsers(dest="local_dev_command", required=True)
    local_dev_sub.add_parser("certs", help="Create local HTTPS certificates").set_defaults(func=local_dev)
    local_up = local_dev_sub.add_parser("up", help="Create and start local services")
    local_up.add_argument("--build", action="store_true")
    local_up.add_argument("services", nargs="*")
    local_up.set_defaults(func=local_dev)
    local_down = local_dev_sub.add_parser("down", help="Stop and remove local services")
    local_down.add_argument("--volumes", action="store_true")
    local_down.set_defaults(func=local_dev)
    for local_command in ("start", "stop", "restart"):
        parser_for_command = local_dev_sub.add_parser(local_command, help=f"{local_command.capitalize()} local services")
        parser_for_command.add_argument("services", nargs="*")
        parser_for_command.set_defaults(func=local_dev)
    local_recreate = local_dev_sub.add_parser("recreate", help="Recreate selected local services")
    local_recreate.add_argument("services", nargs="+")
    local_recreate.set_defaults(func=local_dev)
    local_dev_sub.add_parser("status", help="Show local service status").set_defaults(func=local_dev)
    local_logs = local_dev_sub.add_parser("logs", help="Show local service logs")
    local_logs.add_argument("-f", "--follow", action="store_true")
    local_logs.add_argument("services", nargs="*")
    local_logs.set_defaults(func=local_dev)
    local_shell = local_dev_sub.add_parser("shell", help="Open an interactive shell in a compose service container")
    local_shell.add_argument("service", nargs="?", default="api")
    local_shell.add_argument("exec_command", nargs=argparse.REMAINDER)
    local_shell.set_defaults(func=local_dev)
    local_psql = local_dev_sub.add_parser("psql", help="Open psql in the PostgreSQL service")
    local_psql.add_argument("--database", default="axis")
    local_psql.set_defaults(func=local_dev)
    local_e2e = local_dev_sub.add_parser("e2e", help="Run end-to-end tests against the local stack")
    local_e2e.add_argument("e2e_args", nargs=argparse.REMAINDER)
    local_e2e.set_defaults(func=local_dev)
    local_smoke = local_dev_sub.add_parser("smoke", help="Run local stack smoke checks")
    local_smoke.add_argument("smoke_args", nargs=argparse.REMAINDER)
    local_smoke.set_defaults(func=local_dev)
    local_observability = local_dev_sub.add_parser("observability", help="Manage the optional observability profile")
    local_observability_sub = local_observability.add_subparsers(dest="observability_command", required=True)
    local_observability_sub.add_parser("up", help="Start observability services").set_defaults(func=local_dev)
    local_observability_sub.add_parser("stop", help="Stop observability services").set_defaults(func=local_dev)
    local_observability_sub.add_parser("status", help="Show observability service status").set_defaults(func=local_dev)
    local_observability_logs = local_observability_sub.add_parser("logs", help="Show observability logs")
    local_observability_logs.add_argument("-f", "--follow", action="store_true")
    local_observability_logs.set_defaults(func=local_dev)
    local_dev_sub.add_parser("reset-db", help="Recreate the local database volume").set_defaults(func=local_dev)
    local_dev_sub.add_parser("reset-all", help="Recreate all local volumes and services").set_defaults(func=local_dev)

    check = sub.add_parser("check", help="Run an individual deterministic repository gate")
    check_sub = check.add_subparsers(dest="check_command", required=True)
    check_sub.add_parser("doc-drift", help="Check documented enforcement against repository truth").set_defaults(func=check_doc_drift)
    check_sub.add_parser("policy-tests", help="Run repository policy regression tests").set_defaults(func=check_policy_tests)
    check_sub.add_parser("text-encoding", help="Check repository text encoding and line endings").set_defaults(func=check_text_encoding)
    check_sub.add_parser("scripts-standard", help="Check repository script ownership conventions").set_defaults(func=check_scripts_standard)
    check_sub.add_parser("repo-skills", help="Validate repository skill contracts").set_defaults(func=check_repo_skills)
    check_sub.add_parser("renovate-config", help="Validate the Renovate configuration").set_defaults(func=check_renovate_config)
    check_sub.add_parser("test-naming", help="Validate test naming conventions").set_defaults(func=check_test_naming)
    check_sub.add_parser("test-project-classification", help="Validate test project classifications").set_defaults(func=check_test_project_classification)
    check_sub.add_parser("docker", help="Check Docker availability for integration tests").set_defaults(func=check_docker)
    check_sub.add_parser("dotnet-sdk", help="Check the required .NET SDK version").set_defaults(func=check_dotnet_sdk)
    check_sub.add_parser("frontend-toolchain", help="Check Node and npm versions").set_defaults(func=check_frontend_toolchain)
    check_sub.add_parser("playwright-browsers", help="Check Playwright Chromium availability").set_defaults(func=check_playwright_browsers)
    check_sub.add_parser("vulnerable-packages", help="Audit NuGet dependencies for vulnerabilities").set_defaults(func=check_vulnerable_packages)
    check_sub.add_parser("frontend-vulnerable-packages", help="Audit npm dependencies at high severity").set_defaults(func=check_frontend_vulnerable_packages)
    check_sub.add_parser("ef-domain-mapping", help="Check EF Core mappings against domain ownership").set_defaults(func=check_ef_domain_mapping)
    check_sub.add_parser("frontend-api-contracts", help="Check generated frontend API contracts").set_defaults(func=check_frontend_api_contracts)
    check_sub.add_parser("ui-baseline", help="Check the approved frontend UI baseline").set_defaults(func=check_ui_baseline)
    check_sub.add_parser("frontend-quality", help="Run deterministic frontend policy checks").set_defaults(func=check_frontend_quality)
    check_sub.add_parser("coderabbit-cli", help="Check the CodeRabbit CLI review dependency").set_defaults(func=check_coderabbit_cli)
    check_sub.add_parser("local-dev-docs", help="Check local-development docs against Compose").set_defaults(
        func=lambda _args: run_module_check("check-local-dev-docs.py", ["--check"])
    )
    check_sub.add_parser("doc-link-targets", help="Check local documentation link targets").set_defaults(
        func=lambda _args: run_module_check("check-doc-link-targets.py", ["--check"])
    )
    check_sub.add_parser("markdown-links", help="Check Markdown links with Lychee").set_defaults(func=check_markdown_links)
    check_sub.add_parser("doc-navigation", help="Check documentation navigation blocks").set_defaults(func=check_doc_navigation)
    check_sub.add_parser("doc-size-budgets", help="Check documentation size budgets").set_defaults(func=check_doc_size_budgets)
    check_sub.add_parser("doc-code-fences", help="Check canonical commands in documentation fences").set_defaults(
        func=lambda _args: run_module_check("check-doc-code-fences.py", ["--check"])
    )
    check_sub.add_parser("use-case-docs", help="Validate use-case documentation contracts").set_defaults(
        func=lambda _args: run_module_check("check-use-case-docs.py", ["--check"])
    )
    check_sub.add_parser("foundation-docs", help="Validate foundation documentation contracts").set_defaults(
        func=lambda _args: run_module_check("check-foundation-docs.py", ["--check"])
    )
    pr_parser = check_sub.add_parser("pr", help="Validate pull-request metadata and branch naming")
    pr_parser.add_argument("--title")
    pr_parser.add_argument("--body-file", type=Path)
    pr_parser.add_argument("--branch")
    pr_parser.set_defaults(func=check_pr)

    test = sub.add_parser("test", help="Run repository test profiles")
    test_sub = test.add_subparsers(dest="test_command", required=True)
    unit = test_sub.add_parser("unit", help="Run all convention-classified .NET unit test projects")
    unit.add_argument("dotnet_args", nargs=argparse.REMAINDER)
    unit.set_defaults(func=test_unit)

    generate = sub.add_parser("generate", help="Generate committed repository artifacts")
    generate_sub = generate.add_subparsers(dest="generate_command", required=True)
    generate_sub.add_parser("api-contracts", help="Generate OpenAPI and frontend API types").set_defaults(func=generate_api_contracts)

    args = parser.parse_args(argv)
    try:
        return int(args.func(args))
    except CheckError as exc:
        print(exc, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
