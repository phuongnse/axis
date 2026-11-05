// Finds the tenant cap of a staging cell for scripts/load-cell.sh (docs/reference/performance.md#cell-load-test). It runs the
// cell load test at 1,000 tenants and then at each lower count in steps of 100, with findCap of load-cell-criteria.mjs, and
// prints the first count that passes as `Cell:TenantCap=<count>`. It exits 0 with a cap, 1 when no count from 1,000 down to
// 100 passed or a step broke, and 2 for a missing setting.
// <folder> is artifacts/load-cell. When <folder>/k6/tenants.json is missing, it creates the tenants with load-cell-tenants.mjs.
// Otherwise it reuses them and first resumes the tenants that an earlier run listed in <folder>/suspended.json. A step at a
// count below 1,000 suspends the idle tenants with the highest numbers through the provisioning API, so the cell serves only
// that count: the busy tenants, the idle tenants that stay, and the tenant load-backlog. Each step then
//   1. runs AXIS_LOAD_BACKLOG_COMMAND, which gives load-backlog its 100,000 due work items,
//   2. runs tests/load/cell.js in the pinned k6 image, which writes <results>/cell-<count>/summary.json,
//   3. runs AXIS_LOAD_METRICS_COMMAND, which writes the worker metrics of the step to $AXIS_LOAD_OUTPUT,
//   4. runs AXIS_LOAD_USAGE_COMMAND, which writes the cell's peak usage to $AXIS_LOAD_OUTPUT, for example
//      { "cpu": 0.62, "memory": 0.55, "connections": 0.71 },
// and checks the files with evaluate of load-cell-criteria.mjs. The step keeps summary.json, worker-metrics.json, usage.json
// and result.txt in <results>/cell-<count>. A step with no k6 summary, or with a command that failed, stops the run: a broken
// tool is not a measurement, and stepping down after it would print a cap that is too low.
// The commands run with `bash -c` and the environment of this script, plus AXIS_LOAD_BACKLOG_TENANT, AXIS_LOAD_BACKLOG_URL and
// AXIS_LOAD_BACKLOG_ITEMS for the first, and AXIS_LOAD_STARTED, AXIS_LOAD_ENDED (ISO times of the k6 run) and AXIS_LOAD_OUTPUT
// for the other two. The settings of load-cell-tenants.mjs apply, and AXIS_CA_FILE is also mounted into the k6 container.
// Run it as `node scripts/lib/load-cell-run.mjs <folder> <seed>`.
import { randomBytes } from 'node:crypto'
import { spawnSync } from 'node:child_process'
import { chmodSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { evaluate, findCap } from './load-cell-criteria.mjs'
import { backlogTenantId, cellClient, provisionLoadTenants, tenantIds } from './load-cell-tenants.mjs'
import { k6Image } from './load-stack.mjs'

const root = fileURLToPath(new URL('../..', import.meta.url))
const results = join(root, 'artifacts/load-results')
const backlogItems = 100000

const required = [
  'AXIS_CELL_URL',
  'AXIS_CELL_TENANT_DOMAIN',
  'AXIS_IDENTITY_URL',
  'AXIS_CELL_OPERATOR_REALM',
  'AXIS_CELL_OPERATOR_CLIENT_ID',
  'AXIS_CELL_OPERATOR_CLIENT_SECRET',
  'AXIS_IDENTITY_ADMIN_CLIENT_SECRET',
  'AXIS_LOAD_APPLICATION',
  'AXIS_LOAD_ADMINISTRATOR',
  'AXIS_LOAD_BACKLOG_COMMAND',
  'AXIS_LOAD_METRICS_COMMAND',
  'AXIS_LOAD_USAGE_COMMAND',
]

// Runs a command of the operator with `bash -c` and the output on this script's own, and throws when it fails.
const runCommand = (name, command, extraEnv, count) => {
  const result = spawnSync('bash', ['-c', command], { stdio: 'inherit', env: { ...process.env, ...extraEnv } })
  if (result.status !== 0) throw new Error(`the ${name} command failed at ${count} tenants`)
}

// Runs a command that writes its answer to $AXIS_LOAD_OUTPUT, and returns the file's text.
const collect = (name, command, output, times, count) => {
  runCommand(name, command, { ...times, AXIS_LOAD_OUTPUT: output }, count)
  if (!existsSync(output)) throw new Error(`the ${name} command wrote no ${output} at ${count} tenants`)
  return readFileSync(output, 'utf8')
}

const runCell = async ({ folder, seed, env }) => {
  const tenantsFile = join(folder, 'k6/tenants.json')
  const suspendedFile = join(folder, 'suspended.json')
  const runId = `${Date.now().toString(36)}${randomBytes(3).toString('hex')}`
  const { call, waitForJob } = cellClient({ folder, env })
  const queue = (action, id) =>
    call('POST', `/api/cell/tenants/${id}/${action}`, undefined, ['-H', `Idempotency-Key: load-cell-${runId}-${action}-${id}`]).id

  let tenants
  let suspended = []
  const saveSuspended = () => writeFileSync(suspendedFile, JSON.stringify(suspended, null, 2))
  if (!existsSync(tenantsFile)) {
    console.log('Creating the load tenants')
    tenants = provisionLoadTenants({ folder, seed, env })
  } else {
    tenants = JSON.parse(readFileSync(tenantsFile, 'utf8'))
    if (existsSync(suspendedFile)) suspended = JSON.parse(readFileSync(suspendedFile, 'utf8'))
    if (suspended.length > 0) {
      console.log(`Resuming ${suspended.length} tenants that an earlier run suspended`)
      const jobs = suspended.map((id) => ({ id, job: queue('resume', id) }))
      for (const { id, job } of jobs) waitForJob(job, `the resume job ${job} of tenant ${id}`)
      suspended = []
      saveSuspended()
    }
  }
  if (!tenants.backlog) throw new Error(`${tenantsFile} lists no backlog tenant. Remove ${folder} and run again`)

  const busyCount = tenants.busy.length
  const idleIds = tenantIds(busyCount, tenants.idle.length).filter(({ busy }) => !busy).map(({ id }) => id)

  const runAt = async (count) => {
    const idle = count - busyCount - 1
    console.log(`Cell load test at ${count} tenants`)

    // The idle tenants with the highest numbers are suspended, so the cell holds and serves only `count` tenants.
    const toSuspend = idleIds.slice(idle).filter((id) => !suspended.includes(id))
    const jobs = toSuspend.map((id) => {
      const job = queue('suspend', id)
      suspended.push(id)
      return { id, job }
    })
    if (jobs.length > 0) saveSuspended()
    for (const { id, job } of jobs) waitForJob(job, `the suspend job ${job} of tenant ${id}`)

    const listFile = `tenants-${count}.json`
    writeFileSync(join(folder, 'k6', listFile), JSON.stringify({ busy: tenants.busy, idle: tenants.idle.slice(0, idle), backlog: tenants.backlog }, null, 2))
    chmodSync(join(folder, 'k6', listFile), 0o644)

    const step = join(results, `cell-${count}`)
    mkdirSync(step, { recursive: true })
    // The grafana/k6 image does not run as the host user, so the folder must be writable by all.
    chmodSync(step, 0o777)
    for (const file of ['summary.json', 'worker-metrics.json', 'usage.json', 'result.txt']) rmSync(join(step, file), { force: true })

    runCommand(
      'backlog',
      env.AXIS_LOAD_BACKLOG_COMMAND,
      { AXIS_LOAD_BACKLOG_TENANT: backlogTenantId, AXIS_LOAD_BACKLOG_URL: tenants.backlog.url, AXIS_LOAD_BACKLOG_ITEMS: String(backlogItems) },
      count,
    )

    const started = new Date().toISOString()
    const caFile = env.AXIS_CA_FILE ? resolve(env.AXIS_CA_FILE) : undefined
    // The exit code of k6 is not read: its summary and the criteria of load-cell-criteria.mjs decide.
    spawnSync(
      'docker',
      [
        'run', '--rm',
        '-v', `${join(root, 'tests/load')}:/scripts:ro`,
        '-v', `${join(folder, 'k6')}:/load:ro`,
        '-v', `${step}:/results`,
        ...(caFile ? ['-v', `${caFile}:/ca.crt:ro`, '-e', 'SSL_CERT_FILE=/ca.crt'] : []),
        '-e', `AXIS_LOAD_TENANTS=/load/${listFile}`,
        '-e', `AXIS_LOAD_IDLE_TENANTS=${idle}`,
        k6Image, 'run', '--summary-export=/results/summary.json', '/scripts/cell.js',
      ],
      { stdio: 'inherit' },
    )
    const ended = new Date().toISOString()
    const summaryFile = join(step, 'summary.json')
    if (!existsSync(summaryFile)) throw new Error(`k6 wrote no summary to ${summaryFile}`)

    const times = { AXIS_LOAD_STARTED: started, AXIS_LOAD_ENDED: ended }
    const metrics = collect('metrics', env.AXIS_LOAD_METRICS_COMMAND, join(step, 'worker-metrics.json'), times, count)
    const usageText = collect('usage', env.AXIS_LOAD_USAGE_COMMAND, join(step, 'usage.json'), times, count)
    let usage
    try {
      usage = JSON.parse(usageText)
    } catch (error) {
      throw new Error(`the usage command wrote no JSON to ${join(step, 'usage.json')} at ${count} tenants: ${error.message}`)
    }

    const { lines, met } = evaluate(JSON.parse(readFileSync(summaryFile, 'utf8')), metrics, usage, backlogTenantId)
    for (const line of lines) console.log(line)
    writeFileSync(join(step, 'result.txt'), `${lines.join('\n')}\n`)
    console.log(`${count} tenants ${met ? 'passed' : 'failed'}`)
    return met
  }

  return findCap(runAt)
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [folder, seed] = process.argv.slice(2)
  const missing = required.filter((name) => !process.env[name])
  if (!folder || seed === undefined || missing.length > 0) {
    if (missing.length > 0) console.error(`Missing environment: ${missing.join(', ')}`)
    console.error('Usage: node scripts/lib/load-cell-run.mjs <folder> <seed>')
    process.exit(2)
  }
  try {
    const cap = await runCell({ folder: resolve(folder), seed, env: process.env })
    if (cap === null) {
      console.error('No count from 1000 down to 100 passed, so the cell has no cap.')
      process.exitCode = 1
    } else {
      console.log(`Cell:TenantCap=${cap}`)
    }
  } catch (error) {
    console.error(error.message)
    process.exitCode = 1
  }
}
