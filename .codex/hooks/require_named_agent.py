from __future__ import annotations

import json
from pathlib import Path
import sys
import tomllib
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
ORCHESTRATION_PATH = ROOT / ".codex" / "orchestration.toml"


def configured_agent_types(path: Path = ORCHESTRATION_PATH) -> frozenset[str]:
    contract = tomllib.loads(path.read_text(encoding="utf-8"))
    agents = contract.get("agents")
    if not isinstance(agents, dict) or not agents:
        raise ValueError("orchestration contract must define named agents")
    return frozenset(agents)


def policy_decision(
    payload: dict[str, Any],
    *,
    allowed_agent_types: frozenset[str] | None = None,
) -> dict[str, Any] | None:
    if payload.get("hook_event_name") != "PreToolUse" or payload.get("tool_name") not in {
        "Agent",
        "spawn_agent",
    }:
        return None

    allowed = allowed_agent_types if allowed_agent_types is not None else configured_agent_types()
    tool_input = payload.get("tool_input")
    agent_type = tool_input.get("agent_type") if isinstance(tool_input, dict) else None
    if agent_type in allowed:
        return None

    supplied = repr(agent_type) if agent_type is not None else "omitted"
    return {
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": (
                f"Axis delegation requires an explicit project role; agent_type was {supplied}. "
                f"Choose one of: {', '.join(sorted(allowed))}."
            ),
        }
    }


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
