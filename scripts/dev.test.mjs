// Smoke test for scripts/dev.sh. Needs Docker with the compose plugin, the .NET SDK, Node and
// npm. It runs the script in a temporary copy of the repository, with its own Compose project,
// whose services the cases share, and free ports, so it works on a fresh clone and cannot clash
// with a running dev session.
import assert from 'node:assert/strict'
import { execFileSync, spawn } from 'node:child_process'
import fs from 'node:fs'
import http from 'node:http'
import net from 'node:net'
import os from 'node:os'
import path from 'node:path'
import { after, before, describe, it } from 'node:test'
import { fileURLToPath } from 'node:url'

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const excluded = new Set(['.git', 'node_modules', 'bin', 'obj', 'artifacts', 'wwwroot', 'playwright-report', 'test-results'])
const project = `axis-dev-test-${process.pid}`
const processPattern = /dotnet-watch|dotnet watch|Axis\.Server|Axis\.Worker|Axis\.Admin|Axis\.Agent|vite/

let copy
let scratch
let env
const ports = {}
const running = []

const handedOut = new Set()

/** A free port that no earlier call returned, because a closed port can be handed out again at once. */
async function freePort() {
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

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

function isAlive(pid) {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

// Compose keeps these ports bound for the whole run, so they are picked once, before the first case.
async function pickServicePorts() {
  ports.postgres = await freePort()
  ports.keycloak = await freePort()
  ports.purchasing = await freePort()
  ports.mailpitSmtp = await freePort()
  ports.mailpitWeb = await freePort()
}

// A port picked earlier can be taken by another process before the app binds it, so each start picks its own.
async function pickAppPorts() {
  ports.server = await freePort()
  ports.web = await freePort()
  ports.workerHealth = await freePort()
  ports.agentHealth = await freePort()
  env = {
    ...process.env,
    COMPOSE_PROJECT_NAME: project,
    AXIS_POSTGRES_PORT: String(ports.postgres),
    AXIS_KEYCLOAK_PORT: String(ports.keycloak),
    AXIS_PURCHASING_PORT: String(ports.purchasing),
    AXIS_MAILPIT_SMTP_PORT: String(ports.mailpitSmtp),
    AXIS_MAILPIT_WEB_PORT: String(ports.mailpitWeb),
    AXIS_SERVER_PORT: String(ports.server),
    AXIS_WEB_PORT: String(ports.web),
    AXIS_AGENT_HEALTH_PORT: String(ports.agentHealth),
    Health__Urls: `http://127.0.0.1:${ports.workerHealth}`,
    MSBUILDDISABLENODEREUSE: '1',
  }
}

/** Starts scripts/dev.sh in the copy with fresh app ports and collects its output. */
async function startDev(extraEnv = {}) {
  await pickAppPorts()
  const child = spawn('bash', ['scripts/dev.sh'], {
    cwd: copy,
    env: { ...env, ...extraEnv },
    stdio: ['ignore', 'pipe', 'pipe'],
    detached: true,
  })
  return track(child)
}

/** A copy of the purchase requests sample in the scratch folder, so that a case can edit it. */
function copyPurchaseRequests(name) {
  const dir = path.join(scratch, name)
  fs.cpSync(path.join(repoRoot, 'samples/apps/purchase-requests'), dir, { recursive: true })
  return dir
}

/** Collects the output and the exit of a child, and remembers it for the final cleanup. */
function track(child) {
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

/** Waits until the output matches the pattern. Fails if dev.sh exits first. */
async function waitForOutput(run, pattern, timeoutMs = 300_000) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (pattern.test(run.stdout)) return
    assert.equal(run.exitCode, undefined, `dev.sh exited early with ${run.exitCode}.\n${run.stdout}\n${run.stderr}`)
    await sleep(1000)
  }
  assert.fail(`No output matching ${pattern} after ${timeoutMs} ms.\n${run.stdout}\n${run.stderr}`)
}

/** The leftover processes of a finished run: its Vite by its port, and the server, worker, publisher and agent host of this copy. */
function leftoverProcs() {
  return execFileSync('ps', ['-A', '-o', 'pid=,command='], { encoding: 'utf8' })
    .split('\n')
    .filter(
      (line) =>
        (/vite/.test(line) && line.includes(`--port ${ports.web}`)) ||
        (/Axis\.(Server|Worker|Admin|Agent)/.test(line) && line.includes(copy)),
    )
}

/** The server, worker, publisher, agent host, dotnet watch and Vite processes below the dev.sh process. */
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

/**
 * Every process of the copy: its command line contains the copy's path, or its working directory
 * is inside the copy. dev.sh runs each job in its own process group, and the output prefixer
 * subshells and dotnet watch carry the path only in their working directory.
 */
function copyProcs() {
  const rows = execFileSync('ps', ['-A', '-o', 'pid=,command='], { encoding: 'utf8' })
    .split('\n')
    .map((line) => line.trim().match(/^(\d+)\s+(.*)$/))
    .filter(Boolean)
    .map(([, pid, command]) => ({ pid: Number(pid), command }))
    .filter((row) => row.pid !== process.pid)
  const cwds = new Map()
  if (fs.existsSync('/proc/self')) {
    for (const { pid } of rows) {
      try {
        cwds.set(pid, fs.readlinkSync(`/proc/${pid}/cwd`))
      } catch {
        // The process is gone, or belongs to someone else.
      }
    }
  } else {
    try {
      let pid
      for (const line of execFileSync('lsof', ['-d', 'cwd', '-Fpn'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).split('\n')) {
        if (line.startsWith('p')) pid = Number(line.slice(1))
        else if (line.startsWith('n') && pid !== undefined) cwds.set(pid, line.slice(1))
      }
    } catch {
      // lsof is missing, or it exited non-zero after listing what it could.
    }
  }
  const inCopy = (dir) => dir === copy || dir.startsWith(`${copy}${path.sep}`)
  return rows.filter((row) => row.command.includes(copy) || (cwds.has(row.pid) && inCopy(cwds.get(row.pid))))
}

/** Kills everything a run left behind, then closes its pipes so that Node does not wait for them. */
function killLeftovers(run) {
  try {
    process.kill(-run.child.pid, 'SIGKILL')
  } catch {
    // Already gone.
  }
  for (const { pid } of copyProcs()) {
    try {
      process.kill(pid, 'SIGKILL')
    } catch {
      // Already gone.
    }
  }
  run.child.stdout.destroy()
  run.child.stderr.destroy()
  run.child.unref()
}

describe('scripts/dev.sh', { concurrency: false }, () => {
  before(async () => {
    scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'axis-dev-test-'))
    copy = path.join(scratch, 'repo')
    fs.cpSync(repoRoot, copy, {
      recursive: true,
      filter: (source) => !excluded.has(path.basename(source)),
    })
    await pickServicePorts()
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

  it('installs the SPA packages, serves the app, restarts only the server when application configuration changes, keeps running through an edit that hot reload cannot apply and stops cleanly on SIGINT', async () => {
    const appCopy = copyPurchaseRequests('app-copy')
    const texts = path.join(appCopy, 'texts/en.json')
    const file = path.join(copy, 'src/Axis.Server/Health/DatabaseHealthCheck.cs')
    const original = fs.readFileSync(file, 'utf8')
    // The preview tenant activates the same sample on each start, so only the main tenant's lines count.
    const activations = () =>
      run.stdout.split('\n').filter((line) => /Activated application PurchaseRequests .* for tenant default\.$/.test(line.trimEnd())).length
    // The worker logs the same line for its health address, so only the server's lines count.
    const listening = () => run.stdout.split('\n').filter((line) => /\[server\].*Now listening on/.test(line)).length
    const workerPids = () =>
      runProcs(run.child.pid)
        .filter((p) => /Axis\.Worker/.test(p.command) && !/dotnet[- ]watch/.test(p.command))
        .map((p) => p.pid)

    const run = await startDev({ ActivateOnStartup__0: appCopy })
    await waitReady(run)
    assert.match(run.stdout, /\[dev\] Installing SPA packages/)

    // The built-in module organization and the sample module department-budgets are installed where Modules:Path points, and the server loads them.
    assert.ok(fs.existsSync(path.join(copy, 'artifacts/modules/department-budgets/1.2.0/module.json')))
    assert.ok(fs.existsSync(path.join(copy, 'artifacts/modules/organization/1.0.0/module.json')))
    assert.ok(!fs.existsSync(path.join(copy, 'artifacts/modules', 'departments')))
    assert.doesNotMatch(run.stdout, /The module version is not loaded/)

    // The templates folder exists where Templates:Path points, even with no sample template, or the server would stop.
    assert.ok(fs.statSync(path.join(copy, 'artifacts/templates')).isDirectory())
    assert.doesNotMatch(run.stdout, /The template version is not loaded/)

    // The server creates the applications folder from Studio:ApplicationsPath at start. The copy excludes artifacts,
    // so only the server can have made it.
    assert.ok(fs.statSync(path.join(copy, 'artifacts/studio-apps')).isDirectory())

    // Keycloak serves the development realm, and starts before the server.
    const discovery = await fetch(`http://localhost:${ports.keycloak}/realms/axis/.well-known/openid-configuration`)
    assert.equal(discovery.status, 200)
    const keycloakStart = run.stdout.indexOf('Keycloak on port')
    assert.ok(keycloakStart >= 0, run.stdout)
    assert.ok(keycloakStart < run.stdout.indexOf('[server]'), `Server output before Keycloak started:\n${run.stdout}`)

    // The purchasing system stub answers on its port.
    const purchasing = await fetch(`http://localhost:${ports.purchasing}/health`)
    assert.equal(purchasing.status, 200)

    // Mailpit's web page answers on its port.
    const mailpit = await fetch(`http://localhost:${ports.mailpitWeb}/readyz`)
    assert.equal(mailpit.status, 200)

    // The preview tenant default-preview resolves by its host. The request goes to 127.0.0.1 with the
    // Host header, because Node does not always resolve *.localhost. An unknown host is not a tenant.
    const siteStatus = (host) =>
      new Promise((resolve, reject) => {
        const request = http.request(
          { host: '127.0.0.1', port: ports.server, path: '/api/site', headers: { Host: host } },
          (response) => {
            response.resume()
            response.on('end', () => resolve(response.statusCode))
          },
        )
        request.once('error', reject)
        request.end()
      })
    assert.equal(await siteStatus(`preview.localhost:${ports.server}`), 200)
    assert.equal(await siteStatus('unknown.localhost'), 404)

    // The server activated the four samples on the preview tenant, in its own database axis_preview.
    for (const application of ['PurchaseRequests', 'LeaveRequests', 'ServiceDesk', 'AssetManagement']) {
      assert.match(run.stdout, new RegExp(`Activated application ${application} .* for tenant default-preview\\.`))
    }
    const previewReleases = execFileSync(
      'docker',
      ['compose', '-p', project, 'exec', '-T', 'postgres', 'psql', '-U', 'axis', '-d', 'axis_preview', '-tAc', 'select count(*) from axis.active_releases'],
      { cwd: copy, encoding: 'utf8' },
    )
    assert.ok(Number(previewReleases.trim()) >= 4, `axis.active_releases in axis_preview holds ${previewReleases}`)

    // The worker starts only after the server has migrated the tenant databases.
    await waitForOutput(run, /\[worker\] .*Tenant default is ready for work/)
    // The worker answers ready only once it has found every tenant migrated, so it also waits for the preview tenant.
    await waitForOutput(run, /\[worker\] .*Tenant default-preview is ready for work/)
    const listeningAt = run.stdout.search(/\[server\].*Now listening on/)
    assert.ok(listeningAt >= 0, run.stdout)
    assert.ok(run.stdout.indexOf('[worker]') > listeningAt, `Worker output before the server listened:\n${run.stdout}`)

    // The publisher starts after the server has migrated, and finds the tenant database migrated at once.
    await waitForOutput(run, /\[publisher\] .*default ready/)
    assert.ok(
      run.stdout.search(/\[publisher\]/) > listeningAt,
      `Publisher output before the server listened:\n${run.stdout}`,
    )

    // The worker answers ready on its health address once it has found the tenant database migrated.
    const workerReady = await fetch(`http://127.0.0.1:${ports.workerHealth}/health/ready`)
    assert.equal(workerReady.status, 200)
    assert.equal((await workerReady.json()).status, 'Healthy')

    // The agent host starts at once, idle, and answers ready on its own health address. It ignores the exported Health__Urls of the worker.
    let agentReady
    const agentDeadline = Date.now() + 120_000
    while (!agentReady && Date.now() < agentDeadline) {
      try {
        agentReady = await fetch(`http://127.0.0.1:${ports.agentHealth}/health/ready`)
      } catch {
        await sleep(1000)
      }
    }
    assert.ok(agentReady, `The agent host did not answer.\n${run.stdout}`)
    assert.equal(agentReady.status, 200)
    assert.equal((await agentReady.json()).status, 'Healthy')
    assert.match(run.stdout, /\[agent\] /)

    const page = await fetch(`http://localhost:${ports.web}/`)
    assert.equal(page.status, 200)
    assert.match(await page.text(), /<div id="root">/)

    const procs = runProcs(run.child.pid)
    assert.ok(procs.some((p) => /vite/.test(p.command)), `No Vite process in:\n${JSON.stringify(procs)}`)
    assert.ok(procs.some((p) => /Axis\.Server/.test(p.command)), `No server process in:\n${JSON.stringify(procs)}`)
    assert.ok(procs.some((p) => /Axis\.Worker/.test(p.command)), `No worker process in:\n${JSON.stringify(procs)}`)
    assert.ok(procs.some((p) => /Axis\.Admin/.test(p.command)), `No publisher process in:\n${JSON.stringify(procs)}`)
    assert.ok(procs.some((p) => /Axis\.Agent/.test(p.command)), `No agent host process in:\n${JSON.stringify(procs)}`)

    // The worker runs with its mail settings pointing at Mailpit. /proc exists on Linux only.
    const workerProc = procs.find((p) => /Axis\.Worker/.test(p.command) && !/dotnet[- ]watch/.test(p.command))
    assert.ok(workerProc, `No worker process in:\n${JSON.stringify(procs)}`)
    if (fs.existsSync(`/proc/${workerProc.pid}/environ`)) {
      const workerEnv = fs.readFileSync(`/proc/${workerProc.pid}/environ`, 'utf8').split('\0')
      assert.ok(workerEnv.includes('Email__Smtp__Host=localhost'), 'The worker has no Email__Smtp__Host=localhost')
      assert.ok(workerEnv.includes(`Email__Smtp__Port=${ports.mailpitSmtp}`), 'The worker has the wrong Email__Smtp__Port')
      assert.ok(workerEnv.includes('Email__Smtp__UseTls=false'), 'The worker has no Email__Smtp__UseTls=false')
    }

    // A configuration edit restarts only the server, so the worker keeps its process.
    const activationsBefore = activations()
    assert.equal(activationsBefore, 1, run.stdout)
    const workersBefore = workerPids()
    assert.ok(workersBefore.length > 0, `No worker process in:\n${JSON.stringify(runProcs(run.child.pid))}`)

    const content = JSON.parse(fs.readFileSync(texts, 'utf8'))
    content.texts['site.title'] = 'Purchasing, edited'
    fs.writeFileSync(texts, JSON.stringify(content, null, 2))

    const activationDeadline = Date.now() + 300_000
    while (activations() <= activationsBefore && Date.now() < activationDeadline) {
      assert.equal(run.exitCode, undefined, `dev.sh exited during the restart.\n${run.stdout}\n${run.stderr}`)
      await sleep(1000)
    }
    assert.equal(activations(), activationsBefore + 1, `The server did not activate again.\n${run.stdout}\n${run.stderr}`)
    assert.match(run.stdout, /\[dev\] Application configuration changed/)
    assert.doesNotMatch(run.stdout, /\[dev\] The server exited/)
    await waitReady(run)

    const workersAfter = workerPids()
    assert.deepEqual(workersAfter, workersBefore)
    for (const pid of workersAfter) assert.equal(isAlive(pid), true, `The worker ${pid} stopped`)

    // Making a sealed class unsealed is a rude edit: dotnet watch stops the app, builds and
    // starts it again. It prints "Exited" on the way, which must not end the session. The file is
    // restored only after dev.sh has stopped, because a second edit would restart the server again.
    const listeningBefore = listening()
    try {
      fs.writeFileSync(file, original.replace('internal sealed class', 'internal class'))

      const restartDeadline = Date.now() + 300_000
      while (listening() <= listeningBefore && Date.now() < restartDeadline) {
        assert.equal(run.exitCode, undefined, `dev.sh exited during the restart.\n${run.stdout}\n${run.stderr}`)
        await sleep(1000)
      }
      assert.equal(listening(), listeningBefore + 1, `The server did not restart.\n${run.stdout}\n${run.stderr}`)
      assert.match(run.stdout, /Restart is needed/)
      assert.match(run.stdout, /\[server\] dotnet watch .* Exited/)
      assert.doesNotMatch(run.stdout, /\[dev\] The server exited/)
      await waitReady(run)

      const beforeStop = runProcs(run.child.pid)
      interrupt(run)
      assert.equal(await exitsWithin(run, 60_000), 130, `${run.stdout}\n${run.stderr}`)
      await sleep(500)
      for (const p of beforeStop) assert.equal(isAlive(p.pid), false, `Still running: ${p.command}`)

      const services = execFileSync('docker', ['compose', '-p', project, 'ps', '--status', 'running', '--services'], {
        cwd: copy,
        encoding: 'utf8',
      })
      assert.match(services, /postgres/)
      assert.match(services, /keycloak/)
      assert.match(services, /purchasing/)
      assert.match(services, /mailpit/)
    } finally {
      fs.writeFileSync(file, original)
    }
  })

  it('skips the install on the second run and stops when a configuration edit does not activate', async () => {
    const appCopy = copyPurchaseRequests('second-run-app')
    const texts = path.join(appCopy, 'texts/en.json')
    const run = await startDev({ ActivateOnStartup__0: appCopy })
    await waitReady(run)
    assert.doesNotMatch(run.stdout, /Installing SPA packages/)

    // A configuration that does not activate stops the script, as it does at startup.
    fs.writeFileSync(texts, '{')
    assert.notEqual(await exitsWithin(run, 300_000), 0)
    assert.match(run.stdout, /\[dev\] The server exited/)
    await sleep(500)
    assert.deepEqual(leftoverProcs(), [])
  })

  it('stops everything when SIGINT arrives while the worker starts', async () => {
    const run = await startDev()
    let sent = false
    run.child.stdout.on('data', () => {
      if (!sent && /\[dev\] .*Starting the worker/.test(run.stdout)) {
        sent = true
        interrupt(run)
      }
    })
    // The 60 seconds are for the stop. A cold start builds and starts the server first, which may take
    // minutes: it migrates and activates the sample applications.
    const deadline = Date.now() + 600_000
    while (!sent && Date.now() < deadline) {
      assert.equal(run.exitCode, undefined, `dev.sh exited before the worker started.\n${run.stdout}\n${run.stderr}`)
      await sleep(500)
    }
    assert.ok(sent, `dev.sh did not start the worker.\n${run.stdout}\n${run.stderr}`)
    assert.equal(await exitsWithin(run, 60_000), 130, `${run.stdout}\n${run.stderr}`)
    assert.match(run.stdout, /\[dev\] Stopping/)
    await sleep(500)
    assert.deepEqual(
      copyProcs().filter((p) => processPattern.test(p.command)),
      [],
    )
  })

  it('exits non-zero and stops the SPA when the server exits on its own', async () => {
    const empty = fs.mkdtempSync(path.join(scratch, 'empty-app-'))
    const run = await startDev({ ActivateOnStartup__0: empty })
    const code = await exitsWithin(run, 300_000)
    assert.notEqual(code, 0)
    assert.match(run.stdout, /\[dev\] The server exited/)
    await sleep(500)
    // dev.sh is gone, so find the run's processes by the port and the copy's path.
    assert.deepEqual(leftoverProcs(), [])
  })

  it('exits non-zero and stops the server and the SPA when the worker exits on its own', async () => {
    // The worker does not start with a zero poll interval. The server ignores the setting.
    const run = await startDev({ Worker__PollInterval: '00:00:00' })
    const code = await exitsWithin(run, 300_000)
    assert.notEqual(code, 0)
    assert.match(run.stdout, /\[dev\] The worker exited/)
    assert.match(run.stdout, /Now listening on/)
    await sleep(500)
    assert.deepEqual(leftoverProcs(), [])
  })

  it('the cleanup stops processes of the copy that a run leaves behind', async () => {
    // A stand-in for a leftover worker: it ignores SIGINT, runs in its own process group, lives in
    // the copy and keeps the run's stdout open.
    const child = spawn('bash', ['-c', "set -m; (trap '' INT; exec sleep 600) & exit 0"], {
      cwd: path.join(copy, 'src/Axis.Worker'),
      stdio: ['ignore', 'pipe', 'pipe'],
      detached: true,
    })
    const run = track(child)
    await sleep(500)
    assert.notDeepEqual(copyProcs(), [])
    assert.equal(run.exitCode, undefined, 'The stand-in did not hold the pipe open.')
    killLeftovers(run)
    await exitsWithin(run, 10_000)
    await sleep(500)
    assert.deepEqual(copyProcs(), [])
  })

  it('exits non-zero at once when Docker is not running', async () => {
    const run = await startDev({ DOCKER_HOST: 'unix:///nonexistent/docker.sock' })
    const code = await exitsWithin(run, 15_000)
    assert.notEqual(code, 0)
    assert.match(run.stderr, /Docker is not running/)
  })
})
