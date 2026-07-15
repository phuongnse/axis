"""Deterministic frontend source-policy checks used by the Axis CLI."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

from axis_repo import ROOT, iter_files


def rel(path: Path) -> str:
    return str(path.relative_to(ROOT)).replace("\\", "/")


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

    for path in iter_files(src_root, (".ts", ".tsx")):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        text = path.read_text(encoding="utf-8")
        in_ui_primitives = path.is_relative_to(ui_root) if hasattr(path, "is_relative_to") else False
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


def frontend_public_route_navigation_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    routes_root = root / "frontend" / "src" / "routes"
    if not routes_root.exists():
        return issues

    route_factory = re.compile(r"\bcreate(?:Lazy)?FileRoute\(")
    for path in iter_files(routes_root, (".tsx",)):
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        route_path = str(path.relative_to(routes_root)).replace("\\", "/")
        text = path.read_text(encoding="utf-8")

        if not route_factory.search(text):
            continue
        if route_path == "__root.tsx":
            continue
        if route_path.startswith("_") and "/" not in route_path:
            continue
        if route_path.startswith("_authenticated"):
            continue
        redirect_only_route = re.search(r"\bNavigate\b", text) or (
            "beforeLoad:" in text
            and "component:" not in text
            and "pendingComponent:" not in text
            and "errorComponent:" not in text
            and "notFoundComponent:" not in text
        )
        if redirect_only_route:
            continue

        if "export const routeNavigation" not in text or "publicRouteNavigation(" not in text:
            issues.append(
                f"{normalized}: public route must export `routeNavigation = publicRouteNavigation(...)` "
                "so escape navigation is declared at the route boundary"
            )
        if "from '@/lib/route-navigation'" not in text:
            issues.append(
                f"{normalized}: public route navigation metadata must use '@/lib/route-navigation'"
            )

    return issues


def frontend_route_access_group_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    routes_root = root / "frontend" / "src" / "routes"
    if not routes_root.exists():
        return issues

    guest_group_path = routes_root / "_guest.tsx"
    guest_route_paths = {
        "auth/verify.lazy.tsx",
        "auth/verify.tsx",
        "register.lazy.tsx",
        "register.tsx",
        "register_.confirmation.lazy.tsx",
        "register_.confirmation.tsx",
        "sign-in.lazy.tsx",
        "sign-in.tsx",
    }

    guest_leaf_exists = False
    for path in iter_files(routes_root, (".tsx",)):
        route_path = str(path.relative_to(routes_root)).replace("\\", "/")
        normalized = rel(path) if root == ROOT else str(path.relative_to(root)).replace("\\", "/")
        text = path.read_text(encoding="utf-8")

        if route_path in guest_route_paths:
            issues.append(
                f"{normalized}: guest-only auth routes must live under the `_guest` "
                "pathless route group instead of declaring per-route guards"
            )

        if route_path.startswith("_guest/"):
            guest_leaf_exists = True
            if "redirectAuthenticatedUserFromGuestRoute" in text or "beforeLoad:" in text:
                issues.append(
                    f"{normalized}: guest leaf routes inherit the `_guest` guard; "
                    "do not attach guest guards to individual leaf routes"
                )

    if guest_leaf_exists:
        normalized_group = (
            rel(guest_group_path)
            if root == ROOT
            else str(guest_group_path.relative_to(root)).replace("\\", "/")
        )
        if not guest_group_path.exists():
            issues.append(
                f"{normalized_group}: guest-only routes require a `_guest` pathless route group"
            )
        else:
            group_text = guest_group_path.read_text(encoding="utf-8")
            if "redirectAuthenticatedUserFromGuestRoute" not in group_text or "beforeLoad:" not in group_text:
                issues.append(
                    f"{normalized_group}: `_guest` route group must own the guest-only redirect guard"
                )

    return issues


def frontend_transient_handoff_issues(root: Path = ROOT) -> list[str]:
    issues: list[str] = []
    src_root = root / "frontend" / "src"
    routes_root = src_root / "routes"

    callback_lazy_route = routes_root / "callback.lazy.tsx"
    if callback_lazy_route.exists():
        normalized = rel(callback_lazy_route) if root == ROOT else str(callback_lazy_route.relative_to(root)).replace("\\", "/")
        issues.append(
            f"{normalized}: callback success handoffs must run before render; use a non-lazy `/callback` route with `beforeLoad`"
        )

    callback_route = routes_root / "callback.tsx"
    if callback_route.exists():
        normalized = rel(callback_route) if root == ROOT else str(callback_route.relative_to(root)).replace("\\", "/")
        text = callback_route.read_text(encoding="utf-8")
        if "beforeLoad:" not in text or "redirectFromCallbackRoute" not in text:
            issues.append(
                f"{normalized}: `/callback` must perform successful token handoff in `beforeLoad` before rendering recovery UI"
            )

    callback_page = src_root / "features" / "auth" / "components" / "CallbackPage.tsx"
    if callback_page.exists():
        normalized = rel(callback_page) if root == ROOT else str(callback_page.relative_to(root)).replace("\\", "/")
        text = callback_page.read_text(encoding="utf-8")
        forbidden = {
            "exchangeAuthorizationCode": "exchange tokens in the route handoff guard instead of `CallbackPage`",
            "auth.callback.completing": "remove transient callback success copy; render only recovery UI",
            "auth.callback.title": "remove transient callback success copy; render only recovery UI",
            "Completing sign-in": "remove transient callback success copy; render only recovery UI",
        }
        for token, message in forbidden.items():
            if token in text:
                issues.append(f"{normalized}: {message}")

    translations = src_root / "features" / "preferences" / "translations.ts"
    if translations.exists():
        normalized = rel(translations) if root == ROOT else str(translations.relative_to(root)).replace("\\", "/")
        text = translations.read_text(encoding="utf-8")
        for token in ("auth.callback.completing", "auth.callback.title"):
            if token in text:
                issues.append(
                    f"{normalized}: stale callback pending translation `{token}` is not valid; callback success handoffs are silent"
                )

    return issues


def frontend_quality_issues(root: Path = ROOT) -> list[str]:
    return [
        *frontend_ui_system_issues(root),
        *frontend_component_file_name_issues(root),
        *frontend_tailwind_opacity_issues(root),
        *frontend_form_schema_type_issues(root),
        *frontend_test_async_boundary_issues(root),
        *frontend_public_route_navigation_issues(root),
        *frontend_route_access_group_issues(root),
        *frontend_transient_handoff_issues(root),
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
