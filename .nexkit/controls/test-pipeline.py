"""Exercise rejected workflow configurations without invoking an application or model."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

import yaml

ROOT = Path.cwd()
KIT = None


class PipelineBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="axis-pipeline-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = json.loads((ROOT / ".nexkit/project.json").read_text())
        for name in self.project["files"]:
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / name, target)
        for name in self.project["defaults"]["knowledge"]:
            if (ROOT / name).is_file():
                target = self.root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ROOT / name, target)
        self.write_project()

    def write_project(self):
        path = self.root / ".nexkit/project.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(self.project))

    def replace_control(self, name, text):
        (self.root / name).write_text(text)
        self.project["files"][name]["sha256"] = hashlib.sha256(text.encode()).hexdigest()
        self.write_project()

    def validation(self):
        return subprocess.run([
            sys.executable, str(self.root / ".nexkit/controls/check-pipeline.py"),
            "--root", str(self.root), "--kit", str(KIT),
        ], capture_output=True, text=True)

    def mutate_delivery(self, mutate):
        name = ".github/workflows/nexkit-delivery.yml"
        workflow = yaml.load((self.root / name).read_text(), Loader=yaml.BaseLoader)
        mutate(workflow["jobs"])
        self.replace_control(name, yaml.safe_dump(workflow, sort_keys=False))

    def assert_rejected(self, result, message):
        self.assertNotEqual(result.returncode, 0)
        self.assertIn(message, result.stdout + result.stderr)

    def test_candidate_matches_accepted_controls(self):
        result = self.validation()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_unaccepted_control_change_is_rejected(self):
        name = ".nexkit/controls/delivery-plan.md"
        with (self.root / name).open("a") as stream:
            stream.write("Unexpected control change.\n")
        self.assert_rejected(self.validation(), "Hash drift")

    def test_missing_e2e_dependency_is_rejected(self):
        self.mutate_delivery(lambda jobs: jobs["review"]["needs"].remove("e2e"))
        self.assert_rejected(self.validation(), "Review must consume all candidate checks")

    def test_failed_check_cannot_be_ignored_by_finalizer(self):
        def mutate(jobs):
            value = jobs["finish"]["with"]["jobs_succeeded"]
            jobs["finish"]["with"]["jobs_succeeded"] = value.replace("needs.e2e.outputs.passed == 'true'", "true")
        self.mutate_delivery(mutate)
        self.assert_rejected(self.validation(), "Finalizer omits e2e's actual verdict")

    def test_human_review_cannot_be_disabled(self):
        self.project["pipelines"]["delivery"]["approvals"]["pr-review"]["enabled"] = False
        self.write_project()
        self.assert_rejected(self.validation(), "Require native PR review")

    def test_resume_cannot_replay_an_editor(self):
        name = ".github/workflows/nexkit-pr-continuation.yml"
        workflow = yaml.load((self.root / name).read_text(), Loader=yaml.BaseLoader)
        workflow["jobs"]["implement"] = dict(workflow["jobs"]["finish"])
        self.replace_control(name, yaml.safe_dump(workflow, sort_keys=False))
        self.assert_rejected(self.validation(), "PR approval resumes without replaying")

    def test_deferred_application_work_is_blocked(self):
        policy = json.loads((self.root / ".nexkit/controls/application-readiness.json").read_text())
        if policy["enabled"]:
            self.skipTest("This installation has enabled application delivery.")
        for pipeline in ("delivery", "release"):
            result = subprocess.run([
                sys.executable, str(self.root / ".nexkit/controls/application-readiness.py"),
                "--root", str(self.root), "--pipeline", pipeline,
            ], capture_output=True, text=True)
            self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
            self.assertIn("blocked before reserving work", result.stdout)

    def test_enable_flag_without_application_checks_is_rejected(self):
        policy_name = ".nexkit/controls/application-readiness.json"
        policy = json.loads((self.root / policy_name).read_text())
        policy["enabled"] = True
        self.replace_control(policy_name, json.dumps(policy))
        self.project["pipelines"]["delivery"]["settings"]["application"] = "absent"
        self.project["pipelines"]["delivery"]["settings"]["checks"] = []
        self.write_project()
        result = subprocess.run([
            sys.executable, str(self.root / ".nexkit/controls/application-readiness.py"),
            "--root", str(self.root), "--pipeline", "delivery",
        ], capture_output=True, text=True)
        self.assert_rejected(result, "Real application test and E2E commands are required")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--kit", type=Path, required=True)
    args = parser.parse_args()
    ROOT, KIT = args.root.resolve(), args.kit.resolve()
    unittest.main(argv=[sys.argv[0]], verbosity=2)
