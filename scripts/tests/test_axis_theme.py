"""Regression tests for canonical Axis theme generation."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis_theme  # noqa: E402


class TestAxisTheme(unittest.TestCase):
    def definition(self) -> dict[str, object]:
        light = {
            token: "oklch(0.5 0.1 180)"
            for token in axis_theme.REQUIRED_COLOR_TOKENS
        }
        dark = dict(light)
        light.update(
            {
                "background": "oklch(1 0 0)",
                "card": "oklch(1 0 0)",
                "primary": "oklch(1 0 0)",
                "primary-foreground": "oklch(0 0 0)",
            }
        )
        return {
            "schemaVersion": 1,
            "typography": {
                "web": {
                    "sans": '"Geist Variable", sans-serif',
                    "heading": '"Be Vietnam Pro", sans-serif',
                },
                "email": {
                    "sans": "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,Helvetica,sans-serif",
                },
            },
            "radius": "0.5rem",
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

    def test_render_theme_artifacts_is_deterministic_and_projects_email_safe_values(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_source(root)

            first = axis_theme.render_theme_artifacts(root)
            second = axis_theme.render_theme_artifacts(root)

            self.assertEqual(first, second)
            web = first[axis_theme.WEB_THEME_OUTPUT]
            email = first[axis_theme.EMAIL_THEME_OUTPUT]
            self.assertIn("Generated from theme/axis-theme.json", web)
            self.assertIn(":root {", web)
            self.assertIn(".dark {", web)
            self.assertIn("--primary: oklch(1 0 0);", web)
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


if __name__ == "__main__":
    unittest.main()
