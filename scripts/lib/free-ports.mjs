// Picks free host ports for the smoke tests and the authoring eval stack, and starts a Compose stack on them again
// when Docker finds a picked port taken. Docs: docs/development.md#smoke-tests
import net from 'node:net'

const handedOut = new Set()

/** A free port that no earlier call returned, because a closed port can be handed out again at once. */
export async function freePort() {
  for (;;) {
    const port = await new Promise((resolve, reject) => {
      const server = net.createServer()
      server.once('error', reject)
      server.listen(0, '127.0.0.1', () => {
        const { port } = server.address()
        server.close(() => resolve(port))
      })
    })
    if (!handedOut.has(port)) {
      handedOut.add(port)
      return port
    }
  }
}

/** True when the output of `docker compose up` says that Docker could not bind a host port because another process holds it. */
export const isPortClash = (output) => /port is already allocated|address already in use|ports are not available/i.test(output)

/**
 * A port that was free when it was picked can be taken before Docker binds it. Docker then fails the start with a bind
 * error, so `pick` chooses new ports and the start is tried again. Any other result returns at once.
 * `pick` sets the ports, `start` returns a spawnSync-like `{ status, stdout, stderr }` and `cleanUp` runs between two attempts.
 * It returns the result of the last attempt.
 */
export async function startOnFreePorts({ pick, start, cleanUp = () => {}, attempts = 3 }) {
  let result
  for (let attempt = 1; attempt <= attempts; attempt++) {
    await pick()
    result = await start()
    if (result.status === 0 || !isPortClash(result.stderr ?? '')) return result
    if (attempt < attempts) await cleanUp()
  }
  return result
}
