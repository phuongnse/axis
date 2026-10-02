"""Validate Axis workflow wiring and accepted controls, not application behavior."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys

import yaml


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def dependencies(job: dict) -> set[str]:
    needs = job.get("needs", [])
    return {needs} if isinstance(needs, str) else set(needs)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--kit", type=Path, required=True)
    args = parser.parse_args()
    root, kit = args.root.resolve(), args.kit.resolve()
    project = json.loads((root / ".nexkit/project.json").read_text())
    kit_sha = subprocess.check_output(["git", "-C", str(kit), "rev-parse", "HEAD"], text=True).strip()
    require(kit_sha == project["kit"]["ref"], "Validation kit differs from the accepted immutable pin.")
    sys.path.insert(0, str(kit))
    from nexkit.policy import config

    config(project)
    require(set(project["pipelines"]) == {"discovery", "delivery", "release"}, "Expected three Axis pipelines.")
    workflows = {}
    for name, record in project["files"].items():
        path = root / name
        require(path.is_file() and not path.is_symlink(), f"Missing accepted file: {name}")
        require(hashlib.sha256(path.read_bytes()).hexdigest() == record["sha256"], f"Hash drift: {name}")
        if name.startswith(".github/workflows/"):
            workflows[name] = yaml.load(path.read_text(), Loader=yaml.BaseLoader)
    for name, workflow in workflows.items():
        require(workflow.get("permissions") == {}, f"Use explicit job permissions: {name}")
        for job in workflow["jobs"].values():
            uses = job.get("uses")
            if uses and uses.startswith("phuongnse/nexkit/"):
                require(uses.endswith("@" + kit_sha), f"Mutable toolkit reference: {name}")
            if uses and uses.startswith("./"):
                require(uses.removeprefix("./") in workflows, f"Unaccepted local workflow: {uses}")

    pipeline = project["pipelines"]["delivery"]
    invocations = pipeline["invocations"]
    require({name: value["contract"] for name, value in invocations.items()} == {
        "plan": "task", "implement": "deliver", "review": "review"
    }, "Delivery needs separate planning, editing and review contracts.")
    for binding in project["pipelines"].values():
        for invocation in binding.get("invocations", {}).values():
            require(invocation["model"] == "gpt-6.1-sol" and invocation["reasoning_effort"] == "max", "Preserve the owner's model and effort.")
    gate = pipeline["approvals"]["pr-review"]
    require(gate["enabled"] and gate["mode"] == "pull_request" and gate["subject"] == "candidate", "Require native PR review of the candidate.")
    require(gate["minimum"] == 1 and gate["reviewers"] == "repository", "Require one current authorized collaborator.")
    require(gate["protects"] == ["merge"] and gate["on_rejection"] == "retry", "PR decisions protect merge and permit bounded repair.")

    delivery = workflows[pipeline["entrypoints"]["delivery"]]
    jobs = delivery["jobs"]
    require(dependencies(jobs["prepare"]) == {"readiness"}, "Check readiness before reserving a round.")
    require(dependencies(jobs["implement"]) == {"prepare", "plan"}, "Implementation needs the reserved round and plan.")
    checks = {"build", "lint", "unit", "integration", "e2e"}
    for name in checks:
        require(dependencies(jobs[name]) == {"publish"}, f"Check {name} must use the published candidate.")
        require(jobs[name]["with"]["candidate_artifact_id"] == "${{ needs.publish.outputs.candidate_artifact_id }}", f"Check {name} is not bound to its producer.")
    require(dependencies(jobs["review"]) == checks | {"publish"}, "Review must consume all candidate checks.")
    require(dependencies(jobs["pr-review"]) == checks | {"publish", "review"}, "Human review follows checks and independent AI review.")
    for name in checks:
        reference = "${{ needs." + name + ".outputs.report_artifact_id }}"
        require(reference in jobs["review"]["with"]["check_artifact_ids"], f"Review omits {name}'s exact report.")
        require(reference in jobs["pr-review"]["with"]["check_artifact_ids"], f"PR checkpoint omits {name}'s report.")
        require(reference in jobs["finish"]["with"]["check_artifact_ids"], f"Finalizer omits {name}'s report.")
        require("needs." + name + ".outputs.passed == 'true'" in jobs["finish"]["with"]["jobs_succeeded"], f"Finalizer omits {name}'s actual verdict.")
    require(dependencies(jobs["finish"]) == set(jobs) - {"finish"}, "Finalizer must depend on every preceding job.")
    require("always()" in jobs["finish"]["if"], "Finalizer must handle failures.")
    continuation = workflows[gate["continuation"]]
    require(continuation["concurrency"] == delivery["concurrency"], "Continuation must share the workflow queue.")
    require(set(continuation["jobs"]) == {"resume", "finish"}, "PR approval resumes without replaying completed agents.")
    require(continuation["jobs"]["finish"]["with"]["checkpoint_evidence"] == "true", "Continuation must revalidate frozen evidence.")
    relay = workflows[".github/workflows/nexkit-pr-review-events.yml"]
    require(set(relay["jobs"]) == {"notify"} and "pull_request_review" in relay["on"], "Receive native PR decisions through the metadata relay.")
    intake = workflows[".github/workflows/nexkit-intake.yml"]
    require("release" in intake["on"]["workflow_dispatch"]["inputs"]["operation"]["options"], "Intake must accept queued release requests.")

    policy = json.loads((root / ".nexkit/controls/application-readiness.json").read_text())
    spec = importlib.util.spec_from_file_location("axis_readiness", root / ".nexkit/controls/application-readiness.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    for name in ("delivery", "release"):
        blocked = module.check(root, name)
        if policy["enabled"]:
            require(not blocked, f"Configured {name} prerequisites failed: {blocked}")
        else:
            require(bool(blocked), f"Deferred {name} must block before reserving work.")
            require(not project["pipelines"][name]["settings"].get("checks"), "Do not declare placeholder application checks.")
    print("Axis pipeline controls, artifact wiring, PR gate and application readiness guard verified.")
    print("This result verifies pipeline structure; application tests and E2E are separate evidence.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
