"""Canonical Axis theme validation and deterministic projections."""

from __future__ import annotations

import json
import math
import re
from pathlib import Path
from typing import Any

THEME_SOURCE = Path("theme/axis-theme.json")
WEB_THEME_OUTPUT = Path("frontend/src/theme.generated.css")
EMAIL_THEME_OUTPUT = Path(
    "src/Modules/Identity/Axis.Identity.Infrastructure/Services/TransactionalEmailTheme.g.cs"
)

REQUIRED_COLOR_TOKENS = (
    "background",
    "foreground",
    "card",
    "card-foreground",
    "popover",
    "popover-foreground",
    "primary",
    "primary-foreground",
    "secondary",
    "secondary-foreground",
    "muted",
    "muted-foreground",
    "accent",
    "accent-foreground",
    "destructive",
    "info",
    "info-foreground",
    "success",
    "success-foreground",
    "warning",
    "warning-foreground",
    "border",
    "input",
    "ring",
    "chart-1",
    "chart-2",
    "chart-3",
    "chart-4",
    "chart-5",
    "sidebar",
    "sidebar-foreground",
    "sidebar-primary",
    "sidebar-primary-foreground",
    "sidebar-accent",
    "sidebar-accent-foreground",
    "sidebar-border",
    "sidebar-ring",
)

_OKLCH_RE = re.compile(
    r"^oklch\(\s*(?P<lightness>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s+"
    r"(?P<chroma>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s+"
    r"(?P<hue>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*\)$"
)


class ThemeValidationError(ValueError):
    """Raised when the authored theme contract is invalid."""


def _mapping(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise ThemeValidationError(f"{label} must be an object")
    return value


def _exact_keys(value: dict[str, Any], expected: set[str], label: str) -> None:
    if set(value) == expected:
        return
    missing = sorted(expected - set(value))
    unknown = sorted(set(value) - expected)
    details: list[str] = []
    if missing:
        details.append(f"missing: {', '.join(missing)}")
    if unknown:
        details.append(f"unknown: {', '.join(unknown)}")
    raise ThemeValidationError(f"{label} must contain exactly the required keys ({'; '.join(details)})")


def _nonempty_string(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ThemeValidationError(f"{label} must be a non-empty string")
    if any(character in value for character in ("\n", "\r", "{", "}", ";")):
        raise ThemeValidationError(f"{label} contains unsupported control characters")
    return value.strip()


def _parse_oklch(value: Any, label: str) -> tuple[float, float, float]:
    text = _nonempty_string(value, label)
    match = _OKLCH_RE.fullmatch(text)
    if match is None:
        raise ThemeValidationError(f"{label} must use `oklch(L C H)` syntax")
    lightness = float(match.group("lightness"))
    chroma = float(match.group("chroma"))
    hue = float(match.group("hue"))
    for component_name, component in (
        ("lightness", lightness),
        ("chroma", chroma),
        ("hue", hue),
    ):
        if not math.isfinite(component):
            raise ThemeValidationError(f"{label} {component_name} must be finite")
    if not 0 <= lightness <= 1:
        raise ThemeValidationError(f"{label} lightness must be between 0 and 1")
    if chroma < 0:
        raise ThemeValidationError(f"{label} chroma must be non-negative")
    return lightness, chroma, hue


def load_theme(root: Path) -> dict[str, Any]:
    source = root / THEME_SOURCE
    try:
        value = json.loads(source.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ThemeValidationError(f"missing canonical theme source: {THEME_SOURCE}") from exc
    except json.JSONDecodeError as exc:
        raise ThemeValidationError(f"invalid canonical theme JSON: {exc}") from exc

    theme = _mapping(value, "theme")
    _exact_keys(theme, {"schemaVersion", "typography", "radius", "colors"}, "theme")
    if theme["schemaVersion"] != 1:
        raise ThemeValidationError("theme schemaVersion must be 1")

    typography = _mapping(theme["typography"], "typography")
    _exact_keys(typography, {"web", "email"}, "typography")
    web = _mapping(typography["web"], "typography.web")
    email = _mapping(typography["email"], "typography.email")
    _exact_keys(web, {"sans", "heading"}, "typography.web")
    _exact_keys(email, {"sans"}, "typography.email")
    _nonempty_string(web["sans"], "typography.web.sans")
    _nonempty_string(web["heading"], "typography.web.heading")
    _nonempty_string(email["sans"], "typography.email.sans")
    _nonempty_string(theme["radius"], "radius")

    colors = _mapping(theme["colors"], "colors")
    _exact_keys(colors, {"light", "dark"}, "colors")
    required = set(REQUIRED_COLOR_TOKENS)
    for scheme_name in ("light", "dark"):
        scheme = _mapping(colors[scheme_name], f"colors.{scheme_name}")
        if set(scheme) != required:
            missing = sorted(required - set(scheme))
            unknown = sorted(set(scheme) - required)
            details = [*(f"missing: {item}" for item in missing), *(f"unknown: {item}" for item in unknown)]
            raise ThemeValidationError(
                f"{scheme_name} colors must contain exactly the required semantic tokens"
                + (f" ({'; '.join(details)})" if details else "")
            )
        for token in REQUIRED_COLOR_TOKENS:
            _parse_oklch(scheme[token], f"colors.{scheme_name}.{token}")

    return theme


def _render_web_theme(theme: dict[str, Any]) -> str:
    typography = theme["typography"]["web"]
    colors = theme["colors"]
    lines = [
        "/* <auto-generated>",
        " * Generated from theme/axis-theme.json by `python scripts/axis.py generate theme`.",
        " * Do not edit this file directly.",
        " * </auto-generated> */",
        "",
        "@theme inline {",
        f"  --font-heading: {typography['heading']};",
        f"  --font-sans: {typography['sans']};",
    ]
    lines.extend(f"  --color-{token}: var(--{token});" for token in REQUIRED_COLOR_TOKENS)
    lines.extend(
        (
            "  --radius-sm: calc(var(--radius) * 0.6);",
            "  --radius-md: calc(var(--radius) * 0.8);",
            "  --radius-lg: var(--radius);",
            "  --radius-xl: calc(var(--radius) * 1.4);",
            "  --radius-2xl: calc(var(--radius) * 1.8);",
            "  --radius-3xl: calc(var(--radius) * 2.2);",
            "  --radius-4xl: calc(var(--radius) * 2.6);",
            "}",
            "",
        )
    )
    for selector, scheme_name in ((":root", "light"), (".dark", "dark")):
        lines.append(f"{selector} {{")
        scheme = colors[scheme_name]
        lines.extend(f"  --{token}: {scheme[token]};" for token in REQUIRED_COLOR_TOKENS)
        lines.append(f"  --radius: {theme['radius']};")
        lines.extend(("}", ""))
    return "\n".join(lines)


def _linear_to_srgb(value: float) -> float:
    if value <= 0.0031308:
        return 12.92 * value
    return 1.055 * (value ** (1 / 2.4)) - 0.055


def _oklch_to_srgb(value: str) -> tuple[float, float, float]:
    lightness, chroma, hue = _parse_oklch(value, "email projection color")
    hue_radians = math.radians(hue % 360)
    a = chroma * math.cos(hue_radians)
    b = chroma * math.sin(hue_radians)

    l_root = lightness + 0.3963377774 * a + 0.2158037573 * b
    m_root = lightness - 0.1055613458 * a - 0.0638541728 * b
    s_root = lightness - 0.0894841775 * a - 1.291485548 * b
    l_value = l_root**3
    m_value = m_root**3
    s_value = s_root**3

    red = 4.0767416621 * l_value - 3.3077115913 * m_value + 0.2309699292 * s_value
    green = -1.2684380046 * l_value + 2.6097574011 * m_value - 0.3413193965 * s_value
    blue = -0.0041960863 * l_value - 0.7034186147 * m_value + 1.707614701 * s_value
    return tuple(max(0.0, min(1.0, _linear_to_srgb(channel))) for channel in (red, green, blue))


def _rgb_to_hex(rgb: tuple[float, float, float]) -> str:
    return "#" + "".join(f"{round(channel * 255):02x}" for channel in rgb)


def _blend(
    foreground: tuple[float, float, float],
    background: tuple[float, float, float],
    alpha: float,
) -> tuple[float, float, float]:
    return tuple(alpha * foreground[index] + (1 - alpha) * background[index] for index in range(3))


def _csharp_string(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"')


def _render_email_theme(theme: dict[str, Any]) -> str:
    light = theme["colors"]["light"]
    converted = {token: _oklch_to_srgb(light[token]) for token in REQUIRED_COLOR_TOKENS}
    card = converted["card"]
    warning = converted["warning"]
    values = {
        "BackgroundColor": _rgb_to_hex(converted["background"]),
        "CardColor": _rgb_to_hex(card),
        "ForegroundColor": _rgb_to_hex(converted["foreground"]),
        "MutedColor": _rgb_to_hex(converted["muted-foreground"]),
        "BorderColor": _rgb_to_hex(converted["border"]),
        "PrimaryColor": _rgb_to_hex(converted["primary"]),
        "PrimaryForegroundColor": _rgb_to_hex(converted["primary-foreground"]),
        "LinkColor": _rgb_to_hex(converted["primary"]),
        "AccentColor": _rgb_to_hex(converted["accent"]),
        "WarningBackgroundColor": _rgb_to_hex(_blend(warning, card, 0.10)),
        "WarningBorderColor": _rgb_to_hex(_blend(warning, card, 0.25)),
        "WarningTextColor": _rgb_to_hex(warning),
        "FontFamily": theme["typography"]["email"]["sans"],
    }
    lines = [
        "// <auto-generated />",
        "// Generated from theme/axis-theme.json by `python scripts/axis.py generate theme`.",
        "",
        "namespace Axis.Identity.Infrastructure.Services;",
        "",
        "internal static class TransactionalEmailTheme",
        "{",
    ]
    lines.extend(
        f'    internal const string {name} = "{_csharp_string(value)}";'
        for name, value in values.items()
    )
    lines.extend(("}", ""))
    return "\n".join(lines)


def render_theme_artifacts(root: Path) -> dict[Path, str]:
    theme = load_theme(root)
    return {
        WEB_THEME_OUTPUT: _render_web_theme(theme),
        EMAIL_THEME_OUTPUT: _render_email_theme(theme),
    }


def theme_artifact_issues(root: Path) -> list[str]:
    try:
        expected = render_theme_artifacts(root)
    except (OSError, ThemeValidationError) as exc:
        return [f"{THEME_SOURCE}: {exc}"]

    issues: list[str] = []
    for relative_path, expected_content in expected.items():
        path = root / relative_path
        if not path.is_file():
            issues.append(f"{relative_path}: missing generated theme artifact")
            continue
        try:
            actual = path.read_text(encoding="utf-8")
        except OSError as exc:
            issues.append(f"{relative_path}: cannot read generated theme artifact: {exc}")
            continue
        if actual != expected_content:
            issues.append(f"{relative_path}: stale generated theme artifact")
    return issues


def write_theme_artifacts(root: Path) -> list[Path]:
    artifacts = render_theme_artifacts(root)
    written: list[Path] = []
    for relative_path, content in artifacts.items():
        path = root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")
        written.append(relative_path)
    return written


def is_theme_path(path: str) -> bool:
    normalized = path.replace("\\", "/")
    return normalized in {
        str(THEME_SOURCE),
        str(WEB_THEME_OUTPUT),
        str(EMAIL_THEME_OUTPUT),
        "frontend/src/index.css",
        "scripts/axis_theme.py",
        "scripts/tests/test_axis_theme.py",
    }
