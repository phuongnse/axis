"""Canonical Axis theme validation and deterministic projections."""

from __future__ import annotations

import json
import math
import re
from pathlib import Path
from typing import Any

THEME_SOURCE = Path("theme/axis-theme.json")
WEB_THEME_OUTPUT = Path("frontend/src/theme.generated.css")
WEB_THEME_RUNTIME_OUTPUT = Path("frontend/src/theme.generated.ts")
EMAIL_THEME_OUTPUT = Path(
    "src/Modules/Identity/Axis.Identity.Infrastructure/Services/TransactionalEmailTheme.g.cs"
)

TYPOGRAPHY_ROLES = (
    "metadata",
    "body",
    "label",
    "componentTitle",
    "sectionTitle",
    "pageTitle",
)
SPACING_ROLES = ("inline", "region", "section", "pageCompact", "pageDefault", "pageWide")
DENSITY_ROLES = ("compactControl", "defaultControl", "touchTarget")
RADIUS_ROLES = ("flat", "control", "floating", "managed")
ELEVATION_ROLES = ("none", "floating", "managed", "dock")
ICON_ROLES = ("control", "navigation", "empty")
MOTION_STRING_ROLES = ("stateDuration", "floatingDuration", "easing")
MOTION_MILLISECOND_ROLES = (
    "contentDelayMs",
    "contentMinimumMs",
    "contextDelayMs",
    "contextMinimumMs",
)
LAYER_ROLES = ("base", "sticky", "floating", "modal", "managed", "notification")
AXIS_STYLE_TYPOGRAPHY_ROLE_MAP = {
    "metadata": "metadata",
    "body": "body",
    "label": "label",
    "componentTitle": "componentTitle",
    "sectionTitle": "sectionTitle",
    "pageTitle": "pageTitle",
}
AXIS_STYLE_SPACING_ROLE_MAP = {
    "inline": "inline",
    "region": "region",
    "pageCompact": "pageCompact",
    "pageDefaultAtSmall": "pageDefault",
    "pageWideAtLarge": "pageWide",
}
AXIS_STYLE_DENSITY_ROLE_MAP = {
    "touchTarget": "touchTarget",
    "defaultControl": "defaultControl",
    "compactControlAtSmall": "compactControl",
}
AXIS_STYLE_ICON_ROLE_MAP = {
    "control": "control",
    "navigation": "navigation",
    "empty": "empty",
}
AXIS_STYLE_RADIUS_ROLE_MAP = {
    "flat": "flat",
    "control": "control",
    "floating": "floating",
    "managed": "managed",
}
AXIS_STYLE_ELEVATION_ROLE_MAP = {
    "none": "none",
    "floating": "floating",
    "managed": "managed",
    "dock": "dock",
}
AXIS_STYLE_LAYER_ROLE_MAP = {
    "base": "base",
    "sticky": "sticky",
    "floating": "floating",
    "modal": "modal",
    "managed": "managed",
    "notification": "notification",
}
AXIS_STYLE_MOTION_ROLE_MAP = {
    "state": "stateDuration",
    "floating": "floatingDuration",
    "easing": "easing",
}
TEXT_CONTRAST_PAIRS = (
    ("foreground", "background"),
    ("card-foreground", "card"),
    ("popover-foreground", "popover"),
    ("primary-foreground", "primary"),
    ("secondary-foreground", "secondary"),
    ("muted-foreground", "background"),
    ("accent-foreground", "accent"),
    ("destructive", "background"),
    ("info-foreground", "info"),
    ("success-foreground", "success"),
    ("warning-foreground", "warning"),
    ("sidebar-foreground", "sidebar"),
    ("sidebar-primary-foreground", "sidebar-primary"),
    ("sidebar-accent-foreground", "sidebar-accent"),
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
_REM_RE = re.compile(r"^(?P<value>\d+(?:\.\d+)?)rem$")
_MILLISECOND_RE = re.compile(r"^(?P<value>\d+(?:\.\d+)?)ms$")
_CUBIC_BEZIER_RE = re.compile(
    r"^cubic-bezier\(\s*(?P<x1>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*,\s*"
    r"(?P<y1>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*,\s*"
    r"(?P<x2>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*,\s*"
    r"(?P<y2>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*\)$"
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


def _exact_string_roles(value: Any, roles: tuple[str, ...], label: str) -> dict[str, Any]:
    mapping = _mapping(value, label)
    _exact_keys(mapping, set(roles), label)
    for role in roles:
        _nonempty_string(mapping[role], f"{label}.{role}")
    return mapping


def _nonnegative_integer(value: Any, label: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        raise ThemeValidationError(f"{label} must be a non-negative integer")
    return value


def _positive_rem(value: Any, label: str) -> float:
    text = _nonempty_string(value, label)
    match = _REM_RE.fullmatch(text)
    if match is None or (parsed := float(match.group("value"))) <= 0:
        raise ThemeValidationError(f"{label} must be a positive `rem` value")
    return parsed


def _positive_milliseconds(value: Any, label: str) -> float:
    text = _nonempty_string(value, label)
    match = _MILLISECOND_RE.fullmatch(text)
    if match is None or (parsed := float(match.group("value"))) <= 0:
        raise ThemeValidationError(f"{label} must be a positive `ms` value")
    return parsed


def _cubic_bezier(value: Any, label: str) -> tuple[float, float, float, float]:
    text = _nonempty_string(value, label)
    match = _CUBIC_BEZIER_RE.fullmatch(text)
    if match is None:
        raise ThemeValidationError(f"{label} must use `cubic-bezier(x1, y1, x2, y2)` syntax")
    points = tuple(float(match.group(name)) for name in ("x1", "y1", "x2", "y2"))
    if not 0 <= points[0] <= 1 or not 0 <= points[2] <= 1:
        raise ThemeValidationError(f"{label} x control points must be between 0 and 1")
    return points


def _require_increasing(values: list[float], label: str) -> None:
    if values != sorted(set(values)):
        raise ThemeValidationError(f"{label} must increase by semantic depth")


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
    _exact_keys(theme, {"schemaVersion", "typography", "ui", "colors"}, "theme")
    if theme["schemaVersion"] != 2:
        raise ThemeValidationError("theme schemaVersion must be 2")

    typography = _mapping(theme["typography"], "typography")
    _exact_keys(typography, {"web", "email"}, "typography")
    web = _mapping(typography["web"], "typography.web")
    email = _mapping(typography["email"], "typography.email")
    _exact_keys(web, {"sans", "heading"}, "typography.web")
    _exact_keys(email, {"sans"}, "typography.email")
    _nonempty_string(web["sans"], "typography.web.sans")
    _nonempty_string(web["heading"], "typography.web.heading")
    _nonempty_string(email["sans"], "typography.email.sans")
    ui = _mapping(theme["ui"], "ui")
    _exact_keys(
        ui,
        {
            "typographyRoles",
            "spacingRoles",
            "densityRoles",
            "radiusRoles",
            "elevationRoles",
            "iconRoles",
            "motionRoles",
            "layerRoles",
        },
        "ui",
    )

    typography_roles = _mapping(ui["typographyRoles"], "ui.typographyRoles")
    _exact_keys(typography_roles, set(TYPOGRAPHY_ROLES), "ui.typographyRoles")
    for role in TYPOGRAPHY_ROLES:
        definition = _mapping(typography_roles[role], f"ui.typographyRoles.{role}")
        _exact_keys(definition, {"size", "lineHeight", "weight"}, f"ui.typographyRoles.{role}")
        size = _positive_rem(definition["size"], f"ui.typographyRoles.{role}.size")
        line_height = _positive_rem(
            definition["lineHeight"],
            f"ui.typographyRoles.{role}.lineHeight",
        )
        if line_height < size:
            raise ThemeValidationError(
                f"ui.typographyRoles.{role}.lineHeight must not be smaller than its size"
            )
        weight = definition["weight"]
        if not isinstance(weight, int) or isinstance(weight, bool) or weight not in range(100, 1000, 100):
            raise ThemeValidationError(
                f"ui.typographyRoles.{role}.weight must be a font weight from 100 through 900"
            )

    spacing = _exact_string_roles(ui["spacingRoles"], SPACING_ROLES, "ui.spacingRoles")
    spacing_values = {
        role: _positive_rem(spacing[role], f"ui.spacingRoles.{role}") for role in SPACING_ROLES
    }
    _require_increasing(
        [spacing_values[role] for role in ("inline", "region", "section")],
        "ui spacing relationship roles",
    )
    _require_increasing(
        [spacing_values[role] for role in ("pageCompact", "pageDefault", "pageWide")],
        "ui page spacing roles",
    )

    density = _exact_string_roles(ui["densityRoles"], DENSITY_ROLES, "ui.densityRoles")
    _require_increasing(
        [_positive_rem(density[role], f"ui.densityRoles.{role}") for role in DENSITY_ROLES],
        "ui density roles",
    )

    radii = _exact_string_roles(ui["radiusRoles"], RADIUS_ROLES, "ui.radiusRoles")
    _require_increasing(
        [_positive_rem(radii[role], f"ui.radiusRoles.{role}") for role in RADIUS_ROLES],
        "ui radius roles",
    )

    elevation = _exact_string_roles(
        ui["elevationRoles"],
        ELEVATION_ROLES,
        "ui.elevationRoles",
    )
    if elevation["none"] != "none" or any(elevation[role] == "none" for role in ELEVATION_ROLES[1:]):
        raise ThemeValidationError(
            "ui.elevationRoles must reserve `none` for the none role"
        )
    if len(set(elevation.values())) != len(ELEVATION_ROLES):
        raise ThemeValidationError("ui.elevationRoles must be unique by semantic depth")

    icons = _exact_string_roles(ui["iconRoles"], ICON_ROLES, "ui.iconRoles")
    _require_increasing(
        [_positive_rem(icons[role], f"ui.iconRoles.{role}") for role in ICON_ROLES],
        "ui icon roles",
    )

    motion = _mapping(ui["motionRoles"], "ui.motionRoles")
    _exact_keys(
        motion,
        set(MOTION_STRING_ROLES) | set(MOTION_MILLISECOND_ROLES),
        "ui.motionRoles",
    )
    state_duration = _positive_milliseconds(
        motion["stateDuration"],
        "ui.motionRoles.stateDuration",
    )
    floating_duration = _positive_milliseconds(
        motion["floatingDuration"],
        "ui.motionRoles.floatingDuration",
    )
    if floating_duration > state_duration:
        raise ThemeValidationError(
            "ui.motionRoles.floatingDuration must not exceed stateDuration"
        )
    _cubic_bezier(motion["easing"], "ui.motionRoles.easing")
    for role in MOTION_MILLISECOND_ROLES:
        _nonnegative_integer(motion[role], f"ui.motionRoles.{role}")
    for timing_role in ("content", "context"):
        delay_role = f"{timing_role}DelayMs"
        minimum_role = f"{timing_role}MinimumMs"
        if motion[minimum_role] < motion[delay_role]:
            raise ThemeValidationError(
                f"ui.motionRoles.{minimum_role} must not be smaller than {delay_role}"
            )

    layers = _mapping(ui["layerRoles"], "ui.layerRoles")
    _exact_keys(layers, set(LAYER_ROLES), "ui.layerRoles")
    for role in LAYER_ROLES:
        _nonnegative_integer(layers[role], f"ui.layerRoles.{role}")
    layer_values = [layers[role] for role in LAYER_ROLES]
    if layer_values != sorted(set(layer_values)):
        raise ThemeValidationError("ui.layerRoles must be unique and increase by semantic depth")

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
        for foreground, background in TEXT_CONTRAST_PAIRS:
            contrast = _contrast_ratio(scheme[foreground], scheme[background])
            if contrast < 4.5:
                raise ThemeValidationError(
                    f"colors.{scheme_name}.{foreground} on {background} must have at least 4.5:1 contrast"
                )
        if _contrast_ratio(scheme["ring"], scheme["background"]) < 3:
            raise ThemeValidationError(
                f"colors.{scheme_name}.ring on background must have at least 3:1 contrast"
            )

    return theme


def _render_web_theme(theme: dict[str, Any]) -> str:
    typography = theme["typography"]["web"]
    colors = theme["colors"]
    ui = theme["ui"]
    type_roles = ui["typographyRoles"]
    spacing_roles = ui["spacingRoles"]
    density_roles = ui["densityRoles"]
    radius_roles = ui["radiusRoles"]
    elevation_roles = ui["elevationRoles"]
    icon_roles = ui["iconRoles"]
    motion_roles = ui["motionRoles"]
    layer_roles = ui["layerRoles"]
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
    for role in TYPOGRAPHY_ROLES:
        token = _camel_to_kebab(role)
        definition = type_roles[role]
        lines.extend(
            (
                f"  --text-axis-{token}: {definition['size']};",
                f"  --text-axis-{token}--line-height: {definition['lineHeight']};",
                f"  --font-weight-axis-{token}: {definition['weight']};",
            )
        )
    for role in SPACING_ROLES:
        lines.append(f"  --spacing-axis-{_camel_to_kebab(role)}: {spacing_roles[role]};")
    for role in DENSITY_ROLES:
        lines.append(f"  --spacing-axis-{_camel_to_kebab(role)}: {density_roles[role]};")
    for role in ICON_ROLES:
        lines.append(f"  --spacing-axis-icon-{_camel_to_kebab(role)}: {icon_roles[role]};")
    for role in RADIUS_ROLES:
        lines.append(f"  --radius-axis-{_camel_to_kebab(role)}: {radius_roles[role]};")
    for role in ELEVATION_ROLES:
        lines.append(f"  --shadow-axis-{_camel_to_kebab(role)}: {elevation_roles[role]};")
    for role in LAYER_ROLES:
        lines.append(f"  --z-axis-{_camel_to_kebab(role)}: {layer_roles[role]};")
    lines.extend(
        (
            f"  --transition-duration-axis-state: {motion_roles['stateDuration']};",
            f"  --transition-duration-axis-floating: {motion_roles['floatingDuration']};",
            f"  --ease-axis-state: {motion_roles['easing']};",
            f"  --default-transition-duration: {motion_roles['stateDuration']};",
            f"  --default-transition-timing-function: {motion_roles['easing']};",
            "  --radius-sm: var(--radius-axis-flat);",
            "  --radius-md: var(--radius-axis-control);",
            "  --radius-lg: var(--radius-axis-control);",
            "  --radius-xl: var(--radius-axis-floating);",
            "  --radius-2xl: var(--radius-axis-managed);",
            "  --radius-3xl: var(--radius-axis-managed);",
            "  --radius-4xl: var(--radius-axis-managed);",
            "}",
            "",
        )
    )
    for selector, scheme_name in ((":root", "light"), (".dark", "dark")):
        lines.append(f"{selector} {{")
        scheme = colors[scheme_name]
        lines.extend(f"  --{token}: {scheme[token]};" for token in REQUIRED_COLOR_TOKENS)
        lines.append(f"  --radius: {radius_roles['control']};")
        lines.extend(("}", ""))
    return "\n".join(lines)


def _camel_to_kebab(value: str) -> str:
    return re.sub(r"(?<!^)(?=[A-Z])", "-", value).lower()


def _require_axis_style_role_mappings() -> None:
    exact_mappings = (
        ("typography", AXIS_STYLE_TYPOGRAPHY_ROLE_MAP, TYPOGRAPHY_ROLES),
        ("density", AXIS_STYLE_DENSITY_ROLE_MAP, DENSITY_ROLES),
        ("icon", AXIS_STYLE_ICON_ROLE_MAP, ICON_ROLES),
        ("radius", AXIS_STYLE_RADIUS_ROLE_MAP, RADIUS_ROLES),
        ("elevation", AXIS_STYLE_ELEVATION_ROLE_MAP, ELEVATION_ROLES),
        ("layer", AXIS_STYLE_LAYER_ROLE_MAP, LAYER_ROLES),
        ("motion", AXIS_STYLE_MOTION_ROLE_MAP, MOTION_STRING_ROLES),
    )
    for label, mapping, canonical_roles in exact_mappings:
        if set(mapping.values()) != set(canonical_roles):
            raise ThemeValidationError(
                f"axisStyles {label} role mapping must match canonical roles"
            )

    projected_spacing_roles = set(AXIS_STYLE_SPACING_ROLE_MAP.values())
    canonical_spacing_roles = set(SPACING_ROLES)
    if projected_spacing_roles != canonical_spacing_roles - {"section"}:
        raise ThemeValidationError(
            "axisStyles spacing role mapping must match canonical consumer roles"
        )


def _axis_utility(prefix: str, role: str) -> str:
    return f"{prefix}-axis-{_camel_to_kebab(role)}"


def _responsive(variant: str, utility: str) -> str:
    return f"{variant}:{utility}"


def _motion_duration_utility(api_role: str) -> str:
    canonical_role = AXIS_STYLE_MOTION_ROLE_MAP[api_role]
    suffix = "Duration"
    if not canonical_role.endswith(suffix):
        raise ThemeValidationError(
            f"axisStyles motion duration role {canonical_role} must end in {suffix}"
        )
    return _axis_utility("duration", canonical_role.removesuffix(suffix))


def _axis_style_projection() -> dict[str, Any]:
    _require_axis_style_role_mappings()
    typography_scale = {
        key: _axis_utility("text", role)
        for key, role in AXIS_STYLE_TYPOGRAPHY_ROLE_MAP.items()
    }
    typography_weight = {
        key: _axis_utility("font", role)
        for key, role in AXIS_STYLE_TYPOGRAPHY_ROLE_MAP.items()
    }
    spacing = AXIS_STYLE_SPACING_ROLE_MAP
    density = AXIS_STYLE_DENSITY_ROLE_MAP
    return {
        "typography": {
            "scale": typography_scale,
            "weight": typography_weight,
        },
        "spacing": {
            "gap": {
                "inline": _axis_utility("gap", spacing["inline"]),
                "region": _axis_utility("gap", spacing["region"]),
                "regionAtMedium": _responsive(
                    "md", _axis_utility("gap", spacing["region"])
                ),
            },
            "padding": {
                "all": {
                    "region": _axis_utility("p", spacing["region"]),
                    "pageCompact": _axis_utility("p", spacing["pageCompact"]),
                    "pageDefaultAtSmall": _responsive(
                        "sm", _axis_utility("p", spacing["pageDefaultAtSmall"])
                    ),
                    "pageWideAtLarge": _responsive(
                        "lg", _axis_utility("p", spacing["pageWideAtLarge"])
                    ),
                },
                "inline": {
                    "inline": _axis_utility("px", spacing["inline"]),
                    "pageCompact": _axis_utility("px", spacing["pageCompact"]),
                    "pageDefaultAtSmall": _responsive(
                        "sm", _axis_utility("px", spacing["pageDefaultAtSmall"])
                    ),
                    "pageWideAtLarge": _responsive(
                        "lg", _axis_utility("px", spacing["pageWideAtLarge"])
                    ),
                },
                "block": {
                    "inline": _axis_utility("py", spacing["inline"]),
                    "region": _axis_utility("py", spacing["region"]),
                    "regionAtMedium": _responsive(
                        "md", _axis_utility("py", spacing["region"])
                    ),
                },
                "bottom": {
                    "inline": _axis_utility("pb", spacing["inline"]),
                },
            },
        },
        "density": {
            "minHeight": {
                "touchTarget": _axis_utility("min-h", density["touchTarget"]),
                "defaultControl": _axis_utility("min-h", density["defaultControl"]),
                "compactControlAtSmall": _responsive(
                    "sm", _axis_utility("min-h", density["compactControlAtSmall"])
                ),
            },
            "minWidth": {
                "touchTarget": _axis_utility("min-w", density["touchTarget"]),
                "compactControlAtSmall": _responsive(
                    "sm", _axis_utility("min-w", density["compactControlAtSmall"])
                ),
            },
        },
        "icon": {
            "size": {
                key: f"size-axis-icon-{_camel_to_kebab(role)}"
                for key, role in AXIS_STYLE_ICON_ROLE_MAP.items()
            },
        },
        "radius": {
            key: _axis_utility("rounded", role)
            for key, role in AXIS_STYLE_RADIUS_ROLE_MAP.items()
        },
        "elevation": {
            key: _axis_utility("shadow", role)
            for key, role in AXIS_STYLE_ELEVATION_ROLE_MAP.items()
        },
        "layer": {
            key: _axis_utility("z", role)
            for key, role in AXIS_STYLE_LAYER_ROLE_MAP.items()
        },
        "motion": {
            "duration": {
                "state": _motion_duration_utility("state"),
                "floating": _motion_duration_utility("floating"),
            },
            "easing": {
                "state": _axis_utility("ease", "state"),
            },
        },
    }


def _render_typescript_object(value: dict[str, Any], level: int = 0) -> list[str]:
    indentation = "  " * level
    lines = ["{"]
    for key, item in value.items():
        item_indentation = "  " * (level + 1)
        if isinstance(item, dict):
            rendered = _render_typescript_object(item, level + 1)
            lines.append(f"{item_indentation}{key}: {rendered[0]}")
            lines.extend(rendered[1:-1])
            lines.append(f"{rendered[-1]},")
        else:
            lines.append(f"{item_indentation}{key}: '{item}',")
    lines.append(f"{indentation}}}")
    return lines


def _render_typescript_string_array(
    values: tuple[str, ...], *, indentation: int = 0, multiline: bool = False
) -> str:
    if not multiline:
        return "[" + ", ".join(f"'{value}'" for value in values) + "]"
    outer_indent = " " * indentation
    item_indent = " " * (indentation + 2)
    items = "\n".join(f"{item_indent}'{value}'," for value in values)
    return f"[\n{items}\n{outer_indent}]"


def _axis_tailwind_merge_theme() -> dict[str, tuple[str, ...]]:
    def axis_role(role: str) -> str:
        return f"axis-{_camel_to_kebab(role)}"

    return {
        "text": tuple(axis_role(role) for role in TYPOGRAPHY_ROLES),
        "font-weight": tuple(axis_role(role) for role in TYPOGRAPHY_ROLES),
        "spacing": (
            *(axis_role(role) for role in SPACING_ROLES),
            *(axis_role(role) for role in DENSITY_ROLES),
            *(f"axis-icon-{_camel_to_kebab(role)}" for role in ICON_ROLES),
        ),
        "radius": tuple(axis_role(role) for role in RADIUS_ROLES),
        "shadow": tuple(axis_role(role) for role in ELEVATION_ROLES),
        "ease": ("axis-state",),
    }


def _render_web_theme_runtime(theme: dict[str, Any]) -> str:
    motion = theme["ui"]["motionRoles"]
    lines = [
            "// <auto-generated />",
            "// Generated from theme/axis-theme.json by `python scripts/axis.py generate theme`.",
            "",
            "export const axisUiTiming = {",
            "  content: {",
            f"    delayMs: {motion['contentDelayMs']},",
            f"    minimumMs: {motion['contentMinimumMs']},",
            "  },",
            "  contextTransition: {",
            f"    delayMs: {motion['contextDelayMs']},",
            f"    minimumMs: {motion['contextMinimumMs']},",
            "  },",
            "} as const;",
            "",
    ]
    rendered_styles = _render_typescript_object(_axis_style_projection())
    lines.append(f"export const axisStyles = {rendered_styles[0]}")
    lines.extend(rendered_styles[1:-1])
    lines.extend((f"{rendered_styles[-1]} as const;", ""))

    merge_theme = _axis_tailwind_merge_theme()
    lines.extend(
        (
            "export const axisTailwindMergeExtension = {",
            "  extend: {",
            "    theme: {",
            f"      text: {_render_typescript_string_array(merge_theme['text'], indentation=6, multiline=True)},",
            f"      'font-weight': {_render_typescript_string_array(merge_theme['font-weight'], indentation=6, multiline=True)},",
            f"      spacing: {_render_typescript_string_array(merge_theme['spacing'], indentation=6, multiline=True)},",
            f"      radius: {_render_typescript_string_array(merge_theme['radius'])},",
            f"      shadow: {_render_typescript_string_array(merge_theme['shadow'])},",
            f"      ease: {_render_typescript_string_array(merge_theme['ease'])},",
            "    },",
            "    classGroups: {",
            "      z: [",
            "        {",
            "          z: [",
            "            'axis-base',",
            "            'axis-sticky',",
            "            'axis-floating',",
            "            'axis-modal',",
            "            'axis-managed',",
            "            'axis-notification',",
            "          ],",
            "        },",
            "      ],",
            "      duration: [{ duration: ['axis-state', 'axis-floating'] }],",
            "    },",
            "  },",
            "} as const;",
            "",
        )
    )
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


def _relative_luminance(rgb: tuple[float, float, float]) -> float:
    linear = tuple(
        channel / 12.92 if channel <= 0.04045 else ((channel + 0.055) / 1.055) ** 2.4
        for channel in rgb
    )
    return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]


def _contrast_ratio(foreground: str, background: str) -> float:
    values = sorted(
        (
            _relative_luminance(_oklch_to_srgb(foreground)),
            _relative_luminance(_oklch_to_srgb(background)),
        ),
        reverse=True,
    )
    return (values[0] + 0.05) / (values[1] + 0.05)


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
        WEB_THEME_RUNTIME_OUTPUT: _render_web_theme_runtime(theme),
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
        str(WEB_THEME_RUNTIME_OUTPUT),
        str(EMAIL_THEME_OUTPUT),
        "frontend/src/index.css",
        "frontend/scripts/check-axis-style-consumption.mjs",
        "frontend/scripts/check-axis-style-consumption.test.mjs",
        "scripts/axis_theme.py",
        "scripts/tests/test_axis_theme.py",
    }
