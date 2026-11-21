import { spawn } from 'node:child_process'
import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import net from 'node:net'
import os from 'node:os'
import path from 'node:path'
import { sparePort } from './activate'

/** The built package host. scripts/build.sh builds it in Release. */
const packageHostDll = path.resolve(import.meta.dirname, '..', '..', 'src', 'Axis.PackageHost', 'bin', 'Release', 'net10.0', 'Axis.PackageHost.dll')

/** A stub for Trivy that reports no vulnerability, as the integration tests' StubTrivy does. */
const stubTrivy = `#!/bin/sh\ncat <<'JSON'\n{"Results":[]}\nJSON\n`

/**
 * Starts `Axis.PackageHost host` on a spare loopback port, with `Extensions:Path` in a temporary folder and a stub Trivy
 * that reports no findings. A publisher with `Extensions:Runners:Application` set to `address` builds and tests application
 * packages on it, see docs/reference/package-runners.md#the-runner-protocol and the package spec in docs/testing.md. It
 * returns once a TCP connect to its port succeeds. Host mode has no health route. It throws with the host's output when
 * the host exits or does not listen within 60 seconds. `stop` ends it with SIGTERM, with SIGKILL after 15 seconds, and
 * removes its folders. `output` gives what it printed.
 */
export async function startPackageHost(): Promise<{ address: string; stop(): Promise<void>; output(): string }> {
  const extensions = await mkdtemp(path.join(os.tmpdir(), 'axis-package-host-extensions-'))
  const scripts = await mkdtemp(path.join(os.tmpdir(), 'axis-package-host-trivy-'))
  const trivy = path.join(scripts, 'trivy')
  await writeFile(trivy, stubTrivy, { mode: 0o755 })
  const port = await sparePort()
  const child = spawn('dotnet', [packageHostDll, 'host'], {
    env: {
      ...process.env,
      DOTNET_ENVIRONMENT: 'Development',
      Extensions__Path: extensions,
      PackageHost__Address: '127.0.0.1',
      PackageHost__Port: String(port),
      PackageBuild__Trivy: trivy,
    },
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
    await rm(extensions, { recursive: true, force: true })
    await rm(scripts, { recursive: true, force: true })
  }

  for (let second = 0; second < 60; second++) {
    if (exitCode !== undefined) break
    if (second > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
    if (exitCode !== undefined) break
    if (await accepts(port)) return { address: `127.0.0.1:${port}`, stop, output: () => output }
  }
  const reason =
    exitCode !== undefined
      ? `The package host exited with code ${exitCode}.`
      : 'The package host never listened on its port within 60 seconds.'
  await stop()
  throw new Error(`${reason} Package host output:\n${output}`)
}

/** Whether a TCP connect to the loopback `port` succeeds. */
function accepts(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = net.connect({ host: '127.0.0.1', port })
    socket.once('connect', () => {
      socket.destroy()
      resolve(true)
    })
    socket.once('error', () => {
      socket.destroy()
      resolve(false)
    })
  })
}
