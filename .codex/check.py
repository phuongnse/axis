from __future__ import annotations

from pathlib import Path
import subprocess
import sys
import tomllib


ROOT = Path(__file__).resolve().parent.parent
AGENT_SPECS = {
    "axis_scout": ("gpt-5.6-luna", "medium"),
    "axis_investigator": ("gpt-5.6-terra", "medium"),
    "axis_planner": ("gpt-5.6-sol", "high"),
    "axis_worker": ("gpt-5.6-terra", "medium"),
    "axis_reviewer": ("gpt-5.6-sol", "high"),
}
AGENT_PROFILE_MARKERS = {
    "axis_scout": ("`caveman:caveman` at full intensity", "never default to ultra"),
    "axis_investigator": ("`caveman:caveman` at lite intensity", "never default to ultra"),
    "axis_planner": (
        "`ponytail:ponytail` at full intensity",
        "`caveman:caveman` at lite intensity",
        "never default to ultra",
    ),
    "axis_worker": (
        "`ponytail:ponytail` at full intensity",
        "`caveman:caveman` at full intensity",
        "never default to ultra",
    ),
    "axis_reviewer": (
        "`ponytail:ponytail` at lite intensity",
        "`caveman:caveman` at lite intensity",
        "never default to ultra",
    ),
}
FORBIDDEN_DEFAULT_AGENT_KEYS = ("default_subagent_model", "default_subagent_reasoning_effort")
APPROVED_ULTRA_GUARD = "never default to ultra"


def project_agent_role_files(root: Path = ROOT) -> set[str]:
    agent_root = root / ".codex" / "agents"
    return {path.relative_to(agent_root).as_posix() for path in agent_root.rglob("*.toml")}


def missing_agent_profile_markers(name: str, instructions: str) -> list[str]:
    normalized = instructions.lower()
    return [marker for marker in AGENT_PROFILE_MARKERS.get(name, ()) if marker not in normalized]


def forbidden_agent_profile_directives(name: str, instructions: str) -> list[str]:
    normalized = instructions.lower()
    directives = []
    if "ultra" in normalized.replace(APPROVED_ULTRA_GUARD, ""):
        directives.append("unapproved ultra directive")
    for marker in AGENT_PROFILE_MARKERS.get(name, ()):
        for prefix in ("do not apply ", "do not use ", "never use "):
            directive = f"{prefix}{marker}"
            if directive in normalized:
                directives.append(directive)
    return directives


def unexpected_default_agent_keys(agents: dict[str, object]) -> list[str]:
    return [key for key in FORBIDDEN_DEFAULT_AGENT_KEYS if key in agents]


def config_issues() -> list[str]:
    issues: list[str] = []

    def load(path: Path) -> dict[str, object] | None:
        try:
            return tomllib.loads(path.read_text(encoding="utf-8"))
        except (OSError, tomllib.TOMLDecodeError) as exc:
            issues.append(f"{path.relative_to(ROOT)}: cannot load configuration: {exc}")
            return None

    config = load(ROOT / ".codex" / "config.toml")
    if config is not None:
        expected = {"model": "gpt-5.6-sol", "model_reasoning_effort": "high"}
        for key, value in expected.items():
            if config.get(key) != value:
                issues.append(f".codex/config.toml: `{key}` must be `{value}`")

        agents = config.get("agents")
        expected_agents = {
            "enabled": True,
            "max_concurrent_threads_per_session": 2,
        }
        if not isinstance(agents, dict):
            issues.append(".codex/config.toml: missing `[agents]` table")
        else:
            for key, value in expected_agents.items():
                if agents.get(key) != value:
                    issues.append(f".codex/config.toml: `agents.{key}` must be `{value}`")
            for key in unexpected_default_agent_keys(agents):
                issues.append(
                    f".codex/config.toml: `agents.{key}` must be omitted; "
                    "every delegation selects a named role"
                )

        hooks = config.get("hooks")
        pre_tool_use = hooks.get("PreToolUse") if isinstance(hooks, dict) else None
        hook_command = 'python "$(git rev-parse --show-toplevel)/.codex/hooks/require_named_agent.py"'
        expected_hook = {
            "matcher": "^Agent$",
            "hooks": [
                {
                    "type": "command",
                    "command": hook_command,
                    "timeout": 5,
                    "statusMessage": "Checking Axis delegation role",
                }
            ],
        }
        if pre_tool_use != [expected_hook]:
            issues.append(".codex/config.toml: PreToolUse must enforce explicit Axis agent roles")

        servers = config.get("mcp_servers")
        axis_mcp = servers.get("axis") if isinstance(servers, dict) else None
        expected_mcp = {
            "command": "python",
            "args": ["scripts/axis.py", "mcp", "serve"],
        }
        if not isinstance(axis_mcp, dict):
            issues.append(".codex/config.toml: missing `[mcp_servers.axis]` table")
        else:
            for key, value in expected_mcp.items():
                if axis_mcp.get(key) != value:
                    issues.append(f".codex/config.toml: `mcp_servers.axis.{key}` must be `{value}`")
            if "cwd" in axis_mcp:
                issues.append(".codex/config.toml: `mcp_servers.axis.cwd` must be omitted")
            args = axis_mcp.get("args")
            if not isinstance(args, list) or not args or not (ROOT / str(args[0])).is_file():
                issues.append(".codex/config.toml: Axis MCP entrypoint must resolve from project root")

    expected_agent_files = {f"{name}.toml" for name in AGENT_SPECS}
    actual_agent_files = project_agent_role_files()
    for name in sorted(actual_agent_files - expected_agent_files):
        issues.append(f".codex/agents/{name}: unexpected project agent role")
    for name, (model, effort) in AGENT_SPECS.items():
        path = ROOT / ".codex" / "agents" / f"{name}.toml"
        agent = load(path)
        if agent is None:
            continue
        expected = {"name": name, "model": model, "model_reasoning_effort": effort}
        for key, value in expected.items():
            if agent.get(key) != value:
                issues.append(f"{path.relative_to(ROOT)}: `{key}` must be `{value}`")
        if "sandbox_mode" in agent:
            issues.append(f"{path.relative_to(ROOT)}: omit `sandbox_mode`; delegated agents inherit the primary runtime")
        for key in ("description", "developer_instructions"):
            if not isinstance(agent.get(key), str) or not agent[key].strip():
                issues.append(f"{path.relative_to(ROOT)}: `{key}` is required")
        instructions = agent.get("developer_instructions")
        if isinstance(instructions, str):
            for marker in missing_agent_profile_markers(name, instructions):
                issues.append(f"{path.relative_to(ROOT)}: missing profile marker `{marker}`")
            for directive in forbidden_agent_profile_directives(name, instructions):
                issues.append(f"{path.relative_to(ROOT)}: forbidden profile directive `{directive}`")

    hook_path = ROOT / ".codex" / "hooks" / "require_named_agent.py"
    if not hook_path.is_file():
        issues.append(".codex/hooks/require_named_agent.py: named-role enforcement hook is required")

    tracked = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=ROOT,
        check=True,
        capture_output=True,
    ).stdout.decode().split("\0")
    private_terms = (*AGENT_SPECS, *(spec[0] for spec in AGENT_SPECS.values()))
    for relative in tracked:
        if not relative or relative.startswith(".codex/"):
            continue
        path = ROOT / relative
        if not path.is_file():
            continue
        text = path.read_text(encoding="utf-8", errors="ignore")
        for term in private_terms:
            if term in text:
                issues.append(f"{relative}: keep runtime-specific agent term `{term}` under `.codex/`")
    return issues


def main() -> int:
    issues = config_issues()
    if issues:
        print("check-project-orchestration FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1
    print("check-project-orchestration: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
