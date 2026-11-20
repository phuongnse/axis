// Picks free host ports for the smoke tests and the authoring eval stack, and starts a Compose stack on them again
// when Docker finds a picked port taken. Docs: docs/development.md#smoke-tests
import fs from 'node:fs'
import net from 'node:net'

// Picked ports come from this range. Ports below 10000 are the fixed ports the scripts use.
const FIRST_PORT = 10000
const LAST_PORT = 65535
// The bad ports that `fetch` refuses, from https://fetch.spec.whatwg.org/#bad-port. A request to one of them fails before
// it reaches a server, so the picker never returns one. Only 10080 lies in the picked range.
const BAD_PORTS = new Set([
  1, 7, 9, 11, 13, 15, 17, 19, 20, 21, 22, 23, 25, 37, 42, 43, 53, 69, 77, 79, 87, 95, 101, 102, 103, 104, 109, 110, 111,
  113, 115, 117, 119, 123, 135, 137, 139, 143, 161, 179, 389, 427, 465, 512, 513, 514, 515, 526, 530, 531, 532, 540, 548,
  554, 556, 563, 587, 601, 636, 989, 990, 993, 995, 1719, 1720, 1723, 2049, 3659, 4045, 4190, 5060, 5061, 6000, 6566, 6665,
  6666, 6667, 6668, 6669, 6679, 6697, 10080,
])
const handedOut = new Set()

/** The range the system takes source ports for outgoing connections from, as `[first, last]`. Port 0 binds from it too. */
export function ephemeralRange() {
  try {
    const [first, last] = fs.readFileSync('/proc/sys/net/ipv4/ip_local_port_range', 'utf8').trim().split(/\s+/).map(Number)
    return [first, last]
  } catch {
    return [49152, 65535]
  }
}

/** True when the port can be bound on 127.0.0.1 right now. */
function canBind(port) {
  return new Promise((resolve) => {
    const server = net.createServer()
    server.once('error', () => resolve(false))
    server.listen(port, '127.0.0.1', () => server.close(() => resolve(true)))
  })
}

/**
 * A free port that no earlier call returned, because a closed port can be handed out again at once. The port lies outside
 * the range the system takes source ports for outgoing connections from. A connection to a port that no server holds then
 * cannot take it, so a server that restarts can bind it again. The port is also never a bad port that `fetch` refuses, so a
 * request to it reaches the server. `from` and `to` narrow the range the port is picked from.
 */
export async function freePort({ from = FIRST_PORT, to = LAST_PORT } = {}) {
  const [first, last] = ephemeralRange()
  const candidates = []
  for (let port = from; port <= to; port++) {
    if ((port < first || port > last) && !BAD_PORTS.has(port) && !handedOut.has(port)) candidates.push(port)
  }
  while (candidates.length > 0) {
    const [port] = candidates.splice(Math.floor(Math.random() * candidates.length), 1)
    if (await canBind(port)) {
      handedOut.add(port)
      return port
    }
  }
  throw new Error(`No free port from ${from} to ${to} lies outside the ephemeral range ${first}-${last} and off Fetch's bad port list.`)
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
