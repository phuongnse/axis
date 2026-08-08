from __future__ import annotations

import json
from pathlib import Path
import sys
import tomllib
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
ORCHESTRATION_PATH = ROOT / ".codex" / "orchestration.toml"


def configured_agent_specs(path: Path = ORCHESTRATION_PATH) -> dict[str, dict[str, str]]:
    contract = tomllib.loads(path.read_text(encoding="utf-8"))
    agents = contract.get("agents")
    if not isinstance(agents, dict) or not agents:
        raise ValueError("orchestration contract must define named agents")

    specs: dict[str, dict[str, str]] = {}
    for name, value in agents.items():
        if not isinstance(name, str) or not isinstance(value, dict):
            raise ValueError("orchestration contract contains an invalid agent role")
        model = value.get("model")
        reasoning = value.get("reasoning")
        if not isinstance(model, str) or not isinstance(reasoning, str):
            raise ValueError(f"orchestration role {name!r} must define model and reasoning")
        specs[name] = {"model": model, "reasoning": reasoning}
    return specs


def configured_agent_types(path: Path = ORCHESTRATION_PATH) -> frozenset[str]:
    return frozenset(configured_agent_specs(path))


def requested_role(
    tool_name: str,
    tool_input: dict[str, Any],
    allowed_agent_types: frozenset[str],
) -> str | None:
    if tool_name == "Agent":
        agent_type = tool_input.get("agent_type")
        return agent_type if isinstance(agent_type, str) else None

    task_name = tool_input.get("task_name")
    if not isinstance(task_name, str):
        return None
    return next(
        (
            role
            for role in sorted(allowed_agent_types, key=len, reverse=True)
            if task_name == role or task_name.startswith(f"{role}__")
        ),
        None,
    )


def deny(reason: str) -> dict[str, Any]:
    return {
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }


def policy_decision(
    payload: dict[str, Any],
    *,
    allowed_agent_types: frozenset[str] | None = None,
    agent_specs: dict[str, dict[str, str]] | None = None,
) -> dict[str, Any] | None:
    tool_name = payload.get("tool_name")
    if payload.get("hook_event_name") != "PreToolUse" or tool_name not in {
        "Agent",
        "spawn_agent",
        "collaboration.spawn_agent",
    }:
        return None

    specs = configured_agent_specs() if agent_specs is None else agent_specs
    allowed = allowed_agent_types if allowed_agent_types is not None else frozenset(specs)
    tool_input = payload.get("tool_input")
    if not isinstance(tool_input, dict):
        tool_input = {}
    role = requested_role(tool_name, tool_input, allowed)
    if role not in allowed:
        supplied = tool_input.get("agent_type") if tool_name == "Agent" else tool_input.get("task_name")
        field = "agent_type" if tool_name == "Agent" else "task_name"
        return deny(
            f"Axis delegation requires an explicit project role; {field} was {supplied!r}. "
            f"Choose one of: {', '.join(sorted(allowed))}."
        )

    if tool_name != "Agent":
        spec = specs.get(role)
        if spec is None:
            return deny(f"Axis delegation role {role!r} has no configured model profile.")
        actual_model = tool_input.get("model")
        actual_reasoning = tool_input.get("reasoning_effort")
        if actual_model != spec["model"] or actual_reasoning != spec["reasoning"]:
            return deny(
                f"Axis delegation role {role!r} requires model={spec['model']!r} and "
                f"reasoning_effort={spec['reasoning']!r}; received "
                f"model={actual_model!r}, reasoning_effort={actual_reasoning!r}."
            )

    return None


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, OSError) as exc:
        print(f"Axis delegation hook could not read tool input: {exc}", file=sys.stderr)
        return 2

    if not isinstance(payload, dict):
        print("Axis delegation hook expected a JSON object", file=sys.stderr)
        return 2

    try:
        decision = policy_decision(payload)
    except (OSError, tomllib.TOMLDecodeError, ValueError) as exc:
        print(f"Axis delegation hook could not load orchestration contract: {exc}", file=sys.stderr)
        return 2
    if decision is not None:
        json.dump(decision, sys.stdout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
