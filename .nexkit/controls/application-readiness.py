"""Check the accepted prerequisites before reserving application work."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


def check(root: Path, pipeline: str) -> list[str]:
    project = json.loads((root / ".nexkit/project.json").read_text())
    policy_path = ".nexkit/controls/application-readiness.json"
    policy = json.loads((root / policy_path).read_text())
    problems = []

    for name, record in project["files"].items():
        path = root / name
        if path.is_symlink() or not path.is_file():
            problems.append(f"Accepted file is missing or a symlink: {name}")
        elif hashlib.sha256(path.read_bytes()).hexdigest() != record["sha256"]:
            problems.append(f"Accepted file hash changed: {name}")

    if policy.get("schema") != 1 or policy.get("enabled") is not True:
        problems.append(policy.get("reason", "Application work has not been enabled."))
        return problems

    settings = dict(project["defaults"])
    settings.update(project["pipelines"][pipeline]["settings"])
    if settings.get("application") != "present":
        problems.append("No application is declared present.")

    architecture = policy.get("architecture")
    if not isinstance(architecture, str) or architecture not in settings.get("knowledge", []):
        problems.append("Declare the accepted architecture in the pipeline's knowledge.")
    elif not (root / architecture).is_file() or not (root / architecture).read_text().strip():
        problems.append("The accepted architecture document is missing or empty.")

    checks = settings.get("checks", [])
    if not {"test", "e2e"} <= {value.get("kind") for value in checks}:
        problems.append("Real application test and E2E commands are required.")
    if pipeline == "release":
        release = settings.get("release", {})
        if release.get("enabled") is not True or not release.get("build") or not release.get("artifacts"):
            problems.append("Release build and artifact paths have not been configured.")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pipeline", required=True, choices=("delivery", "release"))
    parser.add_argument("--root", type=Path, default=Path.cwd())
    args = parser.parse_args()
    problems = check(args.root.resolve(), args.pipeline)
    if problems:
        print(f"Axis {args.pipeline} is blocked before reserving work:")
        for problem in problems:
            print(f"- {problem}")
        return 2
    print(f"Axis {args.pipeline} prerequisites are configured; native checks still must pass.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
