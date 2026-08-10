"""Regression tests for canonical Axis theme generation."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis_theme  # noqa: E402


class TestAxisTheme(unittest.TestCase):
    def definition(self) -> dict[str, object]:
        light = {token: "oklch(1 0 0)" for token in axis_theme.REQUIRED_COLOR_TOKENS}
        dark = {token: "oklch(0 0 0)" for token in axis_theme.REQUIRED_COLOR_TOKENS}
        foreground_tokens = {
            "foreground",
            "card-foreground",
            "popover-foreground",
            "primary-foreground",
            "secondary-foreground",
            "muted-foreground",
            "accent-foreground",
            "destructive",
            "info-foreground",
            "success-foreground",
            "warning-foreground",
            "ring",
            "sidebar-foreground",
            "sidebar-primary-foreground",
            "sidebar-accent-foreground",
        }
        light.update({token: "oklch(0 0 0)" for token in foreground_tokens})
        dark.update({token: "oklch(1 0 0)" for token in foreground_tokens})
        return {
            "schemaVersion": 2,
            "typography": {
                "web": {
                    "sans": '"Geist Variable", sans-serif',
                    "heading": '"Be Vietnam Pro", sans-serif',
                },
                "email": {
                    "sans": "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,Helvetica,sans-serif",
                },
            },
            "ui": {
                "typographyRoles": {
                    "metadata": {"size": "0.75rem", "lineHeight": "1rem", "weight": 400},
                    "body": {"size": "0.875rem", "lineHeight": "1.5rem", "weight": 400},
                    "label": {"size": "0.875rem", "lineHeight": "1.25rem", "weight": 500},
                    "componentTitle": {"size": "1rem", "lineHeight": "1.5rem", "weight": 500},
                    "sectionTitle": {"size": "1.125rem", "lineHeight": "1.75rem", "weight": 500},
                    "pageTitle": {"size": "1.5rem", "lineHeight": "2rem", "weight": 600},
                },
                "spacingRoles": {
                    "inline": "0.5rem",
                    "region": "1rem",
                    "section": "1.5rem",
                    "pageCompact": "1rem",
                    "pageDefault": "1.5rem",
                    "pageWide": "2rem",
                },
                "densityRoles": {
                    "compactControl": "2rem",
                    "defaultControl": "2.5rem",
                    "touchTarget": "2.75rem",
                },
                "radiusRoles": {
                    "flat": "0.25rem",
                    "control": "0.5rem",
                    "floating": "0.75rem",
                    "managed": "1rem",
                },
                "elevationRoles": {
                    "none": "none",
                    "floating": "0 4px 6px rgb(0 0 0 / 0.1)",
                    "managed": "0 10px 15px rgb(0 0 0 / 0.1)",
                    "dock": "0 20px 25px rgb(0 0 0 / 0.1)",
                },
                "iconRoles": {
                    "control": "1rem",
                    "navigation": "1.25rem",
                    "empty": "1.5rem",
                },
                "motionRoles": {
                    "stateDuration": "150ms",
                    "floatingDuration": "100ms",
                    "easing": "cubic-bezier(0, 0, 0.2, 1)",
                    "feedbackDelayMs": 300,
                    "feedbackMinimumMs": 400,
                    "contextDelayMs": 500,
                    "contextMinimumMs": 600,
                },
                "layerRoles": {
                    "base": 0,
                    "sticky": 10,
                    "floating": 30,
                    "modal": 40,
                    "managed": 50,
                    "notification": 60,
                },
            },
            "colors": {"light": light, "dark": dark},
        }

    def write_source(self, root: Path, definition: dict[str, object] | None = None) -> None:
        source = root / axis_theme.THEME_SOURCE
        source.parent.mkdir(parents=True, exist_ok=True)
        source.write_text(
            f"{json.dumps(definition or self.definition(), indent=2)}\n",
            encoding="utf-8",
        )

    def test_load_theme_rejects_missing_or_unknown_semantic_tokens(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            definition = self.definition()
            colors = definition["colors"]
            assert isinstance(colors, dict)
            dark = colors["dark"]
            assert isinstance(dark, dict)
            dark.pop("foreground")
            dark["invented"] = "oklch(0.5 0.1 180)"
            self.write_source(root, definition)

            with self.assertRaisesRegex(
                axis_theme.ThemeValidationError,
                "dark colors must contain exactly the required semantic tokens",
            ):
                axis_theme.load_theme(root)

    def test_load_theme_rejects_non_finite_oklch_components(self) -> None:
        huge_number = "9" * 400
        components = {
            "lightness": f"oklch({huge_number} 0.1 180)",
            "chroma": f"oklch(0.5 {huge_number} 180)",
            "hue": f"oklch(0.5 0.1 {huge_number})",
        }

        for component, color in components.items():
            with self.subTest(component=component), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                definition = self.definition()
                colors = definition["colors"]
                assert isinstance(colors, dict)
                light = colors["light"]
                assert isinstance(light, dict)
                light["primary"] = color
                self.write_source(root, definition)

                with self.assertRaisesRegex(
                    axis_theme.ThemeValidationError,
                    f"primary {component} must be finite",
                ):
                    axis_theme.load_theme(root)

    def test_load_theme_rejects_missing_or_invalid_ui_roles(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            definition = self.definition()
            ui = definition["ui"]
            assert isinstance(ui, dict)
            spacing = ui["spacingRoles"]
            assert isinstance(spacing, dict)
            spacing.pop("section")
            layers = ui["layerRoles"]
            assert isinstance(layers, dict)
            layers["notification"] = 40
            self.write_source(root, definition)

            with self.assertRaisesRegex(
                axis_theme.ThemeValidationError,
                "ui.spacingRoles must contain exactly the required keys",
            ):
                axis_theme.load_theme(root)

    def test_load_theme_rejects_invalid_semantic_values_and_order(self) -> None:
        invalid_cases = (
            ("spacingRoles", "inline", "8px", "positive `rem` value"),
            ("densityRoles", "compactControl", "3rem", "increase by semantic depth"),
            ("radiusRoles", "control", "0.1rem", "increase by semantic depth"),
            ("motionRoles", "floatingDuration", "200ms", "must not exceed stateDuration"),
            ("motionRoles", "easing", "linear", "must use `cubic-bezier"),
            ("motionRoles", "feedbackMinimumMs", 200, "must not be smaller"),
        )
        for group, role, value, message in invalid_cases:
            with self.subTest(group=group, role=role), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                definition = self.definition()
                ui = definition["ui"]
                assert isinstance(ui, dict)
                values = ui[group]
                assert isinstance(values, dict)
                values[role] = value
                self.write_source(root, definition)

                with self.assertRaisesRegex(axis_theme.ThemeValidationError, message):
                    axis_theme.load_theme(root)

    def test_load_theme_rejects_insufficient_text_and_focus_contrast(self) -> None:
        invalid_cases = (
            ("foreground", "background", "at least 4.5:1 contrast"),
            ("ring", "background", "at least 3:1 contrast"),
        )
        for token, copied_from, message in invalid_cases:
            with self.subTest(token=token), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                definition = self.definition()
                colors = definition["colors"]
                assert isinstance(colors, dict)
                light = colors["light"]
                assert isinstance(light, dict)
                light[token] = light[copied_from]
                self.write_source(root, definition)

                with self.assertRaisesRegex(axis_theme.ThemeValidationError, message):
                    axis_theme.load_theme(root)

    def test_render_theme_artifacts_is_deterministic_and_projects_email_safe_values(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_source(root)

            first = axis_theme.render_theme_artifacts(root)
            second = axis_theme.render_theme_artifacts(root)

            self.assertEqual(first, second)
            web = first[axis_theme.WEB_THEME_OUTPUT]
            runtime = first[axis_theme.WEB_THEME_RUNTIME_OUTPUT]
            email = first[axis_theme.EMAIL_THEME_OUTPUT]
            self.assertIn("Generated from theme/axis-theme.json", web)
            self.assertIn(":root {", web)
            self.assertIn(".dark {", web)
            self.assertIn("--primary: oklch(1 0 0);", web)
            self.assertIn("--text-axis-page-title: 1.5rem;", web)
            self.assertIn("--spacing-axis-touch-target: 2.75rem;", web)
            self.assertIn("--radius-axis-managed: 1rem;", web)
            self.assertIn("--z-axis-notification: 60;", web)
            self.assertIn("delayMs: 300", runtime)
            self.assertIn("minimumMs: 600", runtime)
            self.assertIn('PrimaryColor = "#ffffff"', email)
            self.assertIn('PrimaryForegroundColor = "#000000"', email)
            self.assertIn("Segoe UI", email)

    def test_theme_artifact_issues_reports_missing_and_stale_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_source(root)

            self.assertTrue(
                any("missing generated theme artifact" in issue for issue in axis_theme.theme_artifact_issues(root))
            )

            axis_theme.write_theme_artifacts(root)
            self.assertEqual([], axis_theme.theme_artifact_issues(root))

            web_output = root / axis_theme.WEB_THEME_OUTPUT
            web_output.write_text("stale\n", encoding="utf-8")
            self.assertTrue(
                any("stale generated theme artifact" in issue for issue in axis_theme.theme_artifact_issues(root))
            )

    def test_write_theme_artifacts_uses_lf_newlines_when_platform_defaults_translate(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_source(root)

            def translating_write_text(
                path: Path,
                content: str,
                encoding: str | None = None,
                errors: str | None = None,
                newline: str | None = None,
            ) -> int:
                del errors
                rendered = content.replace("\n", "\r\n") if newline is None else content
                return path.write_bytes(rendered.encode(encoding or "utf-8"))

            with mock.patch.object(Path, "write_text", autospec=True, side_effect=translating_write_text):
                written = axis_theme.write_theme_artifacts(root)

            for relative_path in written:
                self.assertNotIn(b"\r\n", (root / relative_path).read_bytes())


if __name__ == "__main__":
    unittest.main()
