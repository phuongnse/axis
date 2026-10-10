// Smoke test for scripts/dev.sh. Needs Docker with the compose plugin, the .NET SDK, Node and
// npm. It runs the script in a temporary copy of the repository, with its own Compose project
// and free ports, so it works on a fresh clone and cannot clash with a running dev session.
import assert from 'node:assert/strict'
import { execFileSync, spawn } from 'node:child_process'
import fs from 'node:fs'
import net from 'node:net'
import os from 'node:os'
import path from 'node:path'
import { after, before, describe, it } from 'node:test'
import { fileURLToPath } from 'node:url'

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const excluded = new Set(['.git', 'node_modules', 'bin', 'obj', 'artifacts', 'wwwroot', 'playwright-report', 'test-results'])
const project = `axis-dev-test-${process.pid}`
const processPattern = /dotnet-watch|dotnet watch|Axis\.Server|vite/

let copy
let scratch
let env
let ports
const running = []

function freePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer()
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address()
      server.close(() => resolve(port))
    })
  })
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

function isAlive(pid) {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

/** Starts scripts/dev.sh in the copy and collects its output. */
function startDev(extraEnv = {}) {
  const child = spawn('bash', ['scripts/dev.sh'], {
    cwd: copy,
    env: { ...env, ...extraEnv },
    stdio: ['ignore', 'pipe', 'pipe'],
    detached: true,
  })
  const run = { child, stdout: '', stderr: '', exitCode: undefined, exited: undefined }
  child.stdout.on('data', (chunk) => (run.stdout += chunk))
  child.stderr.on('data', (chunk) => (run.stderr += chunk))
  run.exited = new Promise((resolve) => {
    child.once('close', (code, signal) => {
      run.exitCode = code ?? (signal ? 128 : 1)
      resolve(run.exitCode)
    })
  })
  running.push(run)
  return run
}

async function exitsWithin(run, ms) {
  let timer
  const timeout = new Promise((resolve) => (timer = setTimeout(() => resolve('timeout'), ms)))
  const result = await Promise.race([run.exited, timeout])
  clearTimeout(timer)
  assert.notEqual(result, 'timeout', `dev.sh did not exit within ${ms} ms.\n${run.stdout}\n${run.stderr}`)
  return result
}

async function waitReady(run, timeoutMs = 600_000) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    assert.equal(run.exitCode, undefined, `dev.sh exited early with ${run.exitCode}.\n${run.stdout}\n${run.stderr}`)
    try {
      const response = await fetch(`http://localhost:${ports.web}/health/ready`)
      if (response.status === 200) return
    } catch {
      // Not listening yet.
    }
    await sleep(1000)
  }
  assert.fail(`Not ready after ${timeoutMs} ms.\n${run.stdout}\n${run.stderr}`)
}

/** The server, dotnet watch and Vite processes below the dev.sh process. */
function runProcs(rootPid) {
  const rows = execFileSync('ps', ['-A', '-o', 'pid=,ppid=,command='], { encoding: 'utf8' })
    .split('\n')
    .map((line) => line.trim().match(/^(\d+)\s+(\d+)\s+(.*)$/))
    .filter(Boolean)
    .map(([, pid, ppid, command]) => ({ pid: Number(pid), ppid: Number(ppid), command }))
  const found = []
  const visit = (pid) => {
    for (const row of rows.filter((r) => r.ppid === pid)) {
      found.push(row)
      visit(row.pid)
    }
  }
  visit(rootPid)
  return found.filter((row) => processPattern.test(row.command))
}

function interrupt(run) {
  process.kill(-run.child.pid, 'SIGINT')
}

function killLeftovers(run) {
  if (run.exitCode !== undefined) return
  try {
    process.kill(-run.child.pid, 'SIGKILL')
  } catch {
    // Already gone.
  }
}

describe('scripts/dev.sh', { concurrency: false }, () => {
  before(async () => {
    scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'axis-dev-test-'))
    copy = path.join(scratch, 'repo')
    fs.cpSync(repoRoot, copy, {
      recursive: true,
      filter: (source) => !excluded.has(path.basename(source)),
    })
    ports = { postgres: await freePort(), server: await freePort(), web: await freePort() }
    env = {
      ...process.env,
      COMPOSE_PROJECT_NAME: project,
      AXIS_POSTGRES_PORT: String(ports.postgres),
      AXIS_SERVER_PORT: String(ports.server),
      AXIS_WEB_PORT: String(ports.web),
      MSBUILDDISABLENODEREUSE: '1',
    }
  })

  after(() => {
    for (const run of running) killLeftovers(run)
    if (copy) {
      try {
        execFileSync('docker', ['compose', '-p', project, 'down', '-v'], { cwd: copy, stdio: 'ignore' })
      } catch {
        // Docker may be gone.
      }
    }
    if (scratch) fs.rmSync(scratch, { recursive: true, force: true })
  })

  it('installs the SPA packages, serves the app through Vite and stops cleanly on SIGINT', async () => {
    const run = startDev()
    await waitReady(run)
    assert.match(run.stdout, /\[dev\] Installing SPA packages/)

    const page = await fetch(`http://localhost:${ports.web}/`)
    assert.equal(page.status, 200)
    assert.match(await page.text(), /<div id="root">/)

    const procs = runProcs(run.child.pid)
    assert.ok(procs.some((p) => /vite/.test(p.command)), `No Vite process in:\n${JSON.stringify(procs)}`)
    assert.ok(procs.some((p) => /Axis\.Server/.test(p.command)), `No server process in:\n${JSON.stringify(procs)}`)

    interrupt(run)
    assert.equal(await exitsWithin(run, 60_000), 130)
    await sleep(500)
    for (const p of procs) assert.equal(isAlive(p.pid), false, `Still running: ${p.command}`)

    const services = execFileSync('docker', ['compose', '-p', project, 'ps', '--status', 'running', '--services'], {
      cwd: copy,
      encoding: 'utf8',
    })
    assert.match(services, /postgres/)
  })

  it('skips the install on the second run', async () => {
    const run = startDev()
    await waitReady(run)
    assert.doesNotMatch(run.stdout, /Installing SPA packages/)
    interrupt(run)
    await exitsWithin(run, 60_000)
  })

  it('exits non-zero and stops the SPA when the server exits on its own', async () => {
    const empty = fs.mkdtempSync(path.join(scratch, 'empty-app-'))
    const run = startDev({ ActivateOnStartup__0: empty })
    const code = await exitsWithin(run, 300_000)
    assert.notEqual(code, 0)
    assert.match(run.stdout, /\[dev\] The server exited/)
    await sleep(500)
    // dev.sh is gone, so find the run's Vite process by its port.
    const leftovers = execFileSync('ps', ['-A', '-o', 'pid=,command='], { encoding: 'utf8' })
      .split('\n')
      .filter((line) => /vite/.test(line) && line.includes(`--port ${ports.web}`))
    assert.deepEqual(leftovers, [])
  })

  it('exits non-zero at once when Docker is not running', async () => {
    const run = startDev({ DOCKER_HOST: 'unix:///nonexistent/docker.sock' })
    const code = await exitsWithin(run, 15_000)
    assert.notEqual(code, 0)
    assert.match(run.stderr, /Docker is not running/)
  })
})
