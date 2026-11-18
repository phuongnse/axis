import { spawn } from 'node:child_process'
import path from 'node:path'

/** The built agent host. scripts/build.sh builds it in Release. */
const agentDll = path.resolve(import.meta.dirname, '..', '..', 'src', 'Axis.Agent', 'bin', 'Release', 'net10.0', 'Axis.Agent.dll')

/**
 * Starts `Axis.Agent` with `settings` added to this process's environment, and returns once it answers `/health/ready` at
 * `Health__Urls` with 200. The host plans the authoring requests that wait in `planning`, see
 * docs/reference/authoring-requests.md#the-agent-host. Build the settings with `agentHostSettings` from
 * scripts/lib/ai-eval-stack.mjs. Throws with the host's output when it exits, or when it is not ready within 60 seconds. `stop`
 * ends it with SIGTERM, and with SIGKILL after 15 seconds. `output` gives what it printed.
 */
export async function startAgentHost(settings: Record<string, string>): Promise<{ stop(): Promise<void>; output(): string }> {
  const child = spawn('dotnet', [agentDll], {
    env: { ...process.env, ...settings },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  let output = ''
  child.stdout.setEncoding('utf8').on('data', (chunk: string) => (output += chunk))
  child.stderr.setEncoding('utf8').on('data', (chunk: string) => (output += chunk))
  let exitCode: number | null | undefined
  const exited = new Promise<void>((resolve) =>
    child.once('close', (code) => {
      exitCode = code ?? -1
      resolve()
    }),
  )
  const stop = async () => {
    if (exitCode === undefined) {
      child.kill('SIGTERM')
      // A host that ignores SIGTERM must not hang the spec.
      const killer = setTimeout(() => child.kill('SIGKILL'), 15_000)
      await exited
      clearTimeout(killer)
    }
  }

  const url = settings.Health__Urls
  for (let second = 0; second < 60; second++) {
    if (exitCode !== undefined) break
    if (second > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
    if (exitCode !== undefined) break
    if (await answersReady(url)) return { stop, output: () => output }
  }
  const reason = exitCode !== undefined ? `The agent host exited with code ${exitCode}.` : 'The agent host never answered /health/ready with 200 within 60 seconds.'
  await stop()
  throw new Error(`${reason} Agent host output:\n${output}`)
}

async function answersReady(url: string): Promise<boolean> {
  try {
    return (await fetch(`${url}/health/ready`)).status === 200
  } catch {
    return false
  }
}
