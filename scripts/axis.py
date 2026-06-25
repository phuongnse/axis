#!/usr/bin/env python3
"""Repository maintenance CLI.

Python is the default entrypoint for repository maintenance. Documented repo
workflows go through this CLI; ecosystem-native tools are implementation
details behind the wrapper.
"""

from __future__ import annotations

import argparse
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
REQUIRED_BUF_VERSION = "1.50.0"
REQUIRED_LYCHEE_VERSION = "0.23.0"
MINIMUM_CODERABBIT_CLI_VERSION = "0.6.0"
TOOL_VERSIONS_DOC = "docs/playbooks/scripts.md#tool-versions"
TECH_STACK_DOC = "docs/TECH_STACK.md"
GLOBAL_JSON_PATH = ROOT / "global.json"
NVMRC_PATH = ROOT / "frontend" / ".nvmrc"
LOCAL_DEV_COMPOSE_FILE = ROOT / "docker-compose.yml"
LOCAL_DEV_PROJECT_NAME = "axis"
LOCAL_DEV_POSTGRES_VOLUME = f"{LOCAL_DEV_PROJECT_NAME}_postgres_data"
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

if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis_repo  # noqa: E402
import doc_drift_domains  # noqa: E402
import sync_buf_yaml  # noqa: E402


class CheckError(RuntimeError):
    """Raised when a command fails."""


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
    path = env_path(env)
    if os.name == "nt" and not name.endswith(".exe"):
        cmd = shutil.which(f"{name}.cmd", path=path)
        if cmd:
            return cmd
    found = shutil.which(name, path=path)
    return found or name


def command_exists(name: str, *, env: dict[str, str] | None = None) -> bool:
    path = env_path(env)
    return shutil.which(name, path=path) is not None or shutil.which(f"{name}.cmd", path=path) is not None


def run(
    args: list[str],
    *,
    cwd: Path = ROOT,
    check: bool = True,
    capture: bool = False,
    env: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    merged_env = os.environ.copy()
    merged_env["PYTHONDONTWRITEBYTECODE"] = "1"
    if env:
        merged_env.update(env)
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
    )
    if check and result.returncode != 0:
        raise CheckError(f"{' '.join(args)} failed with exit code {result.returncode}")
    return result


def run_optional(
    args: list[str],
    *,
    cwd: Path = ROOT,
    env: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str] | None:
    try:
        return run(args, cwd=cwd, capture=True, check=False, env=env)
    except OSError:
        return None


def git(args: list[str], *, capture: bool = True, check: bool = True) -> str:
    result = run([exe("git"), *args], capture=capture, check=check)
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
    result = run([exe("git"), *args], capture=True, check=False)
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
    if not root.exists():
        return []
    return (
        p
        for p in root.rglob("*")
        if p.is_file()
        and p.suffix in suffixes
        and "bin" not in p.parts
        and "obj" not in p.parts
        and "node_modules" not in p.parts
    )


def git_ls_files(pattern: str | None = None) -> list[str]:
    args = ["ls-files"]
    if pattern is not None:
        args.append(pattern)
    return [line for line in git(args).splitlines() if line.strip()]


TEXT_ENCODING_SUFFIXES = {
    ".avsc",
    ".cs",
    ".cshtml",
    ".csproj",
    ".css",
    ".dockerignore",
    ".editorconfig",
    ".env",
    ".excalidraw",
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
    ".proto",
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


def check_text_encoding(_args: argparse.Namespace | None = None) -> int:
    paths = [ROOT / path for path in git_ls_files() if should_check_text_encoding(path)]
    issues = text_encoding_issues(paths)
    if issues:
        print("check-text-encoding FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        print("\nUse UTF-8 without BOM and LF line endings for tracked text files.", file=sys.stderr)
        return 1
    print(f"check-text-encoding: OK ({len(paths)} files scanned)")
    return 0


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
    for project in git_ls_files("tests/**/*.csproj"):
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
        for p in git_ls_files("tests/**/*.csproj")
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
        print("\nSee docs/playbooks/persistence-patterns.md#ef-core-aggregate-mapping-patterns", file=sys.stderr)
        return 1
    print("check-ef-domain-mapping: OK")
    return 0


def check_frontend_api_contracts(_args: argparse.Namespace | None = None) -> int:
    pattern = re.compile(r"(^|\s)(export\s+)?(interface|type)\s+[A-Za-z0-9_]*(Request|Response|Dto)\b")
    issues: list[str] = []
    root = ROOT / "frontend" / "src"
    for path in iter_files(root, (".ts", ".tsx")):
        normalized = rel(path)
        if normalized.endswith("frontend/src/lib/api-types.ts") or normalized.endswith("frontend/src/routeTree.gen.ts"):
            continue
        text = path.read_text(encoding="utf-8")
        for idx, line in enumerate(text.splitlines(), 1):
            if not pattern.search(line):
                continue
            if "components['schemas']" in line or 'components["schemas"]' in line:
                continue
            if "operations['" in line or 'operations["' in line:
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


def parse_frontend_primitive_contracts(contract_text: str) -> list[dict[str, object]]:
    block_pattern = re.compile(
        r"\{\s*"
        r"component:\s*'(?P<component>[^']+)'\s*,\s*"
        r"file:\s*'(?P<file>[^']+)'\s*,\s*"
        r"testFiles:\s*\[(?P<tests>[^\]]*)\]\s*,\s*"
        r"(?:(?:source:\s*'(?P<source>[^']+)'\s*,\s*)?)"
        r"(?:(?:sourceItem:\s*'(?P<source_item>[^']+)'\s*,\s*)?)"
        r"(?:(?:sourceReason:\s*'(?P<source_reason>[^']+)'\s*,\s*)?)"
        r"(?:(?:readiness:\s*'(?P<readiness>[^']+)'\s*,\s*"
        r"variants:\s*\[(?P<variants>[^\]]*)\]\s*,\s*"
        r"states:\s*\[(?P<states>[^\]]*)\]\s*,\s*"
        r"accessibility:\s*\[(?P<accessibility>[^\]]*)\]\s*,\s*"
        r"tokenFamilies:\s*\[(?P<token_families>[^\]]*)\]\s*,?\s*)?)"
        r"\}",
        re.DOTALL,
    )
    value_pattern = re.compile(r"'([^']+)'")
    contracts: list[dict[str, object]] = []

    for match in block_pattern.finditer(contract_text):
        contracts.append(
            {
                "component": match.group("component"),
                "file": match.group("file"),
                "testFiles": value_pattern.findall(match.group("tests")),
                "source": match.group("source") or "",
                "sourceItem": match.group("source_item") or "",
                "sourceReason": match.group("source_reason") or "",
                "readiness": match.group("readiness") or "",
                "variants": value_pattern.findall(match.group("variants") or ""),
                "states": value_pattern.findall(match.group("states") or ""),
                "accessibility": value_pattern.findall(match.group("accessibility") or ""),
                "tokenFamilies": value_pattern.findall(match.group("token_families") or ""),
            }
        )

    return contracts


def frontend_primitive_contract_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    ui_root = root / "frontend" / "src" / "components" / "ui"
    if not ui_root.exists():
        return issues

    ui_files = sorted(
        str(path.relative_to(root)).replace("\\", "/")
        for path in iter_files(ui_root, (".tsx",))
    )
    if not ui_files:
        return issues

    contract_path = root / "frontend" / "src" / "design-system" / "primitive-contracts.ts"
    if not contract_path.is_file():
        return [f"{path_label(contract_path)}: missing primitive contract registry"]

    allowed_token_families = {
        "border",
        "breakpoint",
        "color",
        "motion",
        "radius",
        "shadow",
        "sizing",
        "spacing",
        "typography",
    }
    contracts = parse_frontend_primitive_contracts(contract_path.read_text(encoding="utf-8"))
    contracted_files = sorted(str(contract["file"]) for contract in contracts)

    missing_contracts = sorted(set(ui_files) - set(contracted_files))
    stale_contracts = sorted(set(contracted_files) - set(ui_files))
    for file_path in missing_contracts:
        issues.append(
            f"{file_path}: UI primitive must be listed in frontend/src/design-system/primitive-contracts.ts"
        )
    for file_path in stale_contracts:
        issues.append(
            f"frontend/src/design-system/primitive-contracts.ts: stale primitive contract for missing file {file_path}"
        )

    for contract in contracts:
        component = str(contract["component"])
        file_path = str(contract["file"])
        test_files = list(contract["testFiles"])
        source = str(contract["source"])
        source_item = str(contract["sourceItem"])
        readiness = str(contract["readiness"])
        token_families = list(contract["tokenFamilies"])

        if not test_files:
            issues.append(f"{file_path}: {component} contract must name at least one test file")
        if source != "shadcn":
            issues.append(
                f"{file_path}: {component} contract source must be `shadcn`; "
                "Axis-owned components belong outside frontend/src/components/ui"
            )
        if not re.fullmatch(r"@shadcn/[A-Za-z0-9][A-Za-z0-9._/-]*", source_item):
            issues.append(f"{file_path}: {component} shadcn contract must name sourceItem `@shadcn/...`")
        if readiness not in {"ready", "candidate"}:
            issues.append(f"{file_path}: {component} contract readiness must be `ready` or `candidate`")
        for field_name in ("variants", "states", "accessibility", "tokenFamilies"):
            if not list(contract[field_name]):
                issues.append(f"{file_path}: {component} contract must list at least one {field_name} value")
        unknown_token_families = sorted(set(token_families) - allowed_token_families)
        if unknown_token_families:
            joined_values = ", ".join(f"`{value}`" for value in unknown_token_families)
            issues.append(
                f"{file_path}: {component} contract has unknown tokenFamilies values: {joined_values}"
            )

        for test_file in test_files:
            if not (root / test_file).is_file():
                issues.append(f"{file_path}: test file `{test_file}` does not exist")

    return issues


def parse_frontend_consumer_contracts(contract_text: str) -> list[dict[str, object]]:
    block_pattern = re.compile(
        r"\{\s*"
        r"surface:\s*'(?P<surface>[^']+)'\s*,\s*"
        r"kind:\s*'(?P<kind>[^']+)'\s*,\s*"
        r"route:\s*'(?P<route>[^']+)'\s*,\s*"
        r"component:\s*'(?P<component>[^']+)'\s*,\s*"
        r"file:\s*'(?P<file>[^']+)'\s*,\s*"
        r"owner:\s*'(?P<owner>[^']+)'\s*,\s*"
        r"readiness:\s*'(?P<readiness>[^']+)'\s*,\s*"
        r"primitives:\s*\[(?P<primitives>[^\]]*)\]\s*,\s*"
        r"states:\s*\[(?P<states>[^\]]*)\]\s*,\s*"
        r"evidence:\s*\[(?P<evidence>[^\]]*)\]\s*,\s*"
        r"testFiles:\s*\[(?P<tests>[^\]]*)\]\s*,?\s*"
        r"\}",
        re.DOTALL,
    )
    value_pattern = re.compile(r"'([^']+)'")
    contracts: list[dict[str, object]] = []

    for match in block_pattern.finditer(contract_text):
        contracts.append(
            {
                "surface": match.group("surface"),
                "kind": match.group("kind"),
                "route": match.group("route"),
                "component": match.group("component"),
                "file": match.group("file"),
                "owner": match.group("owner"),
                "readiness": match.group("readiness"),
                "primitives": value_pattern.findall(match.group("primitives")),
                "states": value_pattern.findall(match.group("states")),
                "evidence": value_pattern.findall(match.group("evidence")),
                "testFiles": value_pattern.findall(match.group("tests")),
            }
        )

    return contracts


def frontend_route_consumer_files(root: Path = ROOT) -> list[str]:
    route_root = root / "frontend" / "src" / "routes"
    if not route_root.exists():
        return []

    import_pattern = re.compile(
        r"import\s+(?!type\b)(?:[^'\";]+?)\s+from\s+['\"]@/"
        r"(?P<source>(?:features/[^/'\"]+(?:/components/[^'\"]+)?|components/shared/[^'\"]+))['\"]"
    )
    consumer_files: set[str] = set()
    for path in iter_files(route_root, (".tsx",)):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        if normalized.endswith("routeTree.gen.ts"):
            continue

        text = path.read_text(encoding="utf-8")
        for match in import_pattern.finditer(text):
            source = match.group("source")
            base = root / "frontend" / "src" / source
            candidates = (
                base.with_suffix(".tsx"),
                base.with_suffix(".ts"),
                base / "index.tsx",
                base / "index.ts",
            )
            for candidate in candidates:
                if candidate.is_file():
                    consumer_files.add(str(candidate.relative_to(root)).replace("\\", "/"))
                    break

    return sorted(consumer_files)


def frontend_consumer_contract_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    consumer_files = frontend_route_consumer_files(root)
    if not consumer_files:
        return issues

    contract_path = root / "frontend" / "src" / "design-system" / "consumer-contracts.ts"
    if not contract_path.is_file():
        return [f"{path_label(contract_path)}: missing consumer contract registry"]

    allowed_kinds = {"public", "auth", "authenticated"}
    allowed_evidence = {
        "api-state",
        "auth-state",
        "e2e-smoke",
        "form-state",
        "i18n",
        "layout-state",
        "route-state",
        "unit-test",
    }
    contracts = parse_frontend_consumer_contracts(contract_path.read_text(encoding="utf-8"))
    contracted_files = sorted(str(contract["file"]) for contract in contracts)

    missing_contracts = sorted(set(consumer_files) - set(contracted_files))
    stale_contracts = sorted(set(contracted_files) - set(consumer_files))
    for file_path in missing_contracts:
        issues.append(
            f"{file_path}: route-bound UI consumer must be listed in frontend/src/design-system/consumer-contracts.ts"
        )
    for file_path in stale_contracts:
        issues.append(
            f"frontend/src/design-system/consumer-contracts.ts: stale consumer contract for missing route-bound file {file_path}"
        )

    declared_routes: set[str] = set()
    route_root = root / "frontend" / "src" / "routes"
    route_declaration_pattern = re.compile(
        r"create(?:Lazy)?FileRoute\(\s*['\"](?P<route>[^'\"]+)['\"]"
    )
    if route_root.exists():
        for path in iter_files(route_root, (".tsx",)):
            route_text = path.read_text(encoding="utf-8")
            declared_routes.update(match.group("route") for match in route_declaration_pattern.finditer(route_text))

    for contract in contracts:
        component = str(contract["component"])
        file_path = str(contract["file"])
        kind = str(contract["kind"])
        route = str(contract["route"])
        owner = str(contract["owner"])
        readiness = str(contract["readiness"])
        evidence = list(contract["evidence"])
        test_files = list(contract["testFiles"])

        if kind not in allowed_kinds:
            issues.append(f"{file_path}: {component} contract kind must be public, auth, or authenticated")
        if readiness not in {"ready", "candidate"}:
            issues.append(f"{file_path}: {component} contract readiness must be `ready` or `candidate`")
        if route not in declared_routes:
            issues.append(f"{file_path}: {component} contract route `{route}` is not declared in frontend routes")
        if not owner:
            issues.append(f"{file_path}: {component} contract must name an owner")
        for field_name in ("primitives", "states", "evidence", "testFiles"):
            if not list(contract[field_name]):
                issues.append(f"{file_path}: {component} contract must list at least one {field_name} value")

        unknown_evidence = sorted(set(evidence) - allowed_evidence)
        if unknown_evidence:
            joined_values = ", ".join(f"`{value}`" for value in unknown_evidence)
            issues.append(f"{file_path}: {component} contract has unknown evidence values: {joined_values}")

        for test_file in test_files:
            if not (root / test_file).is_file():
                issues.append(f"{file_path}: test file `{test_file}` does not exist")

    return issues


def frontend_shadcn_primitive_files(root: Path = ROOT) -> set[str]:
    contract_path = root / "frontend" / "src" / "design-system" / "primitive-contracts.ts"
    if not contract_path.is_file():
        return set()

    contracts = parse_frontend_primitive_contracts(contract_path.read_text(encoding="utf-8"))
    ui_root = root / "frontend" / "src" / "components" / "ui"
    shadcn_files: set[str] = set()
    for contract in contracts:
        file_path = str(contract["file"])
        candidate = root / file_path
        if (
            str(contract["source"]) == "shadcn"
            and candidate.is_file()
            and candidate.parent == ui_root
            and candidate.suffix == ".tsx"
        ):
            shadcn_files.add(file_path)
    return shadcn_files


def frontend_radius_token_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    token_css = root / "frontend" / "src" / "design-system" / "tokens.css"
    if not token_css.is_file():
        return [f"{rel(token_css) if root == ROOT else token_css}: missing radius token source"]

    css = token_css.read_text(encoding="utf-8")
    if "--radius: 0.5rem;" not in css:
        issues.append(
            "frontend/src/design-system/tokens.css: --radius must stay 0.5rem (8px panel token)"
        )

    src_root = root / "frontend" / "src"
    if not src_root.exists():
        return issues

    oversized = re.compile(r"\brounded-(xl|2xl|3xl)\b")
    arbitrary = re.compile(r"\brounded-\[([^\]]+)\]")
    shadcn_primitive_files = frontend_shadcn_primitive_files(root)
    for path in iter_files(src_root, (".ts", ".tsx")):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        if normalized in shadcn_primitive_files:
            continue
        text = path.read_text(encoding="utf-8")
        for idx, line in enumerate(text.splitlines(), 1):
            if oversized.search(line):
                issues.append(f"{normalized}:{idx}: avoid radius above 8px on core UI surfaces: {line.strip()}")
            match = arbitrary.search(line)
            if match and "var(--radius" not in match.group(1):
                issues.append(f"{normalized}:{idx}: use shared radius tokens instead of arbitrary radius: {line.strip()}")
    return issues


def frontend_component_composition_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = [
        *frontend_primitive_contract_issues(root),
        *frontend_consumer_contract_issues(root),
    ]
    src_root = root / "frontend" / "src"
    route_root = src_root / "routes"
    ui_root = src_root / "components" / "ui"
    shared_root = src_root / "components" / "shared"

    if ui_root.exists():
        shadcn_file_name = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*[.]tsx$")
        for path in iter_files(ui_root, (".tsx",)):
            normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
            if not shadcn_file_name.fullmatch(path.name):
                issues.append(
                    f"{normalized}: shadcn UI primitive files must use registry kebab-case names"
                )

    if shared_root.exists():
        pascal_component_name = re.compile(r"^[A-Z][A-Za-z0-9]*[.]tsx$")
        camel_module_name = re.compile(r"^[a-z][A-Za-z0-9]*[.]ts$")
        for path in iter_files(shared_root, (".ts", ".tsx")):
            normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
            if path.suffix == ".tsx" and not pascal_component_name.fullmatch(path.name):
                issues.append(
                    f"{normalized}: shared React component files must use PascalCase names"
                )
            if path.suffix == ".ts" and not camel_module_name.fullmatch(path.name):
                issues.append(
                    f"{normalized}: shared non-component modules must use camelCase names"
                )

    if route_root.exists():
        for path in iter_files(route_root, (".tsx",)):
            normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
            if normalized.endswith("routeTree.gen.ts"):
                continue
            text = path.read_text(encoding="utf-8")
            for idx, line in enumerate(text.splitlines(), 1):
                if "className=" in line:
                    issues.append(
                        f"{normalized}:{idx}: route files compose page components only; move styled UI into a component"
                    )

    if src_root.exists():
        standard_control = re.compile(r"<\s*(button|input|label|select|textarea)\b")
        for path in iter_files(src_root, (".tsx",)):
            normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
            text = path.read_text(encoding="utf-8")
            in_ui_primitives = path.is_relative_to(ui_root) if hasattr(path, "is_relative_to") else False
            if not in_ui_primitives:
                for idx, line in enumerate(text.splitlines(), 1):
                    if "@base-ui/react" in line:
                        issues.append(
                            f"{normalized}:{idx}: headless UI primitives belong in shadcn primitives under frontend/src/components/ui, not feature components"
                        )
                    for match in standard_control.finditer(line):
                        issues.append(
                            f"{normalized}:{idx}: standard UI control <{match.group(1)}> must use a shared shadcn UI primitive from frontend/src/components/ui"
                        )
    return issues


def check_frontend_component_composition(_args: argparse.Namespace | None = None) -> int:
    issues = frontend_component_composition_issues()
    if issues:
        for issue in issues:
            print(f"check-frontend-component-composition FAIL: {issue}", file=sys.stderr)
        print("\nSee docs/playbooks/frontend.md#component-design", file=sys.stderr)
        return 1
    print("check-frontend-component-composition: OK")
    return 0


def frontend_tailwind_opacity_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    src_root = root / "frontend" / "src"
    if not src_root.exists():
        return issues

    allowed = {str(value) for value in range(0, 101, 5)}
    opacity_token = re.compile(
        r"\b(?:bg|text|border|from|via|to|ring|divide|placeholder|decoration|outline)-[A-Za-z0-9_-]+/(\d{1,3})\b"
    )
    opacity_utility = re.compile(r"\bopacity-(\d{1,3})\b")
    for path in iter_files(src_root, (".ts", ".tsx")):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        text = path.read_text(encoding="utf-8")
        for idx, line in enumerate(text.splitlines(), 1):
            for match in opacity_token.finditer(line):
                value = match.group(1)
                if value not in allowed:
                    issues.append(
                        f"{normalized}:{idx}: unsupported Tailwind opacity /{value}; use 0,5,10...100 or bracket syntax like /[0.{value}]"
                    )
            for match in opacity_utility.finditer(line):
                value = match.group(1)
                if value not in allowed:
                    issues.append(
                        f"{normalized}:{idx}: unsupported Tailwind opacity-{value}; use opacity-0, opacity-5, opacity-10...opacity-100"
                    )
    return issues


def frontend_design_token_usage_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    src_root = root / "frontend" / "src"
    if not src_root.exists():
        return issues

    raw_neutral_color = re.compile(
        r"\b(?:bg|text|border|from|via|to|ring|divide|placeholder|decoration|outline)-"
        r"(?:white|black|slate|gray|zinc|neutral|stone)(?:-\d{2,3})?(?:/[A-Za-z0-9.[\\]-]+)?\b"
    )
    raw_shadow = re.compile(
        r"(?<![-\w])(?:shadow(?![-\w])|shadow-(?:sm|md|lg|xl|2xl|inner|none|\[[^\]]+\]))"
    )
    arbitrary_color = re.compile(
        r"\b(?:bg|text|border|from|via|to|ring|divide|placeholder|decoration|outline)-"
        r"\[(?:linear-gradient|radial-gradient|hsl|rgb|#)[^\]]+\]"
    )
    shadcn_primitive_files = frontend_shadcn_primitive_files(root)

    for path in iter_files(src_root, (".ts", ".tsx")):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        if normalized in shadcn_primitive_files:
            continue
        text = path.read_text(encoding="utf-8")
        inside_contract_token_families = False
        inside_type_alias = False
        for idx, line in enumerate(text.splitlines(), 1):
            stripped = line.strip()
            if inside_type_alias:
                if ";" in stripped:
                    inside_type_alias = False
                continue

            if re.match(r"(?:export\s+)?type\s+\w+\s*=", stripped):
                if ";" not in stripped:
                    inside_type_alias = True
                continue

            if inside_contract_token_families:
                if "]" in line:
                    inside_contract_token_families = False
                continue

            if "tokenFamilies:" in line:
                if "]" not in line.split("tokenFamilies:", 1)[1]:
                    inside_contract_token_families = True
                continue

            if raw_neutral_color.search(line):
                issues.append(
                    f"{normalized}:{idx}: use semantic color tokens instead of raw neutral Tailwind color utilities: {line.strip()}"
                )
            if raw_shadow.search(line):
                issues.append(
                    f"{normalized}:{idx}: use named shadow tokens instead of raw Tailwind shadow utilities: {line.strip()}"
                )
            if arbitrary_color.search(line):
                issues.append(
                    f"{normalized}:{idx}: move arbitrary color or gradient values into design-system tokens: {line.strip()}"
                )
    return issues


def frontend_style_issues(root: Path = ROOT) -> list[str]:
    return [
        *frontend_radius_token_issues(root),
        *frontend_tailwind_opacity_issues(root),
        *frontend_design_token_usage_issues(root),
    ]


def check_frontend_style(_args: argparse.Namespace | None = None) -> int:
    issues = frontend_style_issues()
    if issues:
        for issue in issues:
            print(f"check-frontend-style FAIL: {issue}", file=sys.stderr)
        print("\nSee docs/playbooks/frontend.md#styling", file=sys.stderr)
        return 1
    print("check-frontend-style: OK")
    return 0


def frontend_form_schema_type_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    features_root = root / "frontend" / "src" / "features"
    if not features_root.exists():
        return issues

    form_values_interface = re.compile(r"\b(?:export\s+)?interface\s+([A-Za-z0-9_]*FormValues)\b")
    form_values_type = re.compile(r"\b(?:export\s+)?type\s+([A-Za-z0-9_]*FormValues)\s*=")
    for path in iter_files(features_root, (".ts", ".tsx")):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        if "/schemas/" not in normalized:
            continue

        text = path.read_text(encoding="utf-8")
        for idx, line in enumerate(text.splitlines(), 1):
            interface_match = form_values_interface.search(line)
            if interface_match:
                issues.append(
                    f"{normalized}:{idx}: {interface_match.group(1)} must be inferred from the Zod schema, not hand-authored"
                )
                continue

            type_match = form_values_type.search(line)
            if type_match and "z.infer" not in line:
                issues.append(
                    f"{normalized}:{idx}: {type_match.group(1)} must use z.infer from the schema factory"
                )
    return issues


def frontend_test_async_boundary_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    test_roots = [
        root / "frontend" / "src" / "test",
        root / "frontend" / "tests",
    ]
    ignored_call = re.compile(r"\bvoid\s+[A-Za-z_$][A-Za-z0-9_$]*(?:[.(])")
    for test_root in test_roots:
        if not test_root.exists():
            continue
        for path in iter_files(test_root, (".ts", ".tsx")):
            normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
            text = path.read_text(encoding="utf-8")
            for idx, line in enumerate(text.splitlines(), 1):
                if ignored_call.search(line):
                    issues.append(
                        f"{normalized}:{idx}: test code must await/return async work instead of fire-and-forget `void` calls"
                    )
    return issues


def frontend_quality_issues(root: Path = ROOT) -> list[str]:
    return [
        *frontend_form_schema_type_issues(root),
        *frontend_test_async_boundary_issues(root),
    ]


def check_frontend_quality(_args: argparse.Namespace | None = None) -> int:
    issues = frontend_quality_issues()
    if issues:
        for issue in issues:
            print(f"check-frontend-quality FAIL: {issue}", file=sys.stderr)
        print("\nSee docs/playbooks/frontend.md#state-management and #localization-and-theme-preferences", file=sys.stderr)
        return 1
    print("check-frontend-quality: OK")
    return 0


NAVIGATION_RE = re.compile(r"^> \*\*Navigation\*\*: .*\[[^\]]+\]\([^)]+\)")


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
        if not any(NAVIGATION_RE.search(line) for line in nav_lines):
            issues.append(f"{rel(path)}: navigation block must include at least one markdown link")
    if issues:
        print("check-doc-navigation FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1
    print(f"check-doc-navigation: OK ({len(files)} files scanned)")
    return 0


PLAYBOOK_DEFAULT_MAX_LINES = 100
PATTERN_ROUTER_MAX_LINES = 100
DOC_SIZE_BUDGETS = {
    "docs/playbooks/patterns.md": PATTERN_ROUTER_MAX_LINES,
    "docs/playbooks/patterns-index.md": PATTERN_ROUTER_MAX_LINES,
}


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
                "split by topic or route through patterns-index.md"
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


def check_buf_modules(_args: argparse.Namespace | None = None) -> int:
    return sync_buf_yaml.main_with_args(["--check"]) if hasattr(sync_buf_yaml, "main_with_args") else module_main("sync_buf_yaml.py", ["--check"])


def buf_breaking_base_ref() -> str | None:
    base_ref = os.environ.get("BASE_REF")
    if base_ref:
        return base_ref

    base = os.environ.get("BASE_BRANCH", "main")
    for candidate in (f"origin/{base}", base):
        if ref_exists(candidate):
            return candidate
    return None


def check_buf_breaking_against_base(_args: argparse.Namespace | None = None) -> int:
    base_ref = buf_breaking_base_ref()
    if not base_ref:
        base = os.environ.get("BASE_BRANCH", "main")
        print(
            f"buf-breaking-against-base FAIL: cannot determine base ref; set BASE_REF "
            f"or fetch origin/{base}",
            file=sys.stderr,
        )
        return 1
    if not ref_exists(base_ref):
        print(f"buf-breaking-against-base FAIL: missing {base_ref}", file=sys.stderr)
        return 1
    buf = find_buf()
    if buf is None:
        print(
            f"buf-breaking-against-base FAIL: Buf CLI {REQUIRED_BUF_VERSION} is required, "
            f"but `buf` was not found in PATH. See {TOOL_VERSIONS_DOC}.",
            file=sys.stderr,
        )
        return 1
    buf_ok, buf_detail = buf_version_status(buf)
    if not buf_ok:
        print(
            f"buf-breaking-against-base FAIL: Buf CLI {REQUIRED_BUF_VERSION} is required; {buf_detail}. "
            f"Install the documented version or put it earlier in PATH. See {TOOL_VERSIONS_DOC}.",
            file=sys.stderr,
        )
        return 1
    if not (ROOT / "buf.yaml").is_file():
        print("buf-breaking-against-base: no buf.yaml - skip")
        return 0
    text = (ROOT / "buf.yaml").read_text(encoding="utf-8")
    paths = re.findall(r"^\s+- path: (.+)$", text, re.MULTILINE)
    for path in paths:
        if not path:
            continue
        existing = run([exe("git"), "ls-tree", "-r", "--name-only", base_ref, path], capture=True, check=False)
        if existing.returncode == 0 and re.search(r"[.]proto$", existing.stdout, re.MULTILINE):
            print(f"buf breaking {path} (vs {base_ref})")
            result = run([buf, "breaking", path, "--against", f".git#ref={base_ref},subdir={path}"], check=False)
            if result.returncode != 0:
                return result.returncode
        else:
            print(f"buf breaking: skip {path} (new on this branch - no baseline on {base_ref})")
    print("buf-breaking-against-base: OK")
    return 0


NON_PYTHON_UTILITY_SCRIPT_SUFFIXES = {".mjs", ".js", ".ps1", ".sh", ".cmd", ".bat"}
DOCS_UTILITY_SCRIPT_ROOTS = (
    Path("docs/scripts"),
    Path("docs/wireframes"),
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
FRONTMATTER_RE = re.compile(r"\A---\n(?P<header>.*?)\n---\n", re.DOTALL)
SKILL_MAX_LINES = 100
SKILL_AMBIGUOUS_WORD_RE = re.compile(
    r"\b(best[- ]effort|if you have time|nice to have|maybe|probably|hopefully)\b",
    re.IGNORECASE,
)
SKILL_REPO_REF_RE = re.compile(
    r"`(?P<target>(?:AGENTS\.md|\.github/[A-Za-z0-9._/#-]+|docs/[A-Za-z0-9._/#-]+|"
    r"scripts/[A-Za-z0-9._/#-]+|tests/[A-Za-z0-9._/#-]+|frontend/[A-Za-z0-9._/#-]+))`"
)
SKILL_MD_LINK_RE = re.compile(r"\[[^\]]+\]\((?P<target>[^)]+)\)")
SKILL_REQUIRED_SKILL_REFS = {
    "axis-api-contract": ("axis-design-gate", "axis-ready-review"),
    "axis-cross-module-contract": ("axis-design-gate", "axis-ready-review"),
    "axis-frontend-feature": ("axis-design-gate", "axis-ready-review"),
    "axis-use-case-implementation": ("axis-design-gate", "axis-ready-review"),
    "axis-review-feedback": ("axis-ready-review",),
}


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
    repo_prefixes = ("AGENTS.md", ".github/", "docs/", "scripts/", "tests/", "frontend/")
    if normalized.startswith(repo_prefixes):
        return root / normalized, target
    return skill_dir / normalized, target


def codex_skill_reference_issues(skill_md: Path, text: str, *, root: Path) -> list[str]:
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


def codex_skill_raw_command_issues(skill_md: Path, text: str, *, root: Path) -> list[str]:
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


def codex_skill_issues(*, root: Path = ROOT) -> list[str]:
    skills_root = root / ".agents" / "skills"
    if not skills_root.exists():
        return []

    issues: list[str] = []
    for skill_dir in sorted(skills_root.iterdir()):
        skill_path = rel_from(skill_dir, root)
        if not skill_dir.is_dir():
            issues.append(f"{skill_path}: repo skills must be directories")
            continue

        skill_name = skill_dir.name
        if SKILL_NAME_RE.fullmatch(skill_name) is None:
            issues.append(f"{skill_path}: skill folder name must be lowercase letters, digits, and hyphens")

        skill_md = skill_dir / "SKILL.md"
        if not skill_md.is_file():
            issues.append(f"{skill_path}: missing SKILL.md")
            continue

        text = skill_md.read_text(encoding="utf-8")
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
        issues.extend(codex_skill_raw_command_issues(skill_md, text, root=root))

        frontmatter = FRONTMATTER_RE.match(text)
        if frontmatter is None:
            issues.append(f"{rel_from(skill_md, root)}: missing YAML frontmatter delimited by ---")
            continue

        header = frontmatter.group("header")
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
        if not re.search(r"(?m)^#\s+\S", body):
            issues.append(f"{rel_from(skill_md, root)}: body must start with a Markdown H1")
        for required_skill in SKILL_REQUIRED_SKILL_REFS.get(skill_name, ()):
            if f"${required_skill}" not in text:
                issues.append(f"{rel_from(skill_md, root)}: must chain to ${required_skill}")
        issues.extend(codex_skill_reference_issues(skill_md, text, root=root))

        openai_yaml = skill_dir / "agents" / "openai.yaml"
        if not openai_yaml.is_file():
            issues.append(f"{skill_path}: missing agents/openai.yaml UI metadata")
            continue

        metadata = openai_yaml.read_text(encoding="utf-8")
        if "TODO" in metadata:
            issues.append(f"{rel_from(openai_yaml, root)}: remove template TODO text before committing")

        display_name = simple_yaml_value(metadata, "display_name")
        short_description = simple_yaml_value(metadata, "short_description")
        default_prompt = simple_yaml_value(metadata, "default_prompt")
        if not display_name:
            issues.append(f"{rel_from(openai_yaml, root)}: interface.display_name is required")
        if not short_description:
            issues.append(f"{rel_from(openai_yaml, root)}: interface.short_description is required")
        elif not 25 <= len(short_description) <= 64:
            issues.append(f"{rel_from(openai_yaml, root)}: interface.short_description must be 25-64 characters")
        if not default_prompt:
            issues.append(f"{rel_from(openai_yaml, root)}: interface.default_prompt is required")
        elif f"${skill_name}" not in default_prompt:
            issues.append(f"{rel_from(openai_yaml, root)}: default_prompt must mention ${skill_name}")

    return issues


def check_codex_skills(_args: argparse.Namespace | None = None) -> int:
    issues = codex_skill_issues()
    if issues:
        print("check-codex-skills FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1
    print("check-codex-skills: OK")
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
        "Endpoint returns object/anonymous JSON - use a named Application-layer DTO (REVIEW_FINDINGS.md)",
    ),
    (
        r"\bSkip\s*=",
        lambda p: p.startswith("tests/") and p.endswith(".cs"),
        "Skipped test introduced - fix or remove the test instead",
    ),
    (
        r"[.]EnsureCreated(?:Async)?[(]",
        lambda p: (p.startswith("src/") or p.startswith("tests/")) and p.endswith(".cs"),
        "EnsureCreated introduced - use the owning DbContext migration chain",
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
    (re.compile(r"^grpcurl\b"), "use `python scripts/axis.py grpc ...`"),
    (re.compile(r"^openssl\b"), "use `python scripts/axis.py local-dev certs`"),
    (re.compile(r"^python\s+docs/scripts/"), "use `python scripts/axis.py docs ...`"),
    (re.compile(r"^buf\s+config\s+ls-breaking-rules\b"), "use `python scripts/axis.py buf list-breaking-rules`"),
    (re.compile(r"^buf\s+--version\b"), "use `python scripts/axis.py check buf-cli`"),
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
    candidates = paths or git_ls_files()
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


STALE_REFERENCE_RULES = (
    (
        re.compile(r"feature file|see gaps below|^> \*\*Wireframe\*\*:|docs/epics/|_template-feature-us|\| Diagram \| Source \| Preview \|"),
        "Epic->Use-case migration - see docs/use-cases/README.md",
    ),
    (re.compile(r"\bCLAUDE[.]md\b"), "AGENTS.md is the agent contract; update repo review/agent guidance"),
)


def stale_reference_files(root: Path) -> list[Path]:
    files = list((root / "docs").rglob("*.md")) + list((root / ".github").rglob("*.md"))
    files.extend(root / name for name in ("AGENTS.md", "CONTRIBUTING.md", "README.md"))
    skills_root = root / ".agents" / "skills"
    if skills_root.is_dir():
        files.extend(skills_root.glob("*/SKILL.md"))
    return files


def stale_reference_issues(*, root: Path | None = None) -> list[str]:
    root = root or ROOT
    issues: list[str] = []
    for path in stale_reference_files(root):
        if not path.is_file():
            continue
        for idx, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            for pattern, message in STALE_REFERENCE_RULES:
                if pattern.search(line):
                    issues.append(f"Stale reference in {rel_from(path, root)}: {idx}:{line} ({message})")
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


def is_workaround_comment(path: Path, line: str) -> bool:
    stripped = line.lstrip()
    if path.suffix == ".py":
        return stripped.startswith("#") and "WORKAROUND:" in stripped
    if path.suffix in {".cs", ".ts", ".tsx"}:
        return ("// WORKAROUND:" in line) or ("/* WORKAROUND:" in line)
    return "WORKAROUND:" in line


def check_workarounds(issues: list[str]) -> None:
    workarounds = ROOT / "docs" / "WORKAROUNDS.md"
    if not workarounds.is_file():
        return
    known = set()
    for line in workarounds.read_text(encoding="utf-8").splitlines():
        if line.startswith("### "):
            slug = re.sub(r"[^A-Za-z0-9-]", "", line[4:]).lower()
            known.add(slug)
    roots = [ROOT / "src", ROOT / "tests", ROOT / "frontend" / "src", ROOT / "scripts"]
    for root in roots:
        for path in iter_files(root, (".cs", ".ts", ".tsx", ".md", ".py")):
            text = path.read_text(encoding="utf-8", errors="ignore")
            for idx, line in enumerate(text.splitlines(), 1):
                if not is_workaround_comment(path, line):
                    continue
                match = re.search(r"docs/WORKAROUNDS[.]md#([A-Za-z0-9-]+)", line)
                if not match:
                    fail(issues, f"WORKAROUND comment without docs/WORKAROUNDS.md reference - add link or rephrase: {rel(path)}:{idx}:{line}")
                    continue
                referenced = match.group(1).lower()
                if referenced not in known:
                    fail(
                        issues,
                        f"WORKAROUND comment references unknown slug '{referenced}' - add a section to docs/WORKAROUNDS.md (or fix the slug): {rel(path)}",
                    )


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

REVIEW_FINDINGS_LEDGER_HEADER = [
    "Finding class",
    "Rule owner",
    "Trigger / scope",
    "Mechanism",
    "Proof / gap",
    "Status",
]

REVIEW_FINDINGS_ALLOWED_STATUSES = {
    "Enforced",
    "Partial",
    "Review-only",
    "Guidance",
    "Not a rule",
}

ENFORCEMENT_TRUTH_REQUIRED_SNIPPETS = [
    (
        Path(".github/workflows/build-and-test.yml"),
        [
            ("pull_request:", "CI workflow runs for pull requests"),
            ("run: python scripts/axis.py check pr", "PR metadata guard runs in CI"),
            ("run: python scripts/axis.py check docker", "Docker endpoint is available for Testcontainers in CI through the Axis wrapper"),
            ("run: python scripts/axis.py check vulnerable-packages", "vulnerable package gate runs in CI"),
            ("run: python scripts/axis.py check test-naming", ".NET test naming gate runs in CI"),
            ("dotnet-version: 8.0.x", ".NET CI setup uses the documented SDK major"),
            ("run: python scripts/axis.py dotnet build -- --no-restore", ".NET build runs in CI through the Axis wrapper"),
            ("run: python scripts/axis.py dotnet format --check", ".NET format gate runs in CI through the Axis wrapper"),
            ("python scripts/axis.py dotnet test -- --no-build", "full .NET test suite runs in CI through the Axis wrapper"),
            ("node-version-file: frontend/.nvmrc", "frontend CI setup uses the documented Node source"),
            ("run: python scripts/axis.py frontend install", "frontend dependencies install through the Axis wrapper"),
            ("run: python scripts/axis.py frontend gen-api-types --check", "frontend API type generation runs in CI through the Axis wrapper"),
            ("run: python scripts/axis.py frontend ci", "frontend typecheck/lint runs in CI through the Axis wrapper"),
            ("run: python scripts/axis.py frontend test", "frontend tests run in CI through the Axis wrapper"),
            ('version: "1.50.0"', "protobuf CI setup pins the documented Buf CLI version"),
            ("run: python scripts/axis.py check buf-lint", "protobuf CI lint uses the version-checking local wrapper"),
            ("run: python scripts/axis.py check buf-breaking-against-base", "protobuf CI breaking check uses the version-checking local wrapper"),
            ("run: python scripts/axis.py check policy-tests", "policy gate tests run in CI"),
            ("run: python scripts/axis.py check doc-drift", "doc drift runs in CI"),
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
            ('step("buf lint", lambda: check_buf_lint())', "local verify runs Buf lint through the version-checking wrapper"),
            ('step("buf breaking", lambda: check_buf_breaking_against_base())', "local verify runs Buf breaking through the version-checking wrapper"),
            ('step("policy gate tests", lambda: check_policy_tests())', "local verify runs policy gate tests"),
            ('step("doc drift", lambda: check_doc_drift())', "local verify runs doc drift"),
            ('step("markdown links", lambda: check_markdown_links())', "local verify runs markdown link check"),
            ('def pre_push(args: argparse.Namespace) -> int:', "pre-push quick gate is implemented in Python"),
            ('return verify(args)', "pre-push can opt into full verify with AXIS_PRE_PUSH_FULL"),
            ("for issue in governance_owner_boundary_issues():", "doc drift checks governance owner boundaries"),
            ("for issue in review_findings_registry_issues():", "doc drift checks review findings registry rows"),
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
            ('fresh.Should().Contain("\\"workspaceName\\"");', "OpenAPI test asserts camelCase wire shape"),
            ('fresh.Should().NotContain("\\"workspace_name\\"");', "OpenAPI test rejects snake_case wire drift"),
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
                        "Link to agent-checklist.md#verification-gate--verify-before-pr-review instead."
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
                    "Move deterministic enforcement to scripts/tests and REVIEW_FINDINGS.md."
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


def review_findings_registry_issues(*, root: Path | None = None) -> list[str]:
    """Validate REVIEW_FINDINGS.md as the single rule registry."""
    root = root or ROOT
    path = root / "docs" / "REVIEW_FINDINGS.md"
    normalized = "docs/REVIEW_FINDINGS.md"

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
    if header != REVIEW_FINDINGS_LEDGER_HEADER:
        return [
            f"{normalized}:{header_idx + 1}: ledger header must be "
            f"`{' | '.join(REVIEW_FINDINGS_LEDGER_HEADER)}`"
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
        if len(cells) != len(REVIEW_FINDINGS_LEDGER_HEADER):
            issues.append(
                f"{normalized}:{idx + 1}: ledger row must have "
                f"{len(REVIEW_FINDINGS_LEDGER_HEADER)} cells"
            )
            continue

        row = dict(zip(REVIEW_FINDINGS_LEDGER_HEADER, cells))
        for field, value in row.items():
            if not value:
                issues.append(f"{normalized}:{idx + 1}: ledger `{field}` cell is empty")

        status = plain_markdown_cell(row["Status"])
        if status not in REVIEW_FINDINGS_ALLOWED_STATUSES:
            issues.append(
                f"{normalized}:{idx + 1}: unknown ledger status `{row['Status']}`; "
                f"use one of {sorted(REVIEW_FINDINGS_ALLOWED_STATUSES)}"
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
        elif status == "Guidance":
            if re.search(r"\b(must|gate|enforced)\b", combined):
                issues.append(f"{normalized}:{idx + 1}: Guidance row must not use rule/gate language")
        elif status == "Not a rule":
            if owner not in {"none", "n/a"} or mechanism not in {"none", "n/a"}:
                issues.append(f"{normalized}:{idx + 1}: Not-a-rule row must use `None` owner and mechanism")

    if row_count == 0:
        issues.append(f"{normalized}: ## Ledger must contain at least one rule row")

    return issues


def check_doc_drift(_args: argparse.Namespace | None = None) -> int:
    range_spec = diff_range()
    issues: list[str] = []

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

    if any(path.startswith("src/") for path in paths) and docs_changed_under(paths, "docs/PROGRESS.md"):
        if not any(path.startswith("docs/use-cases/") for path in paths):
            fail(issues, "docs/PROGRESS.md updated but no docs/use-cases/ change while src/ changed")

    all_added_lines = added_lines(range_spec, lambda _path: True)
    for issue in doc_drift_added_line_issues(all_added_lines):
        fail(issues, issue)

    for issue in documented_raw_command_issues():
        fail(issues, issue)

    for hit in endpoint_mediator_hits():
        fail(
            issues,
            "Endpoint handler calls the mediator more than once - move orchestration into a single "
            f"command/handler or saga (REVIEW_FINDINGS.md): {hit}",
        )

    for issue in missing_handler_test_issues(changed_name_status(range_spec)):
        fail(issues, issue)

    check_workarounds(issues)

    for issue in governance_owner_boundary_issues():
        fail(issues, issue)

    for issue in review_findings_registry_issues():
        fail(issues, issue)

    for issue in enforcement_truth_audit_issues():
        fail(issues, issue)

    spec_target = ROOT / "docs" / "ARCHITECTURE.md"
    if spec_target.is_file():
        spec_rx = re.compile(r"Not yet|\bplanned\b|Will be|To be implemented|Coming soon|in the future")
        for idx, line in enumerate(spec_target.read_text(encoding="utf-8").splitlines(), 1):
            if spec_rx.search(line):
                fail(issues, f"Speculation in reference doc - move to docs/PROGRESS.md or a use-case file: {rel(spec_target)}:{idx}:{line}")

    for issue in stale_reference_issues(root=ROOT):
        fail(issues, issue)

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
                    f"the use-case/PROGRESS/retro (docs-style.md): {rel(path)}:{idx}:{line}",
                )

    for migration in (ROOT / "src" / "Modules").glob("**/Migrations/*.cs"):
        if migration.name.endswith(".Designer.cs") or "Snapshot" in migration.name:
            continue
        if not migration.with_name(f"{migration.stem}.Designer.cs").is_file():
            fail(issues, f"EF migration missing .Designer.cs - regenerate with dotnet ef: {rel(migration)}")

    checkers = [
        ("check-text-encoding", check_text_encoding),
        ("check-scripts-standard", check_scripts_standard),
        ("check-codex-skills", check_codex_skills),
        ("check-ef-domain-mapping", check_ef_domain_mapping),
        ("check-frontend-api-contracts", check_frontend_api_contracts),
        ("check-frontend-style", check_frontend_style),
        ("check-frontend-component-composition", check_frontend_component_composition),
        ("check-frontend-quality", check_frontend_quality),
        ("check-use-case-docs.py", lambda _=None: run_module_check("check-use-case-docs.py", ["--check"])),
        ("check-doc-link-targets.py", lambda _=None: run_module_check("check-doc-link-targets.py", ["--check"])),
        ("check-doc-navigation", check_doc_navigation),
        ("check-doc-size-budgets", check_doc_size_budgets),
        ("check-doc-code-fences.py", lambda _=None: run_module_check("check-doc-code-fences.py", ["--check"])),
        ("check-local-dev-docs.py", lambda _=None: run_module_check("check-local-dev-docs.py", ["--check"])),
        ("sync_buf_yaml.py", lambda _=None: module_main("sync_buf_yaml.py", ["--check"])),
        ("check_kafka_wiring.py", lambda _=None: module_main("check_kafka_wiring.py", ["--check"])),
        ("regenerate-domain-readme-index.py", lambda _=None: run_module_check("regenerate-domain-readme-index.py", ["--check"])),
    ]
    for name, checker in checkers:
        if checker() != 0:
            issues.append(f"{name} failed")

    if any_changed(paths, r"^docker-compose[.]yml$") and not docs_changed_under(paths, "docs/playbooks/local-dev.md"):
        fail(issues, "docker-compose.yml changed but docs/playbooks/local-dev.md not updated in this PR")

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

    result = run_optional([command, *version_args], env=env)
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


def _nvm_node_bin_dirs(expected_major: str) -> list[Path]:
    roots: list[Path] = []
    nvm_dir = os.environ.get("NVM_DIR")
    if nvm_dir:
        roots.append(Path(nvm_dir) / "versions" / "node")
    roots.append(Path.home() / ".nvm" / "versions" / "node")

    seen_roots: set[Path] = set()
    candidates: list[Path] = []
    for root in roots:
        if root in seen_roots or not root.is_dir():
            continue
        seen_roots.add(root)
        for version_dir in root.iterdir():
            if not version_dir.is_dir() or version_major(version_dir.name) != expected_major:
                continue
            bin_dir = version_dir / "bin"
            if (bin_dir / "node").is_file() and (bin_dir / "npm").is_file():
                candidates.append(bin_dir)

    return sorted(candidates, key=lambda path: _version_sort_key(path.parent.name), reverse=True)


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


def find_buf() -> str | None:
    return shutil.which(exe("buf")) or shutil.which("buf")


def buf_version_status(buf: str) -> tuple[bool, str]:
    result = run_optional([buf, "--version"])
    if result is None:
        return False, f"{buf} is not executable"
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        return False, detail or f"{buf} --version exited with {result.returncode}"

    first_line = (result.stdout or result.stderr or "").strip().splitlines()
    version_line = first_line[0] if first_line else ""
    if version_line != REQUIRED_BUF_VERSION:
        return (
            False,
            f"found `{version_line or 'unknown'}` at {buf}; expected `{REQUIRED_BUF_VERSION}`",
        )
    return True, f"{version_line} ({buf})"


def check_buf_cli(_args: argparse.Namespace | None = None) -> int:
    buf = find_buf()
    if buf is None:
        print(
            f"buf-cli: FAIL - Buf CLI {REQUIRED_BUF_VERSION} is required, "
            f"but `buf` was not found in PATH. See {TOOL_VERSIONS_DOC}.",
            file=sys.stderr,
        )
        return 1

    ok, detail = buf_version_status(buf)
    if not ok:
        print(
            f"buf-cli: FAIL - Buf CLI {REQUIRED_BUF_VERSION} is required; {detail}. "
            f"Install the documented version or put it earlier in PATH. See {TOOL_VERSIONS_DOC}.",
            file=sys.stderr,
        )
        return 1

    print(f"buf-cli: OK ({detail})")
    return 0


def check_buf_lint(_args: argparse.Namespace | None = None) -> int:
    rc = check_buf_cli()
    if rc != 0:
        return rc
    buf = find_buf()
    if buf is None:
        return 1
    return run([buf, "lint"], check=False).returncode


def find_lychee() -> str | None:
    return shutil.which("lychee")


def lychee_version_status(lychee: str) -> tuple[bool, str]:
    result = run_optional([lychee, "--version"])
    if result is None:
        return False, f"{lychee} is not executable"
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        return False, detail or f"{lychee} --version exited with {result.returncode}"

    first_line = (result.stdout or result.stderr or "").strip().splitlines()
    version_line = first_line[0] if first_line else ""
    expected = f"lychee {REQUIRED_LYCHEE_VERSION}"
    if version_line != expected:
        return (
            False,
            f"found `{version_line or 'unknown'}` at {lychee}; expected `{expected}`",
        )
    return True, f"{version_line} ({lychee})"


def run_lychee_markdown_check(lychee: str) -> subprocess.CompletedProcess[str]:
    return run([lychee, "--config", "./lychee.toml", "./**/*.md"], capture=True, check=False)


def emit_captured_process(result: subprocess.CompletedProcess[str]) -> None:
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr)


def check_markdown_links(_args: argparse.Namespace | None = None) -> int:
    lychee = find_lychee()
    if lychee is None:
        print(
            f"check-markdown-links: Lychee {REQUIRED_LYCHEE_VERSION} is required, "
            "but `lychee` was not found in PATH. See docs/playbooks/scripts.md#tool-versions.",
            file=sys.stderr,
        )
        return 1
    version_ok, version_detail = lychee_version_status(lychee)
    if not version_ok:
        print(
            f"check-markdown-links: Lychee {REQUIRED_LYCHEE_VERSION} is required; {version_detail}. "
            "Install the documented version or put it earlier in PATH. "
            "See docs/playbooks/scripts.md#tool-versions.",
            file=sys.stderr,
        )
        return 1
    result = run_lychee_markdown_check(lychee)
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
        return run([exe("dotnet"), "test", "Axis.sln", "--nologo", *dotnet_args], check=False).returncode
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


def run_frontend_npm(npm_args: list[str], *, cwd: Path = FRONTEND_DIR) -> subprocess.CompletedProcess[str]:
    env = frontend_toolchain_env()
    return run([resolve_exe("npm", env=env), *npm_args], cwd=cwd, env=env, check=False)


def frontend_command(args: argparse.Namespace) -> int:
    rc = check_frontend_toolchain()
    if rc != 0:
        return rc

    command = args.frontend_command
    if command == "install":
        return run_frontend_npm(["ci"]).returncode
    if command == "ci":
        return run_frontend_npm(["run", "ci"]).returncode
    if command == "test":
        return run_frontend_npm(["run", "test"]).returncode
    if command == "gen-api-types":
        result = run_frontend_npm(["run", "gen:api-types"])
        if result.returncode != 0 or not args.check:
            return result.returncode
        diff = run([exe("git"), "diff", "--exit-code", "--", "frontend/src/lib/api-types.ts"], check=False)
        if diff.returncode != 0:
            print(
                "frontend gen-api-types: frontend/src/lib/api-types.ts is stale - "
                "run `python scripts/axis.py frontend gen-api-types` and commit the result",
                file=sys.stderr,
            )
        return diff.returncode
    if command == "gen-design-tokens":
        generated_paths = [
            "frontend/src/design-system/tokens.css",
            "frontend/src/design-system/tokens.ts",
            "frontend/src/design-system/tailwind-tokens.js",
        ]
        before = run(
            [exe("git"), "diff", "--", *generated_paths],
            capture=True,
            check=False,
        ).stdout
        result = run_frontend_npm(["run", "gen:design-tokens"])
        if result.returncode != 0 or not args.check:
            return result.returncode
        after = run(
            [exe("git"), "diff", "--", *generated_paths],
            capture=True,
            check=False,
        ).stdout
        if before != after:
            print(
                "frontend gen-design-tokens: generated design-token files are stale - "
                "run `python scripts/axis.py frontend gen-design-tokens` and commit the result",
                file=sys.stderr,
            )
            return 1
        return 0
    if command == "script":
        npm_args = ["run", args.script_name]
        script_args = list(args.script_args)
        if script_args and script_args[0] == "--":
            script_args = script_args[1:]
        if script_args:
            npm_args.append("--")
            npm_args.extend(script_args)
        return run_frontend_npm(npm_args).returncode
    raise CheckError(f"Unknown frontend command: {command}")


def docs_command(args: argparse.Namespace) -> int:
    command = args.docs_command
    if command == "sync-mermaid-theme":
        return run([sys.executable, str(ROOT / "docs" / "scripts" / "sync-mermaid-theme.py")], check=False).returncode
    if command == "mermaid-init":
        from docs.diagrams.mermaid_theme import MERMAID_INIT

        print(MERMAID_INIT)
        return 0
    raise CheckError(f"Unknown docs command: {command}")


def buf_command(args: argparse.Namespace) -> int:
    rc = check_buf_cli()
    if rc != 0:
        return rc
    if args.buf_command == "list-breaking-rules":
        return run([find_buf() or exe("buf"), "config", "ls-breaking-rules", "--version=v2"], check=False).returncode
    raise CheckError(f"Unknown buf command: {args.buf_command}")


def check_docker(_args: argparse.Namespace | None = None) -> int:
    if _docker_info_ok():
        print("check-docker: OK (docker info works)")
        return 0
    print("check-docker: FAIL - docker info failed; no reachable Docker endpoint detected", file=sys.stderr)
    return 1


def verify(args: argparse.Namespace) -> int:
    range_spec = diff_range()
    paths = changed_paths(range_spec)

    dotnet = False
    frontend = False
    protobuf = False
    markdown_links = False
    if not paths:
        dotnet = True
        frontend = True
        protobuf = True
        markdown_links = True
    else:
        dotnet = any(
            re.search(r"^(src/|tests/|Directory[.]|Axis[.]sln$|global[.]json$|[.]editorconfig$|openapi[.]json$|[.]github/workflows/build-and-test[.]yml$)", p)
            for p in paths
        )
        frontend = any(
            re.search(r"^(frontend/|[.]editorconfig$|openapi[.]json$|[.]github/workflows/build-and-test[.]yml$)", p)
            for p in paths
        )
        protobuf = any(
            re.search(r"(^|/)[^/]+[.]proto$|^buf[.]yaml$|^[.]github/workflows/build-and-test[.]yml$", p)
            for p in paths
        )
        markdown_links = any(
            re.search(r"(^|/)[^/]+[.]md$|^lychee[.]toml$|^[.]github/workflows/build-and-test[.]yml$", p)
            for p in paths
        )

    api_surface_drift = any_changed(paths, r"^src/Axis[.]Api/Endpoints/") and not any_changed(paths, r"^openapi[.]json$")
    failed: list[str] = []

    def step(name: str, fn: callable[[], int]) -> int:
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
        return rc

    print(f"verify - .NET={dotnet} frontend={frontend} protobuf={protobuf} markdown-links={markdown_links}")

    if dotnet:
        if step(".NET SDK", lambda: check_dotnet_sdk()) == 0:
            step(".NET test naming", lambda: check_test_naming())
            step(".NET build", lambda: dotnet_command(argparse.Namespace(dotnet_command="build", dotnet_args=[])))
            step(".NET vulnerable packages", lambda: check_vulnerable_packages())
            step(".NET format", lambda: dotnet_command(argparse.Namespace(dotnet_command="format", check=True, dotnet_args=[])))
            step(".NET test (unit projects)", lambda: test_unit(argparse.Namespace(dotnet_args=[])))

    if frontend:
        if step("frontend toolchain", lambda: check_frontend_toolchain()) == 0:
            step("frontend ci (tsc + biome)", lambda: frontend_command(argparse.Namespace(frontend_command="ci")))
            step("frontend test", lambda: frontend_command(argparse.Namespace(frontend_command="test")))

    if protobuf:
        if step("buf lint", lambda: check_buf_lint()) == 0:
            step("buf breaking", lambda: check_buf_breaking_against_base())

    if markdown_links:
        step("markdown links", lambda: check_markdown_links())

    step("policy gate tests", lambda: check_policy_tests())
    step("doc drift", lambda: check_doc_drift())

    if api_surface_drift:
        print()
        print("WARN API surface changed (src/Axis.Api/Endpoints/) but openapi.json is unchanged.")
        print("  If you added or changed a route / request / response shape, regenerate the contract:")
        print("    python scripts/axis.py generate api-contracts")
        print("  then commit openapi.json + api-types.ts; CI's OpenApiDocumentTests fails otherwise.")

    print()
    if not failed:
        print("verify: PASS")
        return 0
    print(f"verify: FAIL - {len(failed)} step(s): {' '.join(failed)}", file=sys.stderr)
    return 1


def pre_push(args: argparse.Namespace) -> int:
    full = os.environ.get("AXIS_PRE_PUSH_FULL", "").lower() in {"1", "true", "yes", "on"}
    if full:
        print("pre-push: AXIS_PRE_PUSH_FULL is set; running full verify.")
        return verify(args)

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
    skills = not paths or any(p.startswith(".agents/skills/") for p in paths)
    scripts_changed = not paths or any(p.startswith("scripts/") for p in paths)
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
    print("  Run `python scripts/axis.py verify` before marking a PR ready for review.")

    if dotnet:
        step(".NET test naming", lambda: check_test_naming())
        step(".NET test project classification", lambda: check_test_project_classification())

    if docs:
        step("doc navigation", lambda: check_doc_navigation())
        step("doc size budgets", lambda: check_doc_size_budgets())

    if skills:
        step("Codex skills", lambda: check_codex_skills())

    if scripts_changed:
        step("scripts standard", lambda: check_scripts_standard())

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


def generate_wireframes(args: argparse.Namespace) -> int:
    rc = check_frontend_toolchain()
    if rc != 0:
        return rc
    npm_args = ["run", "export:wireframes", "--"]
    if args.filter:
        npm_args.extend(["--filter", args.filter])
    if args.changed:
        npm_args.append("--changed")
    result = run_frontend_npm(npm_args)
    return result.returncode


def register_avro_schemas(args: argparse.Namespace) -> int:
    registry_url = args.schema_registry_url or os.environ.get("SCHEMA_REGISTRY_URL", "http://localhost:8081")
    dry_run = args.dry_run or bool(os.environ.get("DRY_RUN"))
    count = 0
    for file in sorted((ROOT / "src" / "Modules").glob("**/Schemas/*Event.avsc")):
        match = re.search(r"src/Modules/([^/\\]+)/", str(file).replace("\\", "/"))
        if not match:
            continue
        module = match.group(1).lower()
        event_name = file.stem.removesuffix("Event")
        topic = f"axis.{module}.{axis_repo.pascal_to_kebab(event_name)}"
        subject = f"{topic}-value"
        if dry_run:
            print(f"would register {subject}  <-  {rel(file)}")
        else:
            schema = file.read_text(encoding="utf-8")
            payload = json.dumps({"schema": schema})
            result = run(
                [
                    "curl",
                    "-fsS",
                    "-X",
                    "POST",
                    "-H",
                    "Content-Type: application/vnd.schemaregistry.v1+json",
                    "--data",
                    payload,
                    f"{registry_url}/subjects/{subject}/versions",
                ],
                check=False,
            )
            if result.returncode != 0:
                return result.returncode
            print(f"registered {subject}")
        count += 1
    print(f"register-avro-schemas: OK ({count} schemas)")
    return 0


def install_hooks(_args: argparse.Namespace | None = None) -> int:
    current_hooks = run([exe("git"), "config", "--get", "core.hooksPath"], capture=True, check=False)
    if current_hooks.returncode not in (0, 1, 5):
        return current_hooks.returncode

    hooks_value = current_hooks.stdout.strip() if current_hooks.returncode == 0 else ""
    if hooks_value:
        legacy_hooks_path = (ROOT / "scripts" / "hooks").resolve()
        hooks_path = Path(hooks_value)
        resolved_hooks_path = hooks_path if hooks_path.is_absolute() else (ROOT / hooks_path)
        normalized_hooks_value = hooks_value.replace("\\", "/").rstrip("/")
        is_legacy_hooks_path = (
            normalized_hooks_value in {"scripts/hooks", "./scripts/hooks"}
            or resolved_hooks_path.resolve() == legacy_hooks_path
        )
        if not is_legacy_hooks_path:
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
    result = run_optional([exe("docker"), "info"], env=env)
    return result is not None and result.returncode == 0


def _docker_compose_ok() -> bool:
    if shutil.which(exe("docker")) is None and shutil.which("docker") is None:
        return False
    result = run_optional([exe("docker"), "compose", "version"])
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


def local_dev_compose_args(*args: str) -> list[str]:
    return compose_args(LOCAL_DEV_PROJECT_NAME, LOCAL_DEV_COMPOSE_FILE, *args)


def require_docker_compose(label: str) -> int:
    if _docker_compose_ok():
        return 0
    print(f"{label}: Docker Compose v2 is not available in this shell", file=sys.stderr)
    return 1


def run_required(command: list[str]) -> int:
    result = run(command, check=False)
    return result.returncode


def restrict_local_cert_permissions() -> None:
    LOCAL_CERT_DIR.chmod(0o700)
    LOCAL_ROOT_CA_KEY.chmod(0o600)
    LOCALHOST_KEY.chmod(0o600)


def local_dev_certs(_args: argparse.Namespace | None = None) -> int:
    if shutil.which(exe("openssl")) is None and shutil.which("openssl") is None:
        print("local-dev certs: OpenSSL is not available in PATH", file=sys.stderr)
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
        [exe("openssl"), "genrsa", "-out", str(LOCAL_ROOT_CA_KEY), "4096"],
        [
            exe("openssl"),
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
        [exe("openssl"), "x509", "-outform", "der", "-in", str(LOCAL_ROOT_CA_PEM), "-out", str(LOCAL_ROOT_CA_CER)],
        [exe("openssl"), "genrsa", "-out", str(LOCALHOST_KEY), "2048"],
        [
            exe("openssl"),
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
            exe("openssl"),
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
        shell_command = args.exec_command or ["bash"]
        return run(local_dev_compose_args("exec", args.service, *shell_command), check=False).returncode

    if command == "psql":
        return run(
            local_dev_compose_args("exec", "postgres", "psql", "-U", "axis", "-d", args.database),
            check=False,
        ).returncode

    if command == "e2e":
        build = run(local_dev_compose_args("--profile", "e2e", "build", "e2e"), check=False)
        if build.returncode != 0:
            return build.returncode
        return run(local_dev_compose_args("--profile", "e2e", "run", "--rm", "--no-deps", "e2e"), check=False).returncode

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


def grpc_command(args: argparse.Namespace) -> int:
    if shutil.which(exe("grpcurl")) is None and shutil.which("grpcurl") is None:
        print("grpc: grpcurl is not available in PATH", file=sys.stderr)
        return 1

    command = args.grpc_command
    if command == "list":
        return run([exe("grpcurl"), "-cacert", args.cacert, args.target, "list"], check=False).returncode
    if command == "call":
        grpc_args = [exe("grpcurl"), "-cacert", args.cacert]
        if args.authorization:
            grpc_args.extend(["-H", f"authorization: {args.authorization}"])
        grpc_args.extend(["-d", args.data, args.target, args.method])
        return run(grpc_args, check=False).returncode
    raise CheckError(f"Unknown gRPC command: {command}")


def _wsl_docker_ok() -> bool:
    if os.name != "nt" or shutil.which("wsl.exe") is None:
        return False
    result = run_optional(["wsl.exe", "bash", "-lc", "docker info >/dev/null 2>&1"])
    return result is not None and result.returncode == 0


def doctor(args: argparse.Namespace) -> int:
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
        record("WARN", "python launcher", "not found in PATH; open a shell where Python 3 resolves before running repo scripts")

    yaml_status, yaml_detail = _python_module_version("yaml", "PyYAML")
    record(yaml_status, "python package PyYAML", f"{yaml_detail}; required for skill-creator quick_validate.py")

    lychee = find_lychee()
    if lychee is None:
        record(
            "FAIL",
            "lychee",
            f"Lychee {REQUIRED_LYCHEE_VERSION} is required for markdown link checks; "
            "install it on PATH per docs/playbooks/scripts.md#tool-versions",
        )
    else:
        lychee_ok, lychee_detail = lychee_version_status(lychee)
        record("OK" if lychee_ok else "FAIL", "lychee", lychee_detail)

    buf = find_buf()
    if buf is None:
        record(
            "FAIL",
            "buf",
            f"Buf CLI {REQUIRED_BUF_VERSION} is required for protobuf checks; "
            f"install it on PATH per {TOOL_VERSIONS_DOC}",
        )
    else:
        buf_ok, buf_detail = buf_version_status(buf)
        record("OK" if buf_ok else "FAIL", "buf", buf_detail)

    coderabbit_ok, coderabbit_detail = coderabbit_cli_status()
    record("OK" if coderabbit_ok else "FAIL", "coderabbit", coderabbit_detail)

    dotnet_ok, dotnet_detail = dotnet_sdk_status()
    record("OK" if dotnet_ok else "FAIL", ".NET SDK", dotnet_detail)

    frontend_env = frontend_toolchain_env()
    node_ok, node_detail = node_version_status(frontend_env)
    record("OK" if node_ok else "FAIL", "node", node_detail)

    for name, version_args in (
        ("git", ("--version",)),
        ("npm", ("--version",)),
    ):
        env = frontend_env if name == "npm" else None
        status, detail = _command_version(name, *version_args, env=env)
        record(status, name, detail)

    docker_status, docker_detail = _command_version("docker", "--version")
    if docker_status == "FAIL":
        record(
            "WARN",
            "docker cli",
            f"{docker_detail}; use an environment adapter if Docker is available from another execution context",
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
            "an exported Docker endpoint responds; set DOCKER_HOST to that endpoint for the current process/session",
        )
    elif wsl_docker:
        record(
            "WARN",
            "docker endpoint",
            "Docker works from another detected execution context; run the canonical repo-root command there or expose a local Docker endpoint",
        )
    else:
        record("FAIL", "docker endpoint", "docker info failed; no reachable Docker endpoint detected")

    if _docker_compose_ok():
        record("OK", "docker compose", "docker compose version works")
    elif wsl_docker:
        record(
            "WARN",
            "docker compose",
            "Docker Compose is available from another detected execution context; run the canonical repo-root command there",
        )
    else:
        record("FAIL", "docker compose", "Docker Compose v2 not available from this execution context")

    if os.name == "nt":
        npm_cmd = shutil.which("npm.cmd")
        npm_ps1 = shutil.which("npm.ps1")
        if npm_cmd:
            detail = f"native npm shim available ({npm_cmd})"
            if npm_ps1:
                detail += f"; alternate npm launcher also detected ({npm_ps1})"
            record("OK", "npm adapter", detail)

    print("axis doctor")
    for status, label, detail in rows:
        print(f"[{status:<4}] {label}: {detail}")

    failures = [label for status, label, _detail in rows if status == "FAIL"]
    if failures and args.strict:
        print(f"doctor: FAIL - {len(failures)} blocking issue(s): {', '.join(failures)}", file=sys.stderr)
        return 1

    if failures:
        print("doctor: completed with blocking findings; rerun with --strict to fail on them")
    else:
        print("doctor: OK")
    return 0


def bootstrap(_args: argparse.Namespace | None = None) -> int:
    frontend_env = frontend_toolchain_env()
    missing = [
        name
        for name, env in (
            ("git", None),
            ("dotnet", None),
            ("node", frontend_env),
            ("npm", frontend_env),
        )
        if not command_exists(name, env=env)
    ]
    if missing:
        for name in missing:
            print(f"bootstrap: missing '{name}' in PATH", file=sys.stderr)
        return 1
    rc = install_hooks()
    if rc != 0:
        return rc
    print("bootstrap: OK")
    return 0


def check_pr(args: argparse.Namespace) -> int:
    module_args: list[str] = []
    if args.title is not None:
        module_args.extend(["--title", args.title])
    if args.body_file is not None:
        module_args.extend(["--body-file", str(args.body_file)])
    return module_main("check-pr.py", module_args)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("bootstrap").set_defaults(func=bootstrap)
    doctor_parser = sub.add_parser("doctor")
    doctor_parser.add_argument("--strict", action="store_true", help="Exit non-zero when required local-dev tools are unavailable")
    doctor_parser.set_defaults(func=doctor)
    sub.add_parser("install-hooks").set_defaults(func=install_hooks)
    sub.add_parser("pre-push").set_defaults(func=pre_push)
    sub.add_parser("verify").set_defaults(func=verify)

    dotnet_parser = sub.add_parser("dotnet")
    dotnet_sub = dotnet_parser.add_subparsers(dest="dotnet_command", required=True)
    for dotnet_passthrough in ("restore", "build", "test"):
        parser_for_dotnet = dotnet_sub.add_parser(dotnet_passthrough)
        parser_for_dotnet.add_argument("dotnet_args", nargs=argparse.REMAINDER)
        parser_for_dotnet.set_defaults(func=dotnet_command)
    dotnet_format = dotnet_sub.add_parser("format")
    dotnet_format.add_argument("--check", action="store_true", help="Fail when dotnet format would change files")
    dotnet_format.add_argument("dotnet_args", nargs=argparse.REMAINDER)
    dotnet_format.set_defaults(func=dotnet_command)
    dotnet_run_api = dotnet_sub.add_parser("run-api")
    dotnet_run_api.add_argument("dotnet_args", nargs=argparse.REMAINDER)
    dotnet_run_api.set_defaults(func=dotnet_command)
    dotnet_ef = dotnet_sub.add_parser("ef")
    dotnet_ef.add_argument("dotnet_args", nargs=argparse.REMAINDER)
    dotnet_ef.set_defaults(func=dotnet_command)

    frontend_parser = sub.add_parser("frontend")
    frontend_sub = frontend_parser.add_subparsers(dest="frontend_command", required=True)
    frontend_sub.add_parser("install").set_defaults(func=frontend_command)
    frontend_sub.add_parser("ci").set_defaults(func=frontend_command)
    frontend_sub.add_parser("test").set_defaults(func=frontend_command)
    frontend_gen_api = frontend_sub.add_parser("gen-api-types")
    frontend_gen_api.add_argument("--check", action="store_true", help="Fail if generated frontend API types are stale")
    frontend_gen_api.set_defaults(func=frontend_command)
    frontend_gen_tokens = frontend_sub.add_parser("gen-design-tokens")
    frontend_gen_tokens.add_argument(
        "--check",
        action="store_true",
        help="Fail if generated design-token files are stale",
    )
    frontend_gen_tokens.set_defaults(func=frontend_command)
    frontend_script = frontend_sub.add_parser("script")
    frontend_script.add_argument("script_name")
    frontend_script.add_argument("script_args", nargs=argparse.REMAINDER)
    frontend_script.set_defaults(func=frontend_command)

    docs_parser = sub.add_parser("docs")
    docs_sub = docs_parser.add_subparsers(dest="docs_command", required=True)
    docs_sub.add_parser("sync-mermaid-theme").set_defaults(func=docs_command)
    docs_sub.add_parser("mermaid-init").set_defaults(func=docs_command)

    buf_parser = sub.add_parser("buf")
    buf_sub = buf_parser.add_subparsers(dest="buf_command", required=True)
    buf_sub.add_parser("list-breaking-rules").set_defaults(func=buf_command)

    grpc_parser = sub.add_parser("grpc")
    grpc_sub = grpc_parser.add_subparsers(dest="grpc_command", required=True)
    grpc_list = grpc_sub.add_parser("list")
    grpc_list.add_argument("--cacert", default=".dev-certs/rootCA.pem")
    grpc_list.add_argument("--target", default="localhost:5281")
    grpc_list.set_defaults(func=grpc_command)
    grpc_call = grpc_sub.add_parser("call")
    grpc_call.add_argument("method")
    grpc_call.add_argument("--target", default="localhost:5281")
    grpc_call.add_argument("--cacert", default=".dev-certs/rootCA.pem")
    grpc_call.add_argument("--authorization", default="")
    grpc_call.add_argument("--data", required=True)
    grpc_call.set_defaults(func=grpc_command)

    local_dev_parser = sub.add_parser("local-dev")
    local_dev_sub = local_dev_parser.add_subparsers(dest="local_dev_command", required=True)
    local_dev_sub.add_parser("certs").set_defaults(func=local_dev)
    local_up = local_dev_sub.add_parser("up")
    local_up.add_argument("--build", action="store_true")
    local_up.add_argument("services", nargs="*")
    local_up.set_defaults(func=local_dev)
    local_down = local_dev_sub.add_parser("down")
    local_down.add_argument("--volumes", action="store_true")
    local_down.set_defaults(func=local_dev)
    for local_command in ("start", "stop", "restart"):
        parser_for_command = local_dev_sub.add_parser(local_command)
        parser_for_command.add_argument("services", nargs="*")
        parser_for_command.set_defaults(func=local_dev)
    local_recreate = local_dev_sub.add_parser("recreate")
    local_recreate.add_argument("services", nargs="+")
    local_recreate.set_defaults(func=local_dev)
    local_dev_sub.add_parser("status").set_defaults(func=local_dev)
    local_logs = local_dev_sub.add_parser("logs")
    local_logs.add_argument("-f", "--follow", action="store_true")
    local_logs.add_argument("services", nargs="*")
    local_logs.set_defaults(func=local_dev)
    local_shell = local_dev_sub.add_parser("shell")
    local_shell.add_argument("service", nargs="?", default="api")
    local_shell.add_argument("exec_command", nargs=argparse.REMAINDER)
    local_shell.set_defaults(func=local_dev)
    local_psql = local_dev_sub.add_parser("psql")
    local_psql.add_argument("--database", default="axis")
    local_psql.set_defaults(func=local_dev)
    local_dev_sub.add_parser("e2e").set_defaults(func=local_dev)
    local_observability = local_dev_sub.add_parser("observability")
    local_observability_sub = local_observability.add_subparsers(dest="observability_command", required=True)
    local_observability_sub.add_parser("up").set_defaults(func=local_dev)
    local_observability_sub.add_parser("stop").set_defaults(func=local_dev)
    local_observability_sub.add_parser("status").set_defaults(func=local_dev)
    local_observability_logs = local_observability_sub.add_parser("logs")
    local_observability_logs.add_argument("-f", "--follow", action="store_true")
    local_observability_logs.set_defaults(func=local_dev)
    local_dev_sub.add_parser("reset-db").set_defaults(func=local_dev)
    local_dev_sub.add_parser("reset-all").set_defaults(func=local_dev)

    check = sub.add_parser("check")
    check_sub = check.add_subparsers(dest="check_command", required=True)
    check_sub.add_parser("doc-drift").set_defaults(func=check_doc_drift)
    check_sub.add_parser("policy-tests").set_defaults(func=check_policy_tests)
    check_sub.add_parser("text-encoding").set_defaults(func=check_text_encoding)
    check_sub.add_parser("scripts-standard").set_defaults(func=check_scripts_standard)
    check_sub.add_parser("codex-skills").set_defaults(func=check_codex_skills)
    check_sub.add_parser("test-naming").set_defaults(func=check_test_naming)
    check_sub.add_parser("test-project-classification").set_defaults(func=check_test_project_classification)
    check_sub.add_parser("docker").set_defaults(func=check_docker)
    check_sub.add_parser("dotnet-sdk").set_defaults(func=check_dotnet_sdk)
    check_sub.add_parser("frontend-toolchain").set_defaults(func=check_frontend_toolchain)
    check_sub.add_parser("vulnerable-packages").set_defaults(func=check_vulnerable_packages)
    check_sub.add_parser("ef-domain-mapping").set_defaults(func=check_ef_domain_mapping)
    check_sub.add_parser("frontend-api-contracts").set_defaults(func=check_frontend_api_contracts)
    check_sub.add_parser("frontend-style").set_defaults(func=check_frontend_style)
    check_sub.add_parser("frontend-component-composition").set_defaults(func=check_frontend_component_composition)
    check_sub.add_parser("frontend-quality").set_defaults(func=check_frontend_quality)
    check_sub.add_parser("coderabbit-cli").set_defaults(func=check_coderabbit_cli)
    check_sub.add_parser("buf-cli").set_defaults(func=check_buf_cli)
    check_sub.add_parser("buf-lint").set_defaults(func=check_buf_lint)
    check_sub.add_parser("buf-modules").set_defaults(func=check_buf_modules)
    check_sub.add_parser("buf-breaking-against-base").set_defaults(func=check_buf_breaking_against_base)
    check_sub.add_parser("local-dev-docs").set_defaults(
        func=lambda _args: run_module_check("check-local-dev-docs.py", ["--check"])
    )
    check_sub.add_parser("doc-link-targets").set_defaults(
        func=lambda _args: run_module_check("check-doc-link-targets.py", ["--check"])
    )
    check_sub.add_parser("markdown-links").set_defaults(func=check_markdown_links)
    check_sub.add_parser("doc-navigation").set_defaults(func=check_doc_navigation)
    check_sub.add_parser("doc-size-budgets").set_defaults(func=check_doc_size_budgets)
    check_sub.add_parser("doc-code-fences").set_defaults(
        func=lambda _args: run_module_check("check-doc-code-fences.py", ["--check"])
    )
    check_sub.add_parser("use-case-docs").set_defaults(
        func=lambda _args: run_module_check("check-use-case-docs.py", ["--check"])
    )
    check_sub.add_parser("kafka-wiring").set_defaults(
        func=lambda _args: module_main("check_kafka_wiring.py", ["--check"])
    )
    check_sub.add_parser("domain-readme-index").set_defaults(
        func=lambda _args: run_module_check("regenerate-domain-readme-index.py", ["--check"])
    )
    pr_parser = check_sub.add_parser("pr")
    pr_parser.add_argument("--title")
    pr_parser.add_argument("--body-file", type=Path)
    pr_parser.set_defaults(func=check_pr)

    test = sub.add_parser("test")
    test_sub = test.add_subparsers(dest="test_command", required=True)
    unit = test_sub.add_parser("unit")
    unit.add_argument("dotnet_args", nargs=argparse.REMAINDER)
    unit.set_defaults(func=test_unit)

    generate = sub.add_parser("generate")
    generate_sub = generate.add_subparsers(dest="generate_command", required=True)
    generate_sub.add_parser("api-contracts").set_defaults(func=generate_api_contracts)
    wireframes = generate_sub.add_parser("wireframes")
    wireframes.add_argument("-f", "--filter", default="", help="Render only paths containing this text.")
    wireframes.add_argument("--changed", action="store_true", help="Render changed wireframes and linked wireframes.")
    wireframes.set_defaults(func=generate_wireframes)
    write_buf = generate_sub.add_parser("buf-yaml")
    write_buf.set_defaults(func=lambda _args: module_main("sync_buf_yaml.py", ["--write"]))
    write_domain = generate_sub.add_parser("domain-readme-index")
    write_domain.set_defaults(func=lambda _args: module_main("regenerate-domain-readme-index.py", []))

    register = sub.add_parser("register")
    register_sub = register.add_subparsers(dest="register_command", required=True)
    avro = register_sub.add_parser("avro-schemas")
    avro.add_argument("--schema-registry-url")
    avro.add_argument("--dry-run", action="store_true")
    avro.set_defaults(func=register_avro_schemas)

    args = parser.parse_args(argv)
    try:
        return int(args.func(args))
    except CheckError as exc:
        print(exc, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
