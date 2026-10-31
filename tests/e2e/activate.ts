import { spawn } from 'node:child_process'
import net from 'node:net'
import { serverDll, serverEnv, serverProject } from './server-settings'

const readyTimeout = 120_000

/** A free port on the loopback address. Another process can take it before the caller listens. */
export async function sparePort(): Promise<number> {
  const probe = net.createServer()
  await new Promise<void>((resolve, reject) => {
    probe.once('error', reject)
    probe.listen(0, '127.0.0.1', resolve)
  })
  const { port } = probe.address() as net.AddressInfo
  await new Promise<void>((resolve) => probe.close(() => resolve()))
  return port
}

async function answersReady(url: string): Promise<boolean> {
  try {
    return (await fetch(`${url}/health/ready`)).status === 200
  } catch {
    return false
  }
}

/**
 * Starts the built server with exactly `env` added to the environment of this process, and returns once it
 * answers /health/ready at `url`. `stop` ends it with SIGTERM, then SIGKILL after 15 seconds.
 * The server activates before it listens, so the first answer of /health/ready means its releases are active.
 * Throws an Error with the server output when it exits or does not become ready.
 */
export async function runServer(
  url: string,
  env: Record<string, string>,
): Promise<{ stop(): Promise<void> }> {
  const child = spawn('dotnet', [serverDll], {
    cwd: serverProject,
    env: { ...process.env, ...env },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  let output = ''
  child.stdout.on('data', (chunk) => (output += chunk))
  child.stderr.on('data', (chunk) => (output += chunk))
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
      // A server that ignores SIGTERM must not hang the spec.
      const killer = setTimeout(() => child.kill('SIGKILL'), 15_000)
      await exited
      clearTimeout(killer)
    }
  }
  const failure = (reason: string) => new Error(`${reason} Server output:\n${output}`)

  try {
    const deadline = Date.now() + readyTimeout
    for (;;) {
      if (exitCode !== undefined) {
        throw failure(`The server exited with code ${exitCode} before it became ready.`)
      }
      if (await answersReady(url)) return { stop }
      if (Date.now() > deadline) {
        throw failure(`The server did not become ready within ${readyTimeout / 1000} seconds.`)
      }
      await new Promise((resolve) => setTimeout(resolve, 250))
    }
  } catch (error) {
    await stop()
    throw error
  }
}

/**
 * Starts the built server with `env` over the settings of the main E2E server, and returns once it answers
 * /health/ready on `port`, or on a spare port. `stop` ends it, see `runServer`.
 * Throws an Error with the server output when it exits or does not become ready.
 */
export async function startServer(
  env: Record<string, string>,
  port?: number,
): Promise<{ url: string; port: number; stop(): Promise<void> }> {
  const listenPort = port ?? (await sparePort())
  const url = `http://127.0.0.1:${listenPort}`
  const { stop } = await runServer(url, { ...serverEnv(url), ...env })
  return { url, port: listenPort, stop }
}

/**
 * Activates the application in `folder` as a new release, while the E2E run goes on. It starts the built
 * server once more on a spare port, with the settings of the main E2E server, and stops it when it is
 * ready. The main server reads the active release on each request and serves it at once.
 * `ActivateOnStartup__0` replaces only the first entry of appsettings.Development.json, so the leave request
 * sample, which that file lists second, activates again as an unchanged release.
 * Throws an Error with the server output when the folder does not activate.
 */
export async function activateFolder(folder: string): Promise<void> {
  const server = await startServer({ ActivateOnStartup__0: folder })
  await server.stop()
}
