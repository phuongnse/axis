#!/usr/bin/env python3
"""Repository maintenance CLI.

Python is the source of truth for scripts in this repository. Add maintenance
workflows as subcommands here instead of adding new shell or PowerShell scripts.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
from contextlib import contextmanager
from pathlib import Path
from typing import Iterable, TextIO

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = ROOT / "scripts"

if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis_repo  # noqa: E402
import doc_drift_domains  # noqa: E402
import sync_buf_yaml  # noqa: E402


class CheckError(RuntimeError):
    """Raised when a command fails."""


def rel(path: Path) -> str:
    return str(path.relative_to(ROOT)).replace("\\", "/")


def exe(name: str) -> str:
    if os.name == "nt" and not name.endswith(".exe"):
        cmd = shutil.which(f"{name}.cmd")
        if cmd:
            return cmd
    found = shutil.which(name)
    return found or name


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


def changed_paths(range_spec: str | None = None) -> list[str]:
    range_spec = range_spec or diff_range()
    result = run([exe("git"), "diff", "--name-only", range_spec], capture=True, check=False)
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        raise CheckError(f"changed_paths: git diff failed for {range_spec}: {detail}")
    return [line.strip().replace("\\", "/") for line in result.stdout.splitlines() if line.strip()]


def changed_name_status(range_spec: str | None = None) -> list[list[str]]:
    range_spec = range_spec or diff_range()
    result = run([exe("git"), "diff", "--name-status", range_spec], capture=True, check=False)
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        raise CheckError(f"changed_name_status: git diff failed for {range_spec}: {detail}")
    return [line.split("\t") for line in result.stdout.splitlines() if line.strip()]


def module_main(script_name: str, args: list[str], *, stdin_text: str | None = None) -> int:
    path = SCRIPTS / script_name
    module_name = f"_axis_{re.sub(r'[^A-Za-z0-9_]', '_', script_name)}"
    spec = importlib.util.spec_from_file_location(module_name, path)
    if spec is None or spec.loader is None:
        raise CheckError(f"Cannot load {script_name}")
    module = importlib.util.module_from_spec(spec)

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
    ".coderabbit.yaml",
    ".editorconfig",
    ".gitattributes",
    ".gitignore",
    "Dockerfile",
    "Makefile",
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
    result = run(
        [exe("dotnet"), "list", "Axis.sln", "package", "--vulnerable", "--include-transitive"],
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
        print("\nSee docs/playbooks/patterns.md#ef-core-aggregate-mapping-patterns", file=sys.stderr)
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


def frontend_radius_token_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    index_css = root / "frontend" / "src" / "index.css"
    if not index_css.is_file():
        return [f"{rel(index_css) if root == ROOT else index_css}: missing radius token source"]

    css = index_css.read_text(encoding="utf-8")
    if "--radius: 0.5rem;" not in css:
        issues.append("frontend/src/index.css: --radius must stay 0.5rem (8px panel token)")

    src_root = root / "frontend" / "src"
    if not src_root.exists():
        return issues

    oversized = re.compile(r"\brounded-(xl|2xl|3xl)\b")
    arbitrary = re.compile(r"\brounded-\[([^\]]+)\]")
    for path in iter_files(src_root, (".ts", ".tsx")):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        text = path.read_text(encoding="utf-8")
        for idx, line in enumerate(text.splitlines(), 1):
            if oversized.search(line):
                issues.append(f"{normalized}:{idx}: avoid radius above 8px on core UI surfaces: {line.strip()}")
            match = arbitrary.search(line)
            if match and "var(--radius" not in match.group(1):
                issues.append(f"{normalized}:{idx}: use shared radius tokens instead of arbitrary radius: {line.strip()}")
    return issues


def frontend_component_composition_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    src_root = root / "frontend" / "src"
    route_root = src_root / "routes"

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
        duplicated_flow_trace = re.compile(
            r"(grid-cols-\[(28|34|40)px_1fr\]|bottom-\[-\d+px\]|h-\[calc\(100%)"
        )
        access_path_keys = (
            "landing.signInStep",
            "landing.verifyAccess",
            "landing.openWorkspace",
        )
        action_surface_roots = (
            "frontend/src/features/landing/",
            "frontend/src/features/auth/",
        )
        for path in iter_files(src_root, (".tsx",)):
            normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
            if normalized.endswith("frontend/src/components/visual/FlowTrace.tsx"):
                continue
            text = path.read_text(encoding="utf-8")
            if any(normalized.startswith(prefix) for prefix in action_surface_roots):
                for match in re.finditer(
                    r"<Link\b(?:(?!</Link>).)*?className=\"[^\"]*\binline-flex\b[^\"]*\"(?:(?!</Link>).)*?</Link>",
                    text,
                    flags=re.DOTALL,
                ):
                    idx = text.count("\n", 0, match.start()) + 1
                    issues.append(
                        f"{normalized}:{idx}: navigation CTA styling must use ActionLink so icon, spacing, and states stay consistent"
                    )
            for idx, line in enumerate(text.splitlines(), 1):
                if duplicated_flow_trace.search(line):
                    issues.append(
                        f"{normalized}:{idx}: duplicated flow/timeline geometry; use FlowTrace instead"
                    )
                if (
                    not normalized.endswith("frontend/src/components/visual/AccessPathTrace.tsx")
                    and any(key in line for key in access_path_keys)
                ):
                    issues.append(
                        f"{normalized}:{idx}: duplicated access path trace; use AccessPathTrace instead"
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


def frontend_style_issues(root: Path = ROOT) -> list[str]:
    return [
        *frontend_radius_token_issues(root),
        *frontend_tailwind_opacity_issues(root),
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


def check_buf_modules(_args: argparse.Namespace | None = None) -> int:
    return sync_buf_yaml.main_with_args(["--check"]) if hasattr(sync_buf_yaml, "main_with_args") else module_main("sync_buf_yaml.py", ["--check"])


def check_buf_breaking_against_base(_args: argparse.Namespace | None = None) -> int:
    base_ref = os.environ.get("BASE_REF")
    if not base_ref:
        print("buf-breaking-against-base FAIL: Set BASE_REF (e.g. origin/main)", file=sys.stderr)
        return 1
    if not ref_exists(base_ref):
        print(f"buf-breaking-against-base FAIL: missing {base_ref}", file=sys.stderr)
        return 1
    if shutil.which("buf") is None:
        print("buf-breaking-against-base FAIL: buf CLI not on PATH", file=sys.stderr)
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
            result = run(["buf", "breaking", path, "--against", f".git#ref={base_ref},subdir={path}"], check=False)
            if result.returncode != 0:
                return result.returncode
        else:
            print(f"buf breaking: skip {path} (new on this branch - no baseline on {base_ref})")
    print("buf-breaking-against-base: OK")
    return 0


def check_scripts_standard(_args: argparse.Namespace | None = None) -> int:
    issues: list[str] = []
    for path in sorted(SCRIPTS.iterdir()):
        if not path.is_file():
            continue
        if path.suffix == ".py":
            continue
        issues.append(f"{rel(path)}: top-level scripts must be Python; add a scripts/axis.py subcommand instead")
    hook = SCRIPTS / "hooks" / "pre-push"
    if hook.is_file():
        text = hook.read_text(encoding="utf-8", errors="ignore")
        if "axis.py" not in text:
            issues.append(f"{rel(hook)}: pre-push hook must delegate to scripts/axis.py")
    if issues:
        print("check-scripts-standard FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1
    print("check-scripts-standard: OK")
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


def added_lines(range_spec: str, include: callable[[str], bool]) -> Iterable[tuple[str, str]]:
    result = run([exe("git"), "diff", "--unified=0", range_spec], capture=True, check=False)
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        raise CheckError(f"added_lines: git diff failed for {range_spec}: {detail}")
    current = ""
    rows: list[tuple[str, str]] = []
    for line in result.stdout.splitlines():
        if line.startswith("+++ b/"):
            current = line[6:].replace("\\", "/")
            continue
        if not current or not include(current):
            continue
        if line.startswith("+") and not line.startswith("+++"):
            rows.append((current, line[1:]))
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
        r"TODO|FIXME|NotImplementedException|placeholder|stub",
        lambda p: (p.startswith("src/") or p.startswith("tests/") or p.startswith("frontend/src/"))
        and "/obj/" not in p
        and "/node_modules/" not in p,
        "New TODO/FIXME/stub marker introduced - resolve or open an issue",
    ),
]


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
    Path("CLAUDE.md"),
    Path("CONTRIBUTING.md"),
    Path(".github/PULL_REQUEST_TEMPLATE.md"),
]

GOVERNANCE_COMMANDS_OWNED_BY_AGENT_CHECKLIST = [
    "python scripts/axis.py check policy-tests",
    "python scripts/axis.py check doc-drift",
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
            ("run: python scripts/axis.py check vulnerable-packages", "vulnerable package gate runs in CI"),
            ("run: python scripts/axis.py check test-naming", ".NET test naming gate runs in CI"),
            ("run: dotnet build --no-restore", ".NET build runs in CI"),
            ("run: dotnet format Axis.sln --verify-no-changes --no-restore", ".NET format gate runs in CI"),
            ("dotnet test --no-build", "full .NET test suite runs in CI"),
            ("npm run gen:api-types", "frontend API type generation runs in CI"),
            ("git diff --exit-code -- src/lib/api-types.ts", "frontend API type diff fails stale generated types"),
            ("run: npm run ci", "frontend typecheck/lint runs in CI"),
            ("run: npm run test", "frontend tests run in CI"),
            ("run: python scripts/axis.py check policy-tests", "policy gate tests run in CI"),
            ("run: python scripts/axis.py check doc-drift", "doc drift runs in CI"),
            ("BASE_BRANCH: main", "doc drift compares against main"),
        ],
    ),
    (
        Path("scripts/axis.py"),
        [
            ('step("policy gate tests", lambda: check_policy_tests())', "local verify runs policy gate tests"),
            ('step("doc drift", lambda: check_doc_drift())', "local verify runs doc drift"),
            ("for issue in governance_owner_boundary_issues():", "doc drift checks governance owner boundaries"),
            ("for issue in review_findings_registry_issues():", "doc drift checks review findings registry rows"),
            ("for issue in enforcement_truth_audit_issues():", "doc drift checks enforcement truth wiring"),
        ],
    ),
    (
        Path("scripts/hooks/pre-push"),
        [
            ('scripts/axis.py" verify', "pre-push delegates to scripts/axis.py verify"),
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
            ('fresh.Should().Contain("\\"orgName\\"");', "OpenAPI test asserts camelCase wire shape"),
            ('fresh.Should().NotContain("\\"org_name\\"");', "OpenAPI test rejects snake_case wire drift"),
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
                        "Link to agent-checklist.md#verification-gate--verify-before-push instead."
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

    stale_rx = re.compile(r"feature file|see gaps below|^> \*\*Wireframe\*\*:|docs/epics/|_template-feature-us|\| Diagram \| Source \| Preview \|")
    stale_files = list((ROOT / "docs").rglob("*.md")) + list((ROOT / ".github").rglob("*.md"))
    stale_files.extend(ROOT / name for name in ("CLAUDE.md", "CONTRIBUTING.md", "README.md"))
    for path in stale_files:
        if not path.is_file():
            continue
        for idx, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if stale_rx.search(line):
                fail(issues, f"Stale terminology in {rel(path)}: {idx}:{line} (Epic->Use-case migration - see docs/use-cases/README.md)")

    lesson_rx = re.compile(r"\*\*Lesson|[Ll]esson [(]|[Ll]esson[)]")
    lesson_files = list((ROOT / "docs" / "playbooks").rglob("*.md"))
    lesson_files.extend(ROOT / name for name in ("CLAUDE.md", "docs/ARCHITECTURE.md"))
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
        ("check-ef-domain-mapping", check_ef_domain_mapping),
        ("check-frontend-api-contracts", check_frontend_api_contracts),
        ("check-frontend-style", check_frontend_style),
        ("check-frontend-component-composition", check_frontend_component_composition),
        ("check-frontend-quality", check_frontend_quality),
        ("check-use-case-docs.py", lambda _=None: run_module_check("check-use-case-docs.py", ["--check"])),
        ("check-doc-link-targets.py", lambda _=None: run_module_check("check-doc-link-targets.py", ["--check"])),
        ("check-doc-navigation", check_doc_navigation),
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


def verify(args: argparse.Namespace) -> int:
    range_spec = diff_range()
    paths = changed_paths(range_spec)

    dotnet = False
    frontend = False
    if not paths:
        dotnet = True
        frontend = True
    else:
        dotnet = any(
            re.search(r"^(src/|tests/|Directory[.]|Axis[.]sln$|global[.]json$|[.]editorconfig$|openapi[.]json$|[.]github/workflows/build-and-test[.]yml$)", p)
            for p in paths
        )
        frontend = any(
            re.search(r"^(frontend/|[.]editorconfig$|openapi[.]json$|[.]github/workflows/build-and-test[.]yml$)", p)
            for p in paths
        )

    api_surface_drift = any_changed(paths, r"^src/Axis[.]Api/Endpoints/") and not any_changed(paths, r"^openapi[.]json$")
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

    print(f"verify - .NET={dotnet} frontend={frontend}")

    if dotnet:
        step(".NET test naming", lambda: check_test_naming())
        step(".NET build", lambda: run([exe("dotnet"), "build", "Axis.sln", "--nologo"], check=False).returncode)
        step(".NET vulnerable packages", lambda: check_vulnerable_packages())
        step(".NET format", lambda: run([exe("dotnet"), "format", "Axis.sln", "--verify-no-changes"], check=False).returncode)
        step(".NET test (unit projects)", lambda: test_unit(argparse.Namespace(dotnet_args=[])))

    if frontend:
        step("frontend ci (tsc + biome)", lambda: run([exe("npm"), "run", "ci"], cwd=ROOT / "frontend", check=False).returncode)
        step("frontend test", lambda: run([exe("npm"), "run", "test"], cwd=ROOT / "frontend", check=False).returncode)

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


def generate_api_contracts(_args: argparse.Namespace | None = None) -> int:
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
        ([exe("npm"), "run", "gen:api-types"], ROOT / "frontend", None),
    ]
    for command, cwd, env in commands:
        result = run(command, cwd=cwd, env=env, check=False)
        if result.returncode != 0:
            return result.returncode
    return 0


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
    result = run([exe("git"), "config", "core.hooksPath", "scripts/hooks"], check=False)
    if result.returncode != 0:
        return result.returncode
    hook = ROOT / "scripts" / "hooks" / "pre-push"
    if os.name != "nt" and hook.exists():
        hook.chmod(hook.stat().st_mode | 0o111)
    print("Installed: core.hooksPath = scripts/hooks (pre-push runs python scripts/axis.py verify).")
    return 0


def bootstrap(_args: argparse.Namespace | None = None) -> int:
    missing = [name for name in ("git", "dotnet", "node", "npm") if shutil.which(name) is None and shutil.which(f"{name}.cmd") is None]
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
    sub.add_parser("install-hooks").set_defaults(func=install_hooks)
    sub.add_parser("verify").set_defaults(func=verify)

    check = sub.add_parser("check")
    check_sub = check.add_subparsers(dest="check_command", required=True)
    check_sub.add_parser("doc-drift").set_defaults(func=check_doc_drift)
    check_sub.add_parser("policy-tests").set_defaults(func=check_policy_tests)
    check_sub.add_parser("text-encoding").set_defaults(func=check_text_encoding)
    check_sub.add_parser("scripts-standard").set_defaults(func=check_scripts_standard)
    check_sub.add_parser("test-naming").set_defaults(func=check_test_naming)
    check_sub.add_parser("test-project-classification").set_defaults(func=check_test_project_classification)
    check_sub.add_parser("vulnerable-packages").set_defaults(func=check_vulnerable_packages)
    check_sub.add_parser("ef-domain-mapping").set_defaults(func=check_ef_domain_mapping)
    check_sub.add_parser("frontend-api-contracts").set_defaults(func=check_frontend_api_contracts)
    check_sub.add_parser("frontend-style").set_defaults(func=check_frontend_style)
    check_sub.add_parser("frontend-component-composition").set_defaults(func=check_frontend_component_composition)
    check_sub.add_parser("frontend-quality").set_defaults(func=check_frontend_quality)
    check_sub.add_parser("buf-modules").set_defaults(func=check_buf_modules)
    check_sub.add_parser("buf-breaking-against-base").set_defaults(func=check_buf_breaking_against_base)
    check_sub.add_parser("local-dev-docs").set_defaults(
        func=lambda _args: run_module_check("check-local-dev-docs.py", ["--check"])
    )
    check_sub.add_parser("doc-link-targets").set_defaults(
        func=lambda _args: run_module_check("check-doc-link-targets.py", ["--check"])
    )
    check_sub.add_parser("doc-navigation").set_defaults(func=check_doc_navigation)
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
