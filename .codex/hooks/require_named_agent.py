from __future__ import annotations

import json
import sys
from typing import Any


ALLOWED_AGENT_TYPES = frozenset(
    {
        "axis_scout",
        "axis_investigator",
        "axis_planner",
        "axis_worker",
        "axis_reviewer",
    }
)


def policy_decision(payload: dict[str, Any]) -> dict[str, Any] | None:
    if payload.get("hook_event_name") != "PreToolUse" or payload.get("tool_name") not in {
        "Agent",
        "spawn_agent",
    }:
        return None

    tool_input = payload.get("tool_input")
    agent_type = tool_input.get("agent_type") if isinstance(tool_input, dict) else None
    if agent_type in ALLOWED_AGENT_TYPES:
        return None

    supplied = repr(agent_type) if agent_type is not None else "omitted"
    return {
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": (
                f"Axis delegation requires an explicit project role; agent_type was {supplied}. "
                f"Choose one of: {', '.join(sorted(ALLOWED_AGENT_TYPES))}."
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

    decision = policy_decision(payload)
    if decision is not None:
        json.dump(decision, sys.stdout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
