"""Deterministic frontend source-policy checks used by the Axis CLI."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from acceptance_evidence import at_ids_for_cell
from acceptance_evidence import evidence_file_for
from acceptance_evidence import first_markdown_table
from acceptance_evidence import inline_code_values
from acceptance_evidence import record_for_row
from acceptance_evidence import split_h2_sections
from axis_repo import ROOT, iter_files


UI_FOUNDATION_MANIFEST_KEYS = {
    "schemaVersion",
    "contracts",
    "enforcedContracts",
}
UI_CONTRACT_KEYS = {"state", "spec", "coverageProfile", "acceptance", "evidence"}
UI_CONTRACT_STATES = {"defined", "verified", "enforced"}
UI_ACCEPTANCE_KEYS = {"status"}
UI_ACCEPTANCE_STATES = {"pending", "accepted"}
UI_EVIDENCE_KEYS = {"assessment", "component", "browser", "perceptual", "coverage"}
UI_PERCEPTUAL_KEYS = {"status", "artifacts"}
UI_PERCEPTUAL_STATES = {"missing", "candidate", "accepted"}
UI_COVERAGE_KEYS = {"covered", "gaps", "notApplicable"}
UI_COVERED_REQUIREMENT_KEYS = {"acceptance", "evidence", "modes"}
UI_COVERAGE_PROFILE_KEYS = {
    "schemaVersion",
    "profileId",
    "modes",
    "invalidationTriggers",
    "requirements",
}
UI_PROFILE_REQUIREMENT_KEYS = {
    "category",
    "evidenceKinds",
    "requiredModes",
    "invalidatedBy",
    "allowNotApplicable",
}
UI_PROFILE_CATEGORIES = {"standard", "visual", "behavior", "lifecycle"}
UI_PROFILE_EVIDENCE_KINDS = {
    "assessment",
    "browser",
    "component",
    "perceptual",
    "review",
}
UI_REQUIRED_INVALIDATION_TRIGGERS = {
    "acceptance",
    "constitution",
    "consumer",
    "evidence",
    "surface-owner",
    "theme",
}
UI_REQUIRED_PROFILE_REQUIREMENTS = {
    "lifecycle.consumer-ownership-adoption": {
        "evidenceKinds": {"component"},
        "requiredModes": set(),
        "invalidatedBy": {"consumer", "evidence", "surface-owner"},
    },
    "lifecycle.retirement-compatibility": {
        "evidenceKinds": {"assessment", "component"},
        "requiredModes": set(),
        "invalidatedBy": {"constitution", "consumer", "evidence", "surface-owner"},
    },
    "standard.human-centred-evaluation": {
        "evidenceKinds": {"assessment", "review"},
        "requiredModes": set(),
        "invalidatedBy": UI_REQUIRED_INVALIDATION_TRIGGERS,
    },
    "standard.interaction-principles": {
        "evidenceKinds": {"assessment", "browser", "component", "review"},
        "requiredModes": {"keyboard", "pointer"},
        "invalidatedBy": UI_REQUIRED_INVALIDATION_TRIGGERS,
    },
    "standard.wcag-2-2-aa": {
        "evidenceKinds": {"assessment", "browser", "component", "review"},
        "requiredModes": {
            "compact",
            "desktop",
            "keyboard",
            "locale",
            "reduced-motion",
            "screen-reader",
            "zoom-reflow",
        },
        "invalidatedBy": UI_REQUIRED_INVALIDATION_TRIGGERS,
    },
}
UI_CONTRACT_ID = re.compile(r"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$")
UI_PROFILE_ID = re.compile(r"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*-v[1-9][0-9]*$")
UI_REQUIREMENT_ID = re.compile(
    r"^(?:standard|visual|behavior|lifecycle)[.][a-z][a-z0-9]*(?:-[a-z0-9]+)*$"
)
UI_DIMENSION_ID = re.compile(r"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$")
UI_ACCEPTANCE_REFERENCE = re.compile(
    r"^(?P<path>docs/foundations/[A-Za-z0-9_./-]+[.]md)#(?P<at_id>AT-[0-9]{3})$"
)


def rel(path: Path) -> str:
    return str(path.relative_to(ROOT)).replace("\\", "/")


def ui_foundation_issues(root: Path = ROOT) -> list[str]:
    manifest_path = root / "frontend" / "ui-foundation.json"
    if not manifest_path.is_file():
        return ["frontend/ui-foundation.json: active UI foundation state is missing"]

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as exc:
        return [f"frontend/ui-foundation.json: cannot read active UI foundation state: {exc}"]
    if not isinstance(manifest, dict):
        return ["frontend/ui-foundation.json: root value must be an object"]

    issues: list[str] = []

    def checked_string_list(
        value: object,
        field: str,
        *,
        allowed: set[str] | None = None,
        non_empty: bool = False,
    ) -> list[str]:
        if not isinstance(value, list):
            issues.append(f"{field} must be a list")
            return []
        if any(not isinstance(item, str) or not item for item in value):
            issues.append(f"{field} must contain only non-empty strings")
            return [item for item in value if isinstance(item, str) and item]
        if non_empty and not value:
            issues.append(f"{field} must be non-empty")
        if value != sorted(value):
            issues.append(f"{field} must be sorted")
        if len(value) != len(set(value)):
            issues.append(f"{field} contains duplicates")
        if allowed is not None:
            unknown = sorted(set(value) - allowed)
            if unknown:
                issues.append(f"{field} contains unknown values: {', '.join(unknown)}")
        return value

    profile_path = root / "frontend" / "ui-coverage-profile.json"
    profile: object = None
    if not profile_path.is_file():
        issues.append("frontend/ui-coverage-profile.json: governed UI coverage profile is missing")
    else:
        try:
            profile = json.loads(profile_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError) as exc:
            issues.append(f"frontend/ui-coverage-profile.json: cannot read profile: {exc}")

    profile_id: object = None
    profile_modes: set[str] = set()
    profile_triggers: set[str] = set()
    profile_requirements: dict[str, dict[str, object]] = {}
    if not isinstance(profile, dict):
        issues.append("frontend/ui-coverage-profile.json: root value must be an object")
    else:
        unexpected_profile = sorted(set(profile) - UI_COVERAGE_PROFILE_KEYS)
        missing_profile = sorted(UI_COVERAGE_PROFILE_KEYS - set(profile))
        if unexpected_profile:
            issues.append(
                "frontend/ui-coverage-profile.json: unexpected keys: "
                + ", ".join(unexpected_profile)
            )
        if missing_profile:
            issues.append(
                "frontend/ui-coverage-profile.json: missing keys: "
                + ", ".join(missing_profile)
            )
        if profile.get("schemaVersion") != 1:
            issues.append("frontend/ui-coverage-profile.json: `schemaVersion` must be 1")
        profile_id = profile.get("profileId")
        if not isinstance(profile_id, str) or not UI_PROFILE_ID.fullmatch(profile_id):
            issues.append(
                "frontend/ui-coverage-profile.json: `profileId` must be a versioned kebab-case id"
            )
        modes = checked_string_list(
            profile.get("modes"),
            "frontend/ui-coverage-profile.json: `modes`",
            non_empty=True,
        )
        profile_modes = set(modes)
        for mode in modes:
            if not UI_DIMENSION_ID.fullmatch(mode):
                issues.append(
                    f"frontend/ui-coverage-profile.json: mode `{mode}` must use kebab-case"
                )
        triggers = checked_string_list(
            profile.get("invalidationTriggers"),
            "frontend/ui-coverage-profile.json: `invalidationTriggers`",
            non_empty=True,
        )
        profile_triggers = set(triggers)
        missing_required_triggers = sorted(
            UI_REQUIRED_INVALIDATION_TRIGGERS - profile_triggers
        )
        if missing_required_triggers:
            issues.append(
                "frontend/ui-coverage-profile.json: `invalidationTriggers` is missing "
                "baseline triggers: " + ", ".join(missing_required_triggers)
            )
        for trigger in triggers:
            if not UI_DIMENSION_ID.fullmatch(trigger):
                issues.append(
                    "frontend/ui-coverage-profile.json: invalidation trigger "
                    f"`{trigger}` must use kebab-case"
                )
        requirements = profile.get("requirements")
        if not isinstance(requirements, dict) or not requirements:
            issues.append(
                "frontend/ui-coverage-profile.json: `requirements` must be a non-empty object"
            )
        else:
            if list(requirements) != sorted(requirements):
                issues.append(
                    "frontend/ui-coverage-profile.json: `requirements` must be sorted by id"
                )
            for requirement_id, requirement in requirements.items():
                requirement_prefix = f"requirements.{requirement_id}"
                if (
                    not isinstance(requirement_id, str)
                    or not UI_REQUIREMENT_ID.fullmatch(requirement_id)
                ):
                    issues.append(
                        "frontend/ui-coverage-profile.json: requirement id "
                        f"`{requirement_id}` must use `<category>.<kebab-case>`"
                    )
                if not isinstance(requirement, dict):
                    issues.append(
                        f"frontend/ui-coverage-profile.json: `{requirement_prefix}` must be an object"
                    )
                    continue
                profile_requirements[requirement_id] = requirement
                unexpected_requirement = sorted(
                    set(requirement) - UI_PROFILE_REQUIREMENT_KEYS
                )
                missing_requirement = sorted(
                    UI_PROFILE_REQUIREMENT_KEYS - set(requirement)
                )
                if unexpected_requirement:
                    issues.append(
                        f"frontend/ui-coverage-profile.json: `{requirement_prefix}` has unexpected keys: "
                        + ", ".join(unexpected_requirement)
                    )
                if missing_requirement:
                    issues.append(
                        f"frontend/ui-coverage-profile.json: `{requirement_prefix}` is missing keys: "
                        + ", ".join(missing_requirement)
                    )
                category = requirement.get("category")
                if category not in UI_PROFILE_CATEGORIES:
                    issues.append(
                        f"frontend/ui-coverage-profile.json: `{requirement_prefix}.category` "
                        "must be one of: " + ", ".join(sorted(UI_PROFILE_CATEGORIES))
                    )
                elif isinstance(requirement_id, str) and requirement_id.split(".", 1)[0] != category:
                    issues.append(
                        f"frontend/ui-coverage-profile.json: `{requirement_prefix}.category` "
                        "must match its id prefix"
                    )
                checked_string_list(
                    requirement.get("evidenceKinds"),
                    f"frontend/ui-coverage-profile.json: `{requirement_prefix}.evidenceKinds`",
                    allowed=UI_PROFILE_EVIDENCE_KINDS,
                    non_empty=True,
                )
                checked_string_list(
                    requirement.get("requiredModes"),
                    f"frontend/ui-coverage-profile.json: `{requirement_prefix}.requiredModes`",
                    allowed=profile_modes,
                )
                checked_string_list(
                    requirement.get("invalidatedBy"),
                    f"frontend/ui-coverage-profile.json: `{requirement_prefix}.invalidatedBy`",
                    allowed=profile_triggers,
                    non_empty=True,
                )
                if not isinstance(requirement.get("allowNotApplicable"), bool):
                    issues.append(
                        f"frontend/ui-coverage-profile.json: `{requirement_prefix}.allowNotApplicable` "
                        "must be a boolean"
                    )
            present_categories = {
                requirement.get("category")
                for requirement in profile_requirements.values()
                if isinstance(requirement, dict)
            }
            missing_categories = sorted(UI_PROFILE_CATEGORIES - present_categories)
            if missing_categories:
                issues.append(
                    "frontend/ui-coverage-profile.json: `requirements` is missing "
                    "baseline categories: " + ", ".join(missing_categories)
                )
            missing_baseline_requirements = sorted(
                set(UI_REQUIRED_PROFILE_REQUIREMENTS) - set(profile_requirements)
            )
            if missing_baseline_requirements:
                issues.append(
                    "frontend/ui-coverage-profile.json: `requirements` is missing "
                    "baseline requirements: "
                    + ", ".join(missing_baseline_requirements)
                )
            for requirement_id, baseline in UI_REQUIRED_PROFILE_REQUIREMENTS.items():
                requirement = profile_requirements.get(requirement_id)
                if requirement is None:
                    continue
                missing_kinds = sorted(
                    baseline["evidenceKinds"]
                    - set(requirement.get("evidenceKinds", []))
                )
                if missing_kinds:
                    issues.append(
                        "frontend/ui-coverage-profile.json: "
                        f"`requirements.{requirement_id}.evidenceKinds` is missing "
                        "baseline evidence kinds: " + ", ".join(missing_kinds)
                    )
                missing_modes = sorted(
                    baseline["requiredModes"]
                    - set(requirement.get("requiredModes", []))
                )
                if missing_modes:
                    issues.append(
                        "frontend/ui-coverage-profile.json: "
                        f"`requirements.{requirement_id}.requiredModes` is missing "
                        "baseline modes: " + ", ".join(missing_modes)
                    )
                missing_invalidations = sorted(
                    baseline["invalidatedBy"]
                    - set(requirement.get("invalidatedBy", []))
                )
                if missing_invalidations:
                    issues.append(
                        "frontend/ui-coverage-profile.json: "
                        f"`requirements.{requirement_id}.invalidatedBy` is missing "
                        "baseline triggers: " + ", ".join(missing_invalidations)
                    )

    for typed_source in (
        "frontend/src/lib/ui-foundation.ts",
        "frontend/src/lib/active-surface-registry.ts",
    ):
        if not (root / typed_source).is_file():
            issues.append(f"{typed_source}: typed UI foundation source is missing")
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

    if manifest.get("schemaVersion") != 7:
        issues.append("frontend/ui-foundation.json: `schemaVersion` must be 7")

    def checked_path(value: object, field: str, suffix: str) -> Path | None:
        if not isinstance(value, str) or not value.strip():
            issues.append(f"frontend/ui-foundation.json: `{field}` must be a non-empty string")
            return None
        relative = Path(value)
        if (
            relative.is_absolute()
            or ".." in relative.parts
            or relative.as_posix() != value
            or relative.suffix != suffix
        ):
            issues.append(
                f"frontend/ui-foundation.json: `{field}` must be a normalized repo-relative {suffix} path"
            )
            return None
        resolved = root / relative
        if not resolved.is_file():
            issues.append(f"frontend/ui-foundation.json: `{field}` does not exist: {value}")
            return None
        return resolved

    def acceptance_reference_evidence(reference: object, field: str) -> set[str]:
        if not isinstance(reference, str):
            issues.append(f"frontend/ui-foundation.json: `{field}` must be a string")
            return set()
        match = UI_ACCEPTANCE_REFERENCE.fullmatch(reference)
        if match is None:
            issues.append(
                f"frontend/ui-foundation.json: `{field}` must use "
                "`docs/foundations/<spec>.md#AT-NNN`"
            )
            return set()
        owner_relative = Path(match.group("path"))
        if ".." in owner_relative.parts or owner_relative.as_posix() != match.group("path"):
            issues.append(
                f"frontend/ui-foundation.json: `{field}` must use a normalized foundation path"
            )
            return set()
        owner_path = root / owner_relative
        if not owner_path.is_file():
            issues.append(
                f"frontend/ui-foundation.json: `{field}` references missing foundation spec"
            )
            return set()
        at_id = match.group("at_id")
        owner_text = owner_path.read_text(encoding="utf-8")
        if re.search(rf"^[|]\s*{re.escape(at_id)}\s*[|]", owner_text, re.MULTILINE) is None:
            issues.append(
                f"frontend/ui-foundation.json: `{field}` references unknown acceptance id"
            )
            return set()
        sidecar_path = evidence_file_for(owner_path)
        if not sidecar_path.is_file():
            issues.append(
                f"frontend/ui-foundation.json: `{field}` has no acceptance evidence sidecar"
            )
            return set()
        sections = split_h2_sections(sidecar_path.read_text(encoding="utf-8"))
        table = first_markdown_table(sections.get("Acceptance Evidence", ""))
        if table is None:
            issues.append(
                f"frontend/ui-foundation.json: `{field}` has no Acceptance Evidence table"
            )
            return set()
        traced_evidence: set[str] = set()
        for row in table.rows:
            record = record_for_row(table, row)
            if at_id in at_ids_for_cell(record.get("AT ID", "")):
                traced_evidence.update(inline_code_values(record.get("Evidence", "")))
        if not traced_evidence:
            issues.append(
                f"frontend/ui-foundation.json: `{field}` has no matching Acceptance Evidence row"
            )
        return traced_evidence

    contracts = manifest.get("contracts")
    if not isinstance(contracts, dict) or not contracts:
        issues.append("frontend/ui-foundation.json: `contracts` must be a non-empty object")
        contracts = {}
    else:
        if list(contracts) != sorted(contracts):
            issues.append(
                "frontend/ui-foundation.json: `contracts` must be sorted by contract id"
            )
        for contract_id, contract in contracts.items():
            prefix = f"contracts.{contract_id}"
            if not isinstance(contract_id, str) or not UI_CONTRACT_ID.fullmatch(contract_id):
                issues.append(
                    f"frontend/ui-foundation.json: contract id `{contract_id}` must use kebab-case"
                )
            if not isinstance(contract, dict):
                issues.append(f"frontend/ui-foundation.json: `{prefix}` must be an object")
                continue
            unexpected = sorted(set(contract) - UI_CONTRACT_KEYS)
            missing = sorted(UI_CONTRACT_KEYS - set(contract))
            if unexpected:
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}` has unexpected keys: "
                    + ", ".join(unexpected)
                )
            if missing:
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}` is missing keys: "
                    + ", ".join(missing)
                )
            state = contract.get("state")
            if not isinstance(state, str) or state not in UI_CONTRACT_STATES:
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}.state` must be one of: "
                    + ", ".join(sorted(UI_CONTRACT_STATES))
                )

            contract_profile = contract.get("coverageProfile")
            if contract_profile != profile_id:
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}.coverageProfile` must match "
                    "frontend/ui-coverage-profile.json `profileId`"
                )

            acceptance = contract.get("acceptance")
            acceptance_status: object = None
            if not isinstance(acceptance, dict):
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}.acceptance` must be an object"
                )
            else:
                unexpected_acceptance = sorted(set(acceptance) - UI_ACCEPTANCE_KEYS)
                missing_acceptance = sorted(UI_ACCEPTANCE_KEYS - set(acceptance))
                if unexpected_acceptance:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.acceptance` has unexpected keys: "
                        + ", ".join(unexpected_acceptance)
                    )
                if missing_acceptance:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.acceptance` is missing keys: "
                        + ", ".join(missing_acceptance)
                    )
                acceptance_status = acceptance.get("status")
                if (
                    not isinstance(acceptance_status, str)
                    or acceptance_status not in UI_ACCEPTANCE_STATES
                ):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.acceptance.status` must be one of: "
                        + ", ".join(sorted(UI_ACCEPTANCE_STATES))
                    )
                if state == "defined" and acceptance_status == "accepted":
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}` cannot retain accepted review "
                        "while its state is `defined`"
                    )
                if (
                    isinstance(state, str)
                    and state in {"verified", "enforced"}
                    and acceptance_status != "accepted"
                ):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}` cannot be `{state}` without "
                        "accepted review state"
                    )
            spec = checked_path(contract.get("spec"), f"{prefix}.spec", ".md")
            if spec is not None:
                spec_relative = spec.relative_to(root).as_posix()
                if not spec_relative.startswith("docs/foundations/"):
                    issues.append(
                        "frontend/ui-foundation.json: contract spec must live under "
                        f"docs/foundations: {spec_relative}"
                    )

            evidence = contract.get("evidence")
            if not isinstance(evidence, dict):
                issues.append(f"frontend/ui-foundation.json: `{prefix}.evidence` must be an object")
                continue
            unexpected_evidence = sorted(set(evidence) - UI_EVIDENCE_KEYS)
            missing_evidence = sorted(UI_EVIDENCE_KEYS - set(evidence))
            if unexpected_evidence:
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}.evidence` has unexpected keys: "
                    + ", ".join(unexpected_evidence)
                )
            if missing_evidence:
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}.evidence` is missing keys: "
                    + ", ".join(missing_evidence)
                )
            declared_evidence: set[str] = set()
            evidence_by_kind: dict[str, set[str]] = {
                "assessment": set(),
                "browser": set(),
                "component": set(),
                "perceptual": set(),
            }
            for kind in ("assessment", "browser", "component"):
                paths = evidence.get(kind)
                if not isinstance(paths, list):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.{kind}` must be a list"
                    )
                    continue
                if kind != "assessment" and not paths:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.{kind}` must be a non-empty list"
                    )
                if len(paths) != len(set(path for path in paths if isinstance(path, str))):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.{kind}` contains duplicates"
                    )
                for index, path in enumerate(paths):
                    evidence_path = checked_path(
                        path,
                        f"{prefix}.evidence.{kind}[{index}]",
                        ".md" if kind == "assessment" else ".tsx" if kind == "component" else ".ts",
                    )
                    if evidence_path is None:
                        continue
                    evidence_relative = evidence_path.relative_to(root).as_posix()
                    declared_evidence.add(evidence_relative)
                    evidence_by_kind[kind].add(evidence_relative)
                    if kind == "assessment" and not (
                        evidence_relative.startswith("docs/foundations/")
                        and evidence_relative.endswith(".assessment.md")
                    ):
                        issues.append(
                            "frontend/ui-foundation.json: assessment evidence must be a "
                            f"docs/foundations .assessment.md file: {evidence_relative}"
                        )
                    if kind == "component" and not (
                        evidence_relative.endswith(".test.tsx")
                        and (
                            evidence_relative.startswith("frontend/src/")
                            or evidence_relative.startswith("frontend/tests/")
                        )
                    ):
                        issues.append(
                            "frontend/ui-foundation.json: component evidence must be a "
                            f"frontend .test.tsx file: {evidence_relative}"
                        )
                    if kind == "browser" and not (
                        evidence_relative.startswith("frontend/e2e/")
                        and evidence_relative.endswith(".pw.ts")
                    ):
                        issues.append(
                            "frontend/ui-foundation.json: browser evidence must be a "
                            f"frontend/e2e .pw.ts file: {evidence_relative}"
                        )

            perceptual = evidence.get("perceptual")
            perceptual_status: object = None
            if not isinstance(perceptual, dict):
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}.evidence.perceptual` must be an object"
                )
            else:
                unexpected_perceptual = sorted(set(perceptual) - UI_PERCEPTUAL_KEYS)
                missing_perceptual = sorted(UI_PERCEPTUAL_KEYS - set(perceptual))
                if unexpected_perceptual:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.perceptual` has unexpected keys: "
                        + ", ".join(unexpected_perceptual)
                    )
                if missing_perceptual:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.perceptual` is missing keys: "
                        + ", ".join(missing_perceptual)
                    )
                perceptual_status = perceptual.get("status")
                if (
                    not isinstance(perceptual_status, str)
                    or perceptual_status not in UI_PERCEPTUAL_STATES
                ):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.perceptual.status` "
                        "must be one of: "
                        + ", ".join(sorted(UI_PERCEPTUAL_STATES))
                    )
                artifacts = perceptual.get("artifacts")
                if not isinstance(artifacts, list):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.perceptual.artifacts` "
                        "must be a list"
                    )
                    artifacts = []
                if perceptual_status == "missing" and artifacts:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.perceptual` missing state "
                        "requires an empty artifact list"
                    )
                if perceptual_status in {"candidate", "accepted"} and not artifacts:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.perceptual` "
                        f"{perceptual_status} state requires version-controlled artifacts"
                    )
                if len(artifacts) != len(
                    set(path for path in artifacts if isinstance(path, str))
                ):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.perceptual.artifacts` "
                        "contains duplicates"
                    )
                for index, path in enumerate(artifacts):
                    evidence_path = checked_path(
                        path,
                        f"{prefix}.evidence.perceptual.artifacts[{index}]",
                        ".png",
                    )
                    if evidence_path is None:
                        continue
                    evidence_relative = evidence_path.relative_to(root).as_posix()
                    declared_evidence.add(evidence_relative)
                    evidence_by_kind["perceptual"].add(evidence_relative)
                    if not (
                        evidence_relative.startswith("frontend/e2e/")
                        and "-snapshots/" in evidence_relative
                        and evidence_relative.endswith(".png")
                    ):
                        issues.append(
                            "frontend/ui-foundation.json: perceptual evidence must be a "
                            f"version-controlled frontend/e2e snapshot: {evidence_relative}"
                        )
                if (
                    isinstance(state, str)
                    and state in {"verified", "enforced"}
                    and perceptual_status != "accepted"
                ):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}` cannot be `{state}` without "
                        "accepted perceptual evidence"
                    )
                if perceptual_status == "accepted" and acceptance_status != "accepted":
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}` cannot mark perceptual evidence "
                        "accepted without accepted review state"
                    )

            coverage = evidence.get("coverage")
            if not isinstance(coverage, dict):
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}.evidence.coverage` must be an object"
                )
            else:
                unexpected_coverage = sorted(set(coverage) - UI_COVERAGE_KEYS)
                missing_coverage = sorted(UI_COVERAGE_KEYS - set(coverage))
                if unexpected_coverage:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.coverage` has unexpected keys: "
                        + ", ".join(unexpected_coverage)
                    )
                if missing_coverage:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.coverage` is missing keys: "
                        + ", ".join(missing_coverage)
                    )
                covered = coverage.get("covered")
                gaps = coverage.get("gaps")
                not_applicable = coverage.get("notApplicable")
                if not isinstance(covered, dict):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.coverage.covered` "
                        "must be an object"
                    )
                    covered = {}
                elif list(covered) != sorted(covered):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.coverage.covered` "
                        "must be sorted by requirement id"
                    )
                checked_gaps = checked_string_list(
                    gaps,
                    f"frontend/ui-foundation.json: `{prefix}.evidence.coverage.gaps`",
                    allowed=set(profile_requirements),
                )
                if not isinstance(not_applicable, dict):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.coverage.notApplicable` "
                        "must be an object"
                    )
                    not_applicable = {}
                elif list(not_applicable) != sorted(not_applicable):
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.coverage.notApplicable` "
                        "must be sorted by requirement id"
                    )

                covered_ids = set(covered)
                gap_ids = set(checked_gaps)
                not_applicable_ids = set(not_applicable)
                classified_ids = covered_ids | gap_ids | not_applicable_ids
                duplicated_ids = sorted(
                    (covered_ids & gap_ids)
                    | (covered_ids & not_applicable_ids)
                    | (gap_ids & not_applicable_ids)
                )
                if duplicated_ids:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.coverage` classifies "
                        "requirements more than once: " + ", ".join(duplicated_ids)
                    )
                unknown_ids = sorted(classified_ids - set(profile_requirements))
                if unknown_ids:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.coverage` contains "
                        "unknown requirements: " + ", ".join(unknown_ids)
                    )
                unclassified_ids = sorted(set(profile_requirements) - classified_ids)
                if unclassified_ids:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}.evidence.coverage` leaves "
                        "requirements unclassified: " + ", ".join(unclassified_ids)
                    )
                if state in {"verified", "enforced"} and gap_ids:
                    issues.append(
                        f"frontend/ui-foundation.json: `{prefix}` cannot be `{state}` with "
                        "coverage gaps: " + ", ".join(sorted(gap_ids))
                    )

                for requirement_id, rationale in not_applicable.items():
                    requirement = profile_requirements.get(requirement_id)
                    if requirement is not None and requirement.get("allowNotApplicable") is not True:
                        issues.append(
                            f"frontend/ui-foundation.json: `{prefix}.evidence.coverage.notApplicable."
                            f"{requirement_id}` is forbidden by the active profile"
                        )
                    if not isinstance(rationale, str) or not rationale.strip():
                        issues.append(
                            f"frontend/ui-foundation.json: `{prefix}.evidence.coverage.notApplicable."
                            f"{requirement_id}` must give a non-empty rationale"
                        )

                for requirement_id, entry in covered.items():
                    requirement_prefix = (
                        f"{prefix}.evidence.coverage.covered.{requirement_id}"
                    )
                    requirement = profile_requirements.get(requirement_id)
                    if not isinstance(entry, dict):
                        issues.append(
                            f"frontend/ui-foundation.json: `{requirement_prefix}` must be an object"
                        )
                        continue
                    unexpected_entry = sorted(set(entry) - UI_COVERED_REQUIREMENT_KEYS)
                    missing_entry = sorted(UI_COVERED_REQUIREMENT_KEYS - set(entry))
                    if unexpected_entry:
                        issues.append(
                            f"frontend/ui-foundation.json: `{requirement_prefix}` has unexpected keys: "
                            + ", ".join(unexpected_entry)
                        )
                    if missing_entry:
                        issues.append(
                            f"frontend/ui-foundation.json: `{requirement_prefix}` is missing keys: "
                            + ", ".join(missing_entry)
                        )
                    acceptance_references = checked_string_list(
                        entry.get("acceptance"),
                        f"frontend/ui-foundation.json: `{requirement_prefix}.acceptance`",
                        non_empty=True,
                    )
                    traced_evidence: set[str] = set()
                    for index, reference in enumerate(acceptance_references):
                        traced_evidence.update(
                            acceptance_reference_evidence(
                                reference,
                                f"{requirement_prefix}.acceptance[{index}]",
                            )
                        )
                    coverage_evidence = checked_string_list(
                        entry.get("evidence"),
                        f"frontend/ui-foundation.json: `{requirement_prefix}.evidence`",
                    )
                    for index, reference in enumerate(coverage_evidence):
                        if reference not in declared_evidence:
                            issues.append(
                                f"frontend/ui-foundation.json: "
                                f"`{requirement_prefix}.evidence[{index}]` must reference "
                                "declared contract evidence"
                            )
                        if reference not in traced_evidence:
                            issues.append(
                                f"frontend/ui-foundation.json: "
                                f"`{requirement_prefix}.evidence[{index}]` is not traced by "
                                "the declared acceptance evidence"
                            )
                    coverage_modes = checked_string_list(
                        entry.get("modes"),
                        f"frontend/ui-foundation.json: `{requirement_prefix}.modes`",
                        allowed=profile_modes,
                    )
                    if requirement is None:
                        continue
                    required_modes = set(requirement.get("requiredModes", []))
                    missing_modes = sorted(required_modes - set(coverage_modes))
                    if missing_modes:
                        issues.append(
                            f"frontend/ui-foundation.json: `{requirement_prefix}` is missing "
                            "required modes: " + ", ".join(missing_modes)
                        )
                    evidence_kinds = set(requirement.get("evidenceKinds", []))
                    for evidence_kind in sorted(evidence_kinds):
                        if evidence_kind == "review":
                            if acceptance_status != "accepted":
                                issues.append(
                                    f"frontend/ui-foundation.json: `{requirement_prefix}` requires "
                                    "accepted project-owner review"
                                )
                            continue
                        if not (set(coverage_evidence) & evidence_by_kind[evidence_kind]):
                            issues.append(
                                f"frontend/ui-foundation.json: `{requirement_prefix}` requires "
                                f"`{evidence_kind}` evidence"
                            )

    enforced_contracts = manifest.get("enforcedContracts")
    if not isinstance(enforced_contracts, dict):
        issues.append("frontend/ui-foundation.json: `enforcedContracts` must be an object")
        enforced_contracts = {}
    else:
        if list(enforced_contracts) != sorted(enforced_contracts):
            issues.append(
                "frontend/ui-foundation.json: `enforcedContracts` must be sorted by contract id"
            )
        for contract_id, marker in enforced_contracts.items():
            prefix = f"enforcedContracts.{contract_id}"
            if marker is not True:
                issues.append(f"frontend/ui-foundation.json: `{prefix}` must be `true`")
            if contract_id not in contracts:
                issues.append(
                    f"frontend/ui-foundation.json: `{prefix}` has no matching contract"
                )

    enforced_ids = set(enforced_contracts)
    for contract_id, contract in contracts.items():
        if not isinstance(contract, dict):
            continue
        state = contract.get("state")
        registered = contract_id in enforced_ids
        if state == "enforced" and not registered:
            issues.append(
                f"frontend/ui-foundation.json: `contracts.{contract_id}` is `enforced` "
                "but missing from `enforcedContracts`"
            )
        if state != "enforced" and registered:
            issues.append(
                f"frontend/ui-foundation.json: `contracts.{contract_id}` is registered as "
                f"enforced while its state is `{state}`"
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
