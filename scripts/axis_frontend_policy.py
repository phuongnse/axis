"""Deterministic frontend source-policy checks used by the Axis CLI."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from axis_repo import ROOT, iter_files


UI_FOUNDATION_PHASES = {
    "requested",
    "authorized",
    "defined",
    "reference-ready",
    "accepted",
    "adopted",
}
UI_FOUNDATION_MANIFEST_KEYS = {
    "schemaVersion",
    "phase",
    "goldenReference",
}
UI_GOLDEN_REFERENCE_KEYS = {"archetype", "route"}


def rel(path: Path) -> str:
    return str(path.relative_to(ROOT)).replace("\\", "/")


def ui_foundation_issues(root: Path = ROOT) -> list[str]:
    manifest_path = root / "frontend" / "ui-foundation.json"
    contract_path = root / "docs" / "foundations" / "visual-system" / "axis-visual-system.md"
    if not manifest_path.is_file():
        return ["frontend/ui-foundation.json: active UI foundation state is missing"]

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as exc:
        return [f"frontend/ui-foundation.json: cannot read active UI foundation state: {exc}"]
    if not isinstance(manifest, dict):
        return ["frontend/ui-foundation.json: root value must be an object"]

    issues: list[str] = []
    unexpected_keys = sorted(set(manifest) - UI_FOUNDATION_MANIFEST_KEYS)
    missing_keys = sorted(UI_FOUNDATION_MANIFEST_KEYS - set(manifest))
    if unexpected_keys:
        issues.append(
            "frontend/ui-foundation.json: unexpected keys: " + ", ".join(unexpected_keys)
        )
    if missing_keys:
        issues.append(
            "frontend/ui-foundation.json: missing keys: " + ", ".join(missing_keys)
        )

    if manifest.get("schemaVersion") != 2:
        issues.append("frontend/ui-foundation.json: `schemaVersion` must be 2")

    phase = manifest.get("phase")
    if phase not in UI_FOUNDATION_PHASES:
        issues.append(
            "frontend/ui-foundation.json: unknown `phase`; use one of "
            f"{sorted(UI_FOUNDATION_PHASES)}"
        )

    golden_reference = manifest.get("goldenReference")
    if not isinstance(golden_reference, dict):
        issues.append("frontend/ui-foundation.json: `goldenReference` must be an object")
    else:
        unexpected_golden_keys = sorted(set(golden_reference) - UI_GOLDEN_REFERENCE_KEYS)
        missing_golden_keys = sorted(UI_GOLDEN_REFERENCE_KEYS - set(golden_reference))
        if unexpected_golden_keys:
            issues.append(
                "frontend/ui-foundation.json: `goldenReference` has unexpected keys: "
                + ", ".join(unexpected_golden_keys)
            )
        if missing_golden_keys:
            issues.append(
                "frontend/ui-foundation.json: `goldenReference` is missing keys: "
                + ", ".join(missing_golden_keys)
            )
        for field in ("archetype", "route"):
            value = golden_reference.get(field)
            if not isinstance(value, str) or not value.strip():
                issues.append(
                    f"frontend/ui-foundation.json: `goldenReference.{field}` must be a non-empty string"
                )

    if not contract_path.is_file():
        issues.append(
            "docs/foundations/visual-system/axis-visual-system.md: visual-system contract is missing"
        )
    else:
        try:
            contract = contract_path.read_text(encoding="utf-8")
        except OSError as exc:
            issues.append(
                "docs/foundations/visual-system/axis-visual-system.md: "
                f"cannot read visual-system contract: {exc}"
            )
        else:
            frontend_status = re.search(
                r"^[>| ]*\|\s*Frontend\s*\|\s*(?P<status>[A-Za-z-]+)\s*\|",
                contract,
                re.MULTILINE,
            )
            if frontend_status is None:
                issues.append(
                    "docs/foundations/visual-system/axis-visual-system.md: "
                    "implementation status must contain a Frontend row"
                )
            elif phase != "adopted" and frontend_status.group("status") == "Done":
                issues.append(
                    "docs/foundations/visual-system/axis-visual-system.md: "
                    "cannot claim `Frontend | Done` before phase `adopted`"
                )
    return issues


def check_ui_foundation(_args: argparse.Namespace | None = None) -> int:
    issues = ui_foundation_issues()
    if issues:
        for issue in issues:
            print(f"check-ui-foundation FAIL: {issue}", file=sys.stderr)
        return 1
    print("check-ui-foundation: OK")
    return 0


def frontend_ui_system_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    src_root = root / "frontend" / "src"
    ui_root = src_root / "components" / "ui"
    interaction_state_owner = src_root / "components" / "shared" / "interactionStates.ts"
    palette_utility = re.compile(
        r"\b(?:bg|text|border|ring|outline|fill|stroke|from|via|to|divide|placeholder|decoration)-"
        r"(?:(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|"
        r"blue|indigo|violet|purple|fuchsia|pink|rose)-[0-9]{2,3}(?:/[0-9]{1,3})?|black|white)\b"
    )
    arbitrary_value = re.compile(r"(?<![A-Za-z0-9_])(?:[A-Za-z0-9_:/.]+-)+\[[^\]\n]+\]")
    inline_color = re.compile(
        r"\b(?:color|background|backgroundColor|borderColor|fill|stroke)\s*:\s*['\"](?:#|rgba?[(]|hsla?[(]|oklch[(])",
        re.IGNORECASE,
    )
    interaction_state_visual = re.compile(
        r"(?<![A-Za-z0-9_-])(?:(?:dark|sm|md|lg|xl|2xl):)*"
        r"(?:(?:(?:group|peer|not|has)-)?(?:hover|focus|focus-visible|active|disabled|enabled|"
        r"checked|indeterminate|open|"
        r"aria-(?:\[[^\]]+\]|[A-Za-z0-9_-]+)|data-(?:\[[^\]]+\]|[A-Za-z0-9_-]+))"
        r"(?:/[A-Za-z0-9_-]+)?:)+"
        r"(?:\*{1,2}:)?(?:bg|text|border|ring|outline)-[A-Za-z0-9_./-]+"
    )
    import_target = re.compile(
        r"(?:\bfrom\s*|\bimport\s*(?:\(\s*)?|\brequire\s*\(\s*)"
        r"['\"](?P<target>[^'\"]+)['\"]"
    )
    forbidden_roots = (
        src_root / "features",
        src_root / "components" / "shared",
        src_root / "routes",
    )
    raw_badge_owners = {
        src_root / "components" / "shared" / "MetadataTag.tsx",
        src_root / "components" / "shared" / "StatusBadge.tsx",
        src_root / "components" / "shared" / "data-table" / "DataTableToolbar.tsx",
    }
    raw_alert_owners = {
        src_root / "components" / "shared" / "StatusNotice.tsx",
    }
    product_roots = (
        src_root / "features",
        src_root / "routes",
    )
    direct_pending_animation = re.compile(r"\banimate-(?:spin|pulse)\b")
    legacy_query_loading = re.compile(r"[.]isLoading\b")
    background_refresh_as_initial_load = re.compile(
        r"\bloading\s*:\s*[^,\n]*[.]isFetching\b"
    )
    pending_label_swap = re.compile(
        r"\{\s*(?:loading|[A-Za-z_$][A-Za-z0-9_$.]*[.]isPending|"
        r"[A-Za-z_$][A-Za-z0-9_$]*(?:Pending|Loading))"
        r"\s*[?]\s*t\([^{}]*?\)\s*:\s*t\([^{}]*?\)\s*\}",
        re.DOTALL,
    )
    raw_query_pending_status = re.compile(
        r"(?:\b[A-Za-z_$][A-Za-z0-9_$.]*[.](?:isPending|isFetching|isLoading)|\bloading)"
        r"[^?]{0,100}[?]\s*<p\b[^>]*\brole=['\"]status['\"]",
        re.DOTALL,
    )
    for path in iter_files(src_root, (".ts", ".tsx")):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        text = path.read_text(encoding="utf-8")
        in_ui_primitives = path.is_relative_to(ui_root) if hasattr(path, "is_relative_to") else False
        in_product_surface = any(path.is_relative_to(root_path) for root_path in product_roots)
        owns_interaction_states = path == interaction_state_owner
        if in_ui_primitives:
            for match in import_target.finditer(text):
                target = match.group("target")
                has_forbidden_import = (
                    re.match(r"@/(?:features|components/shared|routes)(?:/|$)", target)
                    or (
                        target.startswith(".")
                        and any((path.parent / target).resolve().is_relative_to(root_path.resolve()) for root_path in forbidden_roots)
                    )
                )
                if has_forbidden_import:
                    idx = text.count("\n", 0, match.start()) + 1
                    issues.append(
                        f"{normalized}:{idx}: registry primitives cannot depend on feature, shared, or route code"
                    )
            continue

        for match in import_target.finditer(text):
            target = match.group("target").replace("\\", "/")
            imports_raw_badge = target == "@/components/ui/badge" or target.endswith(
                "/components/ui/badge"
            ) or target.endswith("/ui/badge")
            if imports_raw_badge and path not in raw_badge_owners:
                idx = text.count("\n", 0, match.start()) + 1
                issues.append(
                    f"{normalized}:{idx}: raw Badge is restricted to semantic badge owners; "
                    "use StatusBadge for state/origin, MetadataTag for compact taxonomy or syntax, "
                    "and typography or a structural pattern for headings, steps, outcomes, and prose"
                )
            imports_raw_alert = target == "@/components/ui/alert" or target.endswith(
                "/components/ui/alert"
            ) or target.endswith("/ui/alert")
            if imports_raw_alert and path not in raw_alert_owners:
                idx = text.count("\n", 0, match.start()) + 1
                issues.append(
                    f"{normalized}:{idx}: raw Alert is restricted to the StatusNotice owner; "
                    "use StatusNotice for page, form, dialog, and menu feedback"
                )
            imports_raw_pending_visual = target in {
                "@/components/ui/spinner",
                "@/components/ui/skeleton",
            } or target.endswith(("/components/ui/spinner", "/components/ui/skeleton"))
            if in_product_surface and imports_raw_pending_visual:
                idx = text.count("\n", 0, match.start()) + 1
                issues.append(
                    f"{normalized}:{idx}: feature and route pending visuals must use shared async-state patterns"
                )
            if (
                in_product_surface
                and target.endswith("/components/shared/PendingIndicator")
            ):
                idx = text.count("\n", 0, match.start()) + 1
                issues.append(
                    f"{normalized}:{idx}: PendingIndicator is an internal shared visual; "
                    "use a semantic shared async pattern"
                )
            if (
                in_product_surface
                and target.endswith("/hooks/usePendingVisibility")
            ):
                idx = text.count("\n", 0, match.start()) + 1
                issues.append(
                    f"{normalized}:{idx}: pending timing is owned by shared async patterns; "
                    "feature and route code supplies semantic state only"
                )

        if in_product_surface:
            for match, message in (
                (
                    legacy_query_loading.finditer(text),
                    "TanStack Query initial state must use isPending; background refresh preserves current content",
                ),
                (
                    pending_label_swap.finditer(text),
                    "pending actions must use a shared async action with a stable visible label",
                ),
                (
                    raw_query_pending_status.finditer(text),
                    "query pending feedback must use a semantic shared async region",
                ),
            ):
                for occurrence in match:
                    idx = text.count("\n", 0, occurrence.start()) + 1
                    issues.append(f"{normalized}:{idx}: {message}")

        for idx, line in enumerate(text.splitlines(), 1):
            for match in palette_utility.finditer(line):
                issues.append(
                    f"{normalized}:{idx}: hard-coded Tailwind palette utility `{match.group(0)}`; use a semantic token"
                )
            for match in arbitrary_value.finditer(line):
                issues.append(
                    f"{normalized}:{idx}: arbitrary Tailwind value `{match.group(0)}`; use the standard scale or layout"
                )
            if inline_color.search(line):
                issues.append(
                    f"{normalized}:{idx}: component-local hard-coded color; use a semantic token"
                )
            if (
                in_product_surface and direct_pending_animation.search(line)
            ):
                issues.append(
                    f"{normalized}:{idx}: feature and route pending motion must use shared async-state patterns"
                )
            if (
                in_product_surface and background_refresh_as_initial_load.search(line)
            ):
                issues.append(
                    f"{normalized}:{idx}: DataTable initial loading must use query isPending; background isFetching preserves current content"
                )
            for match in interaction_state_visual.finditer(line):
                if not owns_interaction_states:
                    issues.append(
                        f"{normalized}:{idx}: interaction-state visual `{match.group(0)}` must be owned by "
                        "a registry primitive or frontend/src/components/shared/interactionStates.ts"
                    )
    return issues


def frontend_component_file_name_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
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
            text = path.read_text(encoding="utf-8")
            for idx, line in enumerate(text.splitlines(), 1):
                if "className=" in line:
                    issues.append(
                        f"{normalized}:{idx}: route files compose page components only; move styled UI into a component"
                    )

    if src_root.exists():
        standard_control = re.compile(
            r"<\s*(button|caption|dialog|input|label|option|optgroup|progress|select|table|tbody|td|textarea|tfoot|th|thead|tr)\b"
        )
        unformatted_select_value = re.compile(r"<\s*SelectValue\b[^>]*?/\s*>", re.DOTALL)
        for path in iter_files(src_root, (".tsx",)):
            normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
            text = path.read_text(encoding="utf-8")
            in_ui_primitives = path.is_relative_to(ui_root) if hasattr(path, "is_relative_to") else False
            if not in_ui_primitives:
                for idx, line in enumerate(text.splitlines(), 1):
                    if "@base-ui/react" in line or "@radix-ui" in line:
                        issues.append(
                            f"{normalized}:{idx}: headless UI primitives belong in shadcn primitives under frontend/src/components/ui, not feature components"
                        )
                    if "components/ui/native-select" in line:
                        issues.append(
                            f"{normalized}:{idx}: native fallback primitives require an approved platform-native behavior exception; use the interaction-consistent shadcn primitive by default"
                        )
                    for match in standard_control.finditer(line):
                        issues.append(
                            f"{normalized}:{idx}: standard UI control <{match.group(1)}> must use a shared shadcn UI primitive from frontend/src/components/ui"
                        )
                for match in unformatted_select_value.finditer(text):
                    line_number = text.count("\n", 0, match.start()) + 1
                    issues.append(
                        f"{normalized}:{line_number}: SelectValue must format the selected value from the same display-label source as SelectItem"
                    )
    return issues


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


def frontend_e2e_structure_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    e2e_root = root / "frontend" / "e2e"
    if not e2e_root.exists():
        return issues

    forbidden = {
        "waitForTimeout(": (
            "fixed browser sleeps are forbidden; wait for an observable URL, response, state, or locator"
        ),
        ".setTimeout(": (
            "journeys use the centrally owned Playwright timeout; split or fix the journey instead of overriding it"
        ),
        "mode: 'serial'": (
            "browser journeys must be independently runnable; do not couple them with serial shared state"
        ),
        'mode: "serial"': (
            "browser journeys must be independently runnable; do not couple them with serial shared state"
        ),
    }
    for path in iter_files(e2e_root, (".ts",)):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        text = path.read_text(encoding="utf-8")
        for token, message in forbidden.items():
            for match in re.finditer(re.escape(token), text):
                line_number = text.count("\n", 0, match.start()) + 1
                issues.append(f"{normalized}:{line_number}: {message}")
    return issues


def frontend_quality_issues(root: Path = ROOT) -> list[str]:
    return [
        *frontend_ui_system_issues(root),
        *frontend_component_file_name_issues(root),
        *frontend_tailwind_opacity_issues(root),
        *frontend_form_schema_type_issues(root),
        *frontend_test_async_boundary_issues(root),
        *frontend_e2e_structure_issues(root),
    ]


def check_frontend_quality(_args: argparse.Namespace | None = None) -> int:
    issues = frontend_quality_issues()
    if issues:
        for issue in issues:
            print(f"check-frontend-quality FAIL: {issue}", file=sys.stderr)
        print("\nSee docs/playbooks/frontend.md#component-design", file=sys.stderr)
        return 1
    print("check-frontend-quality: OK")
    return 0
