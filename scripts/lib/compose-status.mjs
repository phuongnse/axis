// Reads `docker compose ps --format json` and says which services are not ready, for the Compose smoke test.

/** Parses the output of `docker compose ps --format json`. Newer Compose versions print one JSON object per line, older ones an array. */
export function parsePs(text) {
  const trimmed = text.trim()
  if (trimmed === '') return []
  return trimmed.startsWith('[') ? JSON.parse(trimmed) : trimmed.split('\n').map((line) => JSON.parse(line))
}

/**
 * One line for each container that is not running, or whose health is neither empty nor healthy.
 * `lastChecks` maps a service name to the output of its last health check. It is '' when every container is ready.
 */
export function describeNotReady(containers, lastChecks = {}) {
  const lines = []
  for (const { Service: service, State: state, Health: health, ExitCode: exitCode } of containers) {
    if (state !== 'running') {
      lines.push(`${service}: ${state === 'exited' ? `exited with ${exitCode}` : state}`)
    } else if (health !== undefined && health !== '' && health !== 'healthy') {
      const output = lastChecks[service]?.trim()
      lines.push(`${service}: ${health}.${output ? ` Last check: ${output}` : ''}`)
    }
  }
  return lines.join('\n')
}
