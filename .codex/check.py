from __future__ import annotations

from pathlib import Path
import sys
import tomllib


ROOT = Path(__file__).resolve().parent.parent
ORCHESTRATION_PATH = ROOT / ".codex" / "orchestration.toml"
PROFILE_LEVELS = {"off", "lite", "full"}
REASONING_LEVELS = {"low", "medium", "high", "xhigh", "max", "ultra"}
FORBIDDEN_DEFAULT_AGENT_KEYS = ("default_subagent_model", "default_subagent_reasoning_effort")


def project_agent_role_files(root: Path = ROOT) -> set[str]:
    agent_root = root / ".codex" / "agents"
    return {path.relative_to(agent_root).as_posix() for path in agent_root.rglob("*.toml")}


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

    orchestration = load(ORCHESTRATION_PATH)
    agent_specs: dict[str, dict[str, object]] = {}
    runtime: dict[str, object] = {}
    if orchestration is not None:
        expected_root_keys = {"version", "runtime", "profile_providers", "agents"}
        unknown = sorted(set(orchestration) - expected_root_keys)
        if unknown:
            issues.append(f".codex/orchestration.toml: unknown root keys {unknown}")
        if orchestration.get("version") != 1:
            issues.append(".codex/orchestration.toml: `version` must be 1")

        runtime_value = orchestration.get("runtime")
        if not isinstance(runtime_value, dict):
            issues.append(".codex/orchestration.toml: missing `[runtime]` table")
        else:
            runtime = runtime_value
            expected_runtime_keys = {
                "primary_model",
                "primary_reasoning",
                "max_concurrent_threads",
                "allow_default_delegate",
            }
            if set(runtime) != expected_runtime_keys:
                issues.append(
                    ".codex/orchestration.toml: `[runtime]` must contain exactly "
                    f"{sorted(expected_runtime_keys)}"
                )
            if runtime.get("primary_reasoning") not in REASONING_LEVELS:
                issues.append(".codex/orchestration.toml: invalid `runtime.primary_reasoning`")
            if not isinstance(runtime.get("primary_model"), str) or not runtime["primary_model"]:
                issues.append(".codex/orchestration.toml: `runtime.primary_model` is required")
            if not isinstance(runtime.get("max_concurrent_threads"), int) or runtime["max_concurrent_threads"] < 1:
                issues.append(".codex/orchestration.toml: `runtime.max_concurrent_threads` must be positive")
            if not isinstance(runtime.get("allow_default_delegate"), bool):
                issues.append(".codex/orchestration.toml: `runtime.allow_default_delegate` must be boolean")

        providers = orchestration.get("profile_providers")
        if not isinstance(providers, dict) or set(providers) != {"minimality", "compression"} or any(
            not isinstance(value, str) or not value for value in providers.values()
        ):
            issues.append(
                ".codex/orchestration.toml: `[profile_providers]` must define non-empty "
                "`minimality` and `compression` providers"
            )

        agents_value = orchestration.get("agents")
        if not isinstance(agents_value, dict) or not agents_value:
            issues.append(".codex/orchestration.toml: `[agents]` must define named roles")
        else:
            expected_agent_keys = {"model", "reasoning", "minimality", "compression"}
            for name, spec in agents_value.items():
                label = f".codex/orchestration.toml: agent `{name}`"
                if not isinstance(spec, dict):
                    issues.append(f"{label} must be a table")
                    continue
                if set(spec) != expected_agent_keys:
                    issues.append(f"{label} must contain exactly {sorted(expected_agent_keys)}")
                    continue
                if not isinstance(spec.get("model"), str) or not spec["model"]:
                    issues.append(f"{label} model is required")
                if spec.get("reasoning") not in REASONING_LEVELS:
                    issues.append(f"{label} reasoning must be one of {sorted(REASONING_LEVELS)}")
                for profile in ("minimality", "compression"):
                    if spec.get(profile) not in PROFILE_LEVELS:
                        issues.append(f"{label} {profile} must be one of {sorted(PROFILE_LEVELS)}")
                agent_specs[name] = spec

    config = load(ROOT / ".codex" / "config.toml")
    if config is not None:
        expected = {
            "model": runtime.get("primary_model"),
            "model_reasoning_effort": runtime.get("primary_reasoning"),
        }
        for key, value in expected.items():
            if config.get(key) != value:
                issues.append(f".codex/config.toml: `{key}` must be `{value}`")

        agents = config.get("agents")
        expected_agents = {
            "enabled": True,
            "max_concurrent_threads_per_session": runtime.get("max_concurrent_threads"),
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
            if runtime.get("allow_default_delegate") is True:
                issues.append(
                    ".codex/orchestration.toml: default delegation conflicts with named-role enforcement"
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
            "args": ["scripts/axis.py", "mcp", "serve", "--access", "write"],
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

    expected_agent_files = {f"{name}.toml" for name in agent_specs}
    actual_agent_files = project_agent_role_files()
    for name in sorted(actual_agent_files - expected_agent_files):
        issues.append(f".codex/agents/{name}: unexpected project agent role")
    for name, spec in agent_specs.items():
        path = ROOT / ".codex" / "agents" / f"{name}.toml"
        agent = load(path)
        if agent is None:
            continue
        expected = {
            "name": name,
            "model": spec["model"],
            "model_reasoning_effort": spec["reasoning"],
        }
        for key, value in expected.items():
            if agent.get(key) != value:
                issues.append(f"{path.relative_to(ROOT)}: `{key}` must be `{value}`")
        if "sandbox_mode" in agent:
            issues.append(f"{path.relative_to(ROOT)}: omit `sandbox_mode`; delegated agents inherit the primary runtime")
        for key in ("description", "developer_instructions"):
            if not isinstance(agent.get(key), str) or not agent[key].strip():
                issues.append(f"{path.relative_to(ROOT)}: `{key}` is required")

    hook_path = ROOT / ".codex" / "hooks" / "require_named_agent.py"
    if not hook_path.is_file():
        issues.append(".codex/hooks/require_named_agent.py: named-role enforcement hook is required")

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
