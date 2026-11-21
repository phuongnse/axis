import { spawn, type ChildProcess } from 'node:child_process'
import { chmod, mkdtemp, rm, writeFile } from 'node:fs/promises'
import net from 'node:net'
import os from 'node:os'
import path from 'node:path'
import { sparePort } from './activate'
import { extensionsPath } from './server-settings'

/** The built package host, which scripts/build.sh builds in Release. */
const hostDll = path.resolve(import.meta.dirname, '..', '..', 'src', 'Axis.PackageHost', 'bin', 'Release', 'net10.0', 'Axis.PackageHost.dll')

/** A Trivy that reports no vulnerability, as the integration tests' stub does. The host's scan fails closed without a Trivy. */
const stubTrivy = `#!/bin/sh
cat <<'JSON'
{"Results":[]}
JSON
`

/** How long the host has to accept a connection on its port. */
const readyTimeout = 30_000

/**
 * Starts `Axis.PackageHost host` for one spec, on a spare loopback port, with the run's `Extensions:Path` and a stub Trivy
 * that reports no findings. The spec's own publisher names it in `Extensions:Runners:Application`, so the shared package
 * host of scripts/e2e.sh is not used. Returns once the port accepts a connection, and throws with the host's output when
 * the host exits first. `stop` ends the host with SIGTERM, then SIGKILL after 15 seconds, and removes the stub's folder.
 * `output` gives what the host printed. `address` is the `host:port` that `Extensions:Runners:Application` takes.
 */
export async function startPackageHost(): Promise<{ address: string; stop(): Promise<void>; output(): string }> {
  const scripts = await mkdtemp(path.join(os.tmpdir(), 'axis-package-host-'))
  let host: ChildProcess | undefined
  try {
    const trivy = path.join(scripts, 'trivy')
    await writeFile(trivy, stubTrivy)
    await chmod(trivy, 0o700)

    const port = await sparePort()
    const child = spawn('dotnet', [hostDll, 'host'], {
      env: {
        ...process.env,
        DOTNET_ENVIRONMENT: 'Development',
        Extensions__Path: extensionsPath,
        PackageHost__Address: '127.0.0.1',
        PackageHost__Port: String(port),
        PackageBuild__Trivy: trivy,
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    })
    host = child
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

    const deadline = Date.now() + readyTimeout
    while (!(await accepts(port))) {
      if (exitCode !== undefined) {
        throw new Error(`The package host exited with code ${exitCode} at its start. Host output:\n${output}`)
      }
      if (Date.now() > deadline) {
        throw new Error(`The package host did not accept connections within ${readyTimeout / 1000} seconds. Host output:\n${output}`)
      }
      await new Promise((resolve) => setTimeout(resolve, 250))
    }

    const stop = async () => {
      try {
        if (exitCode === undefined) {
          child.kill('SIGTERM')
          // A host that ignores SIGTERM must not hang the spec.
          const killer = setTimeout(() => child.kill('SIGKILL'), 15_000)
          await exited
          clearTimeout(killer)
        }
      } finally {
        await rm(scripts, { recursive: true, force: true })
      }
    }
    return { address: `127.0.0.1:${port}`, stop, output: () => output }
  } catch (error) {
    host?.kill('SIGKILL')
    await rm(scripts, { recursive: true, force: true })
    throw error
  }
}

/** Whether a connection to the loopback `port` succeeds. */
function accepts(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = net.connect(port, '127.0.0.1')
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
