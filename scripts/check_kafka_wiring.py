#!/usr/bin/env python3
"""Verify Axis.Api startup wiring references every *KafkaTopics.cs constant (ADR-019 / ADR-025)."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

_SCRIPTS_DIR = Path(__file__).resolve().parent
if str(_SCRIPTS_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPTS_DIR))

from axis_repo import MODULES_DIR, ROOT, pascal_to_kebab

KAFKA_TOPIC_CONST_RE = re.compile(
    r"public\s+const\s+string\s+(\w+)\s*=\s*\"(axis\.[^\"]+)\";",
    re.MULTILINE,
)
AXIS_API_DIR = ROOT / "src" / "Axis.Api"


def iter_kafka_topic_constants() -> list[tuple[str, str, str]]:
    """(class_name, const_name, topic_value) for each KafkaTopics constant."""
    rows: list[tuple[str, str, str]] = []
    for topics_file in sorted(MODULES_DIR.glob("*/Axis.*.Contracts/*KafkaTopics.cs")):
        class_name = topics_file.stem
        text = topics_file.read_text(encoding="utf-8")
        for const_name, topic in KAFKA_TOPIC_CONST_RE.findall(text):
            rows.append((class_name, const_name, topic))
    return rows


def expected_topic_from_avsc_path(path: str) -> str:
    m = re.search(r"src/Modules/([^/]+)/.*/Schemas/(\w+)Event\.avsc$", path.replace("\\", "/"))
    if not m:
        raise ValueError(path)
    module_name, event_stem = m.group(1), m.group(2)
    return f"axis.{module_name.lower()}.{pascal_to_kebab(event_stem)}"


def read_axis_api_source() -> str:
    if not AXIS_API_DIR.is_dir():
        raise FileNotFoundError(AXIS_API_DIR)

    return "\n".join(
        path.read_text(encoding="utf-8")
        for path in sorted(AXIS_API_DIR.glob("**/*.cs"))
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="validate and exit non-zero on drift")
    args = parser.parse_args()
    if not args.check:
        parser.print_help()
        return 2

    if not AXIS_API_DIR.is_dir():
        print("check-kafka-wiring FAIL: src/Axis.Api missing", file=sys.stderr)
        return 1

    axis_api_source = read_axis_api_source()
    issues: list[str] = []
    constants = iter_kafka_topic_constants()
    topic_values = {topic for _, _, topic in constants}

    for class_name, const_name, topic in constants:
        ref = f"{class_name}.{const_name}"
        if ref not in axis_api_source:
            issues.append(
                f"Axis.Api startup wiring missing {ref} (topic {topic!r}) in "
                f"PublishAndListenWithAvro / PublishLocally"
            )

    for avsc in sorted(ROOT.glob("src/Modules/**/Schemas/*Event.avsc")):
        rel = str(avsc.relative_to(ROOT)).replace("\\", "/")
        expected = expected_topic_from_avsc_path(rel)
        if expected not in topic_values:
            issues.append(f"{rel}: no *KafkaTopics.cs constant for topic {expected!r}")

    if issues:
        print("check-kafka-wiring FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    print(f"check-kafka-wiring: OK ({len(constants)} KafkaTopics constants wired)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
