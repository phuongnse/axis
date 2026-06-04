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


def git_ls_files(pattern: str) -> list[str]:
    return [line for line in git(["ls-files", pattern]).splitlines() if line.strip()]


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
        ("check-scripts-standard", check_scripts_standard),
        ("check-ef-domain-mapping", check_ef_domain_mapping),
        ("check-frontend-api-contracts", check_frontend_api_contracts),
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
        ([exe("dotnet"), "build", "src/Axis.Api/Axis.Api.csproj", "--nologo"], ROOT),
        ([exe("dotnet"), "tool", "run", "swagger", "tofile", "--output", str(ROOT / "openapi.json"), "bin/Debug/net8.0/Axis.Api.dll", "v1"], ROOT / "src" / "Axis.Api"),
        ([exe("npm"), "run", "gen:api-types"], ROOT / "frontend"),
    ]
    for command, cwd in commands:
        result = run(command, cwd=cwd, check=False)
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
    check_sub.add_parser("scripts-standard").set_defaults(func=check_scripts_standard)
    check_sub.add_parser("test-naming").set_defaults(func=check_test_naming)
    check_sub.add_parser("test-project-classification").set_defaults(func=check_test_project_classification)
    check_sub.add_parser("vulnerable-packages").set_defaults(func=check_vulnerable_packages)
    check_sub.add_parser("ef-domain-mapping").set_defaults(func=check_ef_domain_mapping)
    check_sub.add_parser("frontend-api-contracts").set_defaults(func=check_frontend_api_contracts)
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
