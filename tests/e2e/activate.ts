import { spawn } from 'node:child_process'
import net from 'node:net'
import { serverDll, serverEnv, serverProject } from './server-settings'

const readyTimeout = 120_000

/** A free port on the loopback address. Another process can take it before the caller listens. */
async function sparePort(): Promise<number> {
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
 * Activates the application in `folder` as a new release, while the E2E run goes on. It starts the built
 * server once more on a spare port, with the settings of the main E2E server, and stops it when it is
 * ready. The server activates before it listens, so the first answer of /health/ready means the release
 * is active. The main server reads the active release on each request and serves it at once.
 * Throws an Error with the server output when the folder does not activate.
 */
export async function activateFolder(folder: string): Promise<void> {
  const url = `http://127.0.0.1:${await sparePort()}`
  const child = spawn('dotnet', [serverDll], {
    cwd: serverProject,
    env: { ...process.env, ...serverEnv(url), ActivateOnStartup__0: folder },
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
  const failure = (reason: string) => new Error(`${reason} Server output:\n${output}`)

  try {
    const deadline = Date.now() + readyTimeout
    for (;;) {
      if (exitCode !== undefined) {
        throw failure(`The server exited with code ${exitCode} before it activated ${folder}.`)
      }
      if (await answersReady(url)) return
      if (Date.now() > deadline) {
        throw failure(`The server did not become ready within ${readyTimeout / 1000} seconds for ${folder}.`)
      }
      await new Promise((resolve) => setTimeout(resolve, 250))
    }
  } finally {
    if (exitCode === undefined) {
      child.kill('SIGTERM')
      // A server that ignores SIGTERM must not hang the spec.
      const killer = setTimeout(() => child.kill('SIGKILL'), 15_000)
      await exited
      clearTimeout(killer)
    }
  }
}
