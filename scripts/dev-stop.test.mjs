// Tests scripts/lib/dev-stop.sh without Docker. A job starts a child after dev.sh has listed the
// job's processes. The child ignores SIGINT and SIGTERM. The stop must still leave nothing behind.
import assert from 'node:assert/strict'
import { execFile, execFileSync, spawn } from 'node:child_process'
import path from 'node:path'
import { promisify } from 'node:util'
import { after, describe, it } from 'node:test'
import { fileURLToPath } from 'node:url'

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const execFileAsync = promisify(execFile)
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

const leaderScript = `trap '(trap "" INT TERM; exec sleep 600) & exit 0' INT; echo ready; while :; do sleep 0.1; done`

/** Resolves once the leader prints its ready line, which means its SIGINT trap is set. */
function waitForReady(child) {
  return new Promise((resolve, reject) => {
    let output = ''
    child.stdout.on('data', (chunk) => {
      output += chunk
      if (output.includes('ready')) resolve()
    })
    child.once('exit', (code) => reject(new Error(`The leader exited with ${code} before it was ready.`)))
  })
}

/** The processes of a group that still run. A zombie has exited, so it does not count. */
function groupMembers(pgid) {
  return execFileSync('ps', ['-A', '-o', 'pid=,pgid=,stat='], { encoding: 'utf8' })
    .split('\n')
    .map((line) => line.trim().split(/\s+/))
    .filter(([, group, state]) => Number(group) === pgid && state && !state.startsWith('Z'))
    .map(([pid, , state]) => `${pid} ${state}`)
}

async function waitForEmptyGroup(pgid, ms) {
  const deadline = Date.now() + ms
  let members = groupMembers(pgid)
  while (members.length > 0 && Date.now() < deadline) {
    await sleep(100)
    members = groupMembers(pgid)
  }
  return members
}

describe('scripts/lib/dev-stop.sh', () => {
  const leaders = []

  after(() => {
    for (const pid of leaders) {
      try {
        process.kill(-pid, 'SIGKILL')
      } catch {
        // The group has already gone.
      }
    }
  })

  it('leaves no process of a job group behind when a child starts after the stop begins', async () => {
    const leader = spawn('bash', ['-c', leaderScript], {
      cwd: repoRoot,
      detached: true,
      stdio: ['ignore', 'pipe', 'ignore'],
    })
    leaders.push(leader.pid)
    await waitForReady(leader)

    await execFileAsync('bash', ['-c', 'source scripts/lib/dev-stop.sh; stop_processes "$0"', String(leader.pid)], {
      cwd: repoRoot,
    })

    assert.deepEqual(await waitForEmptyGroup(leader.pid, 5000), [])
  })
})
