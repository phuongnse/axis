"""Deterministic dependency-vulnerability policy evaluation."""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import date
from typing import Mapping


@dataclass(frozen=True)
class NpmAuditPolicyResult:
    issues: tuple[str, ...]
    accepted: tuple[tuple[str, str], ...]


def _advisory_id(value: object) -> str | None:
    if not isinstance(value, Mapping):
        return None
    url = value.get("url")
    if not isinstance(url, str):
        return None
    match = re.search(r"/advisories/(GHSA-[A-Za-z0-9-]+)$", url)
    return match.group(1) if match else None


def _severity(value: object) -> str | None:
    if not isinstance(value, str):
        return None
    normalized = value.strip().lower()
    return normalized if normalized in {"info", "low", "moderate", "high", "critical"} else None


def _via_entries(vulnerability: object) -> list[object]:
    if not isinstance(vulnerability, Mapping):
        return []
    via = vulnerability.get("via")
    return list(via) if isinstance(via, list) else []


def _resolved_advisories(
    package: str,
    vulnerabilities: Mapping[str, object],
    *,
    seen: frozenset[str] = frozenset(),
) -> tuple[set[str], set[str]]:
    if package in seen:
        return set(), {f"dependency cycle at {package}"}
    vulnerability = vulnerabilities.get(package)
    if not isinstance(vulnerability, Mapping):
        return set(), {f"missing vulnerability node {package}"}

    advisory_ids: set[str] = set()
    unresolved: set[str] = set()
    for entry in _via_entries(vulnerability):
        if isinstance(entry, str):
            nested_ids, nested_unresolved = _resolved_advisories(
                entry,
                vulnerabilities,
                seen=seen | {package},
            )
            advisory_ids.update(nested_ids)
            unresolved.update(nested_unresolved)
            continue
        advisory = _advisory_id(entry)
        if advisory is None:
            unresolved.add(f"{package} contains an advisory without a GitHub advisory ID")
        else:
            advisory_ids.add(advisory)
    return advisory_ids, unresolved


def _acceptance_issues(
    document: object,
    vulnerabilities: Mapping[str, object],
    advisory_severities: Mapping[str, str],
    *,
    today: date,
) -> tuple[dict[str, Mapping[str, object]], list[str]]:
    issues: list[str] = []
    if not isinstance(document, Mapping) or document.get("schemaVersion") != 1:
        return {}, ["frontend/dependency-risk-acceptances.json must use schemaVersion 1"]
    rows = document.get("acceptances")
    if not isinstance(rows, list):
        return {}, ["frontend/dependency-risk-acceptances.json acceptances must be an array"]

    acceptances: dict[str, Mapping[str, object]] = {}
    for index, row in enumerate(rows):
        label = f"acceptances[{index}]"
        if not isinstance(row, Mapping):
            issues.append(f"{label} must be an object")
            continue
        advisory = row.get("advisory")
        if not isinstance(advisory, str) or not re.fullmatch(r"GHSA-[A-Za-z0-9-]+", advisory):
            issues.append(f"{label}.advisory must be a GitHub advisory ID")
            continue
        if advisory in acceptances:
            issues.append(f"{advisory} has duplicate risk acceptances")
            continue
        acceptances[advisory] = row

        for field in ("owner", "scope", "reason", "remediation"):
            if not isinstance(row.get(field), str) or not str(row[field]).strip():
                issues.append(f"{advisory}.{field} must be non-empty")

        accepted_severity = _severity(row.get("severity"))
        if accepted_severity is None:
            issues.append(f"{advisory}.severity is invalid")
        elif advisory in advisory_severities and accepted_severity != advisory_severities[advisory]:
            issues.append(
                f"{advisory} severity changed from accepted {accepted_severity} "
                f"to {advisory_severities[advisory]}"
            )

        accepted_on = row.get("acceptedOn")
        expires_on = row.get("expiresOn")
        try:
            accepted = date.fromisoformat(accepted_on) if isinstance(accepted_on, str) else None
        except ValueError:
            accepted = None
        try:
            expiry = date.fromisoformat(expires_on) if isinstance(expires_on, str) else None
        except ValueError:
            expiry = None
        if accepted is None:
            issues.append(f"{advisory}.acceptedOn must be an ISO date")
        elif accepted > today:
            issues.append(f"{advisory}.acceptedOn cannot be in the future")
        if expiry is None:
            issues.append(f"{advisory}.expiresOn must be an ISO date")
        elif expiry < today:
            issues.append(f"{advisory} risk acceptance expired on {expiry.isoformat()}")
        if accepted is not None and expiry is not None:
            if expiry < accepted:
                issues.append(f"{advisory}.expiresOn cannot precede acceptedOn")
            elif (expiry - accepted).days > 30:
                issues.append(f"{advisory} risk acceptance exceeds the 30-day maximum")

        path = row.get("dependencyPath")
        if not isinstance(path, list) or not path or not all(isinstance(item, str) and item for item in path):
            issues.append(f"{advisory}.dependencyPath must be a non-empty package path")
            continue
        if advisory not in advisory_severities:
            continue
        first = vulnerabilities.get(path[0])
        if not isinstance(first, Mapping) or first.get("isDirect") is not True:
            issues.append(f"{advisory}.dependencyPath must start with a current direct dependency")
        for parent, child in zip(path, path[1:]):
            if child not in _via_entries(vulnerabilities.get(parent)):
                issues.append(f"{advisory}.dependencyPath edge {parent} -> {child} is stale")
        if advisory not in {
            value
            for value in (_advisory_id(entry) for entry in _via_entries(vulnerabilities.get(path[-1])))
            if value is not None
        }:
            issues.append(f"{advisory}.dependencyPath does not terminate at the accepted advisory")

    for advisory in sorted(set(acceptances) - set(advisory_severities)):
        issues.append(f"{advisory} is stale because npm audit no longer reports it")
    return acceptances, issues


def evaluate_npm_audit(
    report: object,
    acceptance_document: object,
    *,
    today: date | None = None,
) -> NpmAuditPolicyResult:
    if not isinstance(report, Mapping) or report.get("auditReportVersion") != 2:
        return NpmAuditPolicyResult(("npm audit output must use auditReportVersion 2",), ())
    vulnerabilities = report.get("vulnerabilities")
    if not isinstance(vulnerabilities, Mapping):
        return NpmAuditPolicyResult(("npm audit output must contain a vulnerabilities object",), ())

    issues: list[str] = []
    package_advisories: dict[str, set[str]] = {}
    advisory_severities: dict[str, str] = {}
    for package, vulnerability in vulnerabilities.items():
        if not isinstance(package, str) or not isinstance(vulnerability, Mapping):
            issues.append("npm audit contains an invalid vulnerability entry")
            continue
        severity = _severity(vulnerability.get("severity"))
        if severity is None:
            issues.append(f"{package} has an invalid severity")
            continue
        advisory_ids, unresolved = _resolved_advisories(package, vulnerabilities)
        package_advisories[package] = advisory_ids
        issues.extend(sorted(unresolved))
        if not advisory_ids:
            issues.append(f"{package} does not resolve to a GitHub advisory")
        for entry in _via_entries(vulnerability):
            advisory = _advisory_id(entry)
            advisory_severity = _severity(entry.get("severity")) if isinstance(entry, Mapping) else None
            if advisory is not None and advisory_severity is not None:
                advisory_severities[advisory] = advisory_severity

    acceptances, acceptance_issues = _acceptance_issues(
        acceptance_document,
        vulnerabilities,
        advisory_severities,
        today=today or date.today(),
    )
    issues.extend(acceptance_issues)

    used_acceptances: set[str] = set()
    for package, vulnerability in vulnerabilities.items():
        if not isinstance(package, str) or not isinstance(vulnerability, Mapping):
            continue
        severity = _severity(vulnerability.get("severity"))
        if severity in {"high", "critical"}:
            issues.append(f"{package}: {severity} vulnerabilities cannot be accepted")
            continue
        for advisory in sorted(package_advisories.get(package, set())):
            if advisory not in acceptances:
                issues.append(f"{advisory} is not accepted for {package}")
            else:
                used_acceptances.add(advisory)

    accepted = tuple(
        sorted(
            (
                advisory,
                str(acceptances[advisory].get("expiresOn")),
            )
            for advisory in used_acceptances
            if advisory in acceptances
        )
    )
    return NpmAuditPolicyResult(tuple(dict.fromkeys(issues)), accepted)
