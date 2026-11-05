import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// The cell scenario imports k6 modules, which Node cannot load, so this test reads tests/load/cell.js as text. It keeps the
// numbers of the cell load test in docs/reference/performance.md equal to the numbers and thresholds of the scenario.
const root = fileURLToPath(new URL('..', import.meta.url))
const scenario = readFileSync(`${root}tests/load/cell.js`, 'utf8')
const performance = readFileSync(`${root}docs/reference/performance.md`, 'utf8')

const cellSection = performance.slice(performance.indexOf('### Cell load test'))
const objectivesSection = performance.slice(performance.indexOf('## Objectives'), performance.indexOf('## Reference environment'))

const constant = (name) => {
  const match = new RegExp(`^const ${name} = (\\d+)$`, 'm').exec(scenario)
  assert.ok(match, `cell.js has no integer constant ${name}`)
  return Number(match[1])
}

const thresholds = new Map([...scenario.matchAll(/^\s*'([^']+)': \['([^']+)'\],?$/gm)].map((match) => [match[1], match[2]]))

describe('tests/load/cell.js and the cell load test of performance.md', () => {
  test('the tenants, the warm-up and the run are the numbers of the doc', () => {
    const busy = cellSection.match(/(\d+) busy tenants, each with (\d+) users/)
    const idle = cellSection.match(/([\d,]+) idle tenants/)
    const warmUp = cellSection.match(/warm-up of (\d+) minutes/)
    assert.ok(busy && idle && warmUp, 'performance.md does not state the cell load test numbers')
    assert.match(cellSection, /over one hour/)
    assert.deepEqual(
      {
        busyTenants: constant('busyTenants'),
        usersPerBusyTenant: constant('usersPerBusyTenant'),
        idleTenants: constant('idleTenants'),
        warmUpMinutes: constant('warmUpMinutes'),
        measuredMinutes: constant('measuredMinutes'),
      },
      {
        busyTenants: Number(busy[1]),
        usersPerBusyTenant: Number(busy[2]),
        idleTenants: Number(idle[1].replace(/,/g, '')),
        warmUpMinutes: Number(warmUp[1]),
        measuredMinutes: 60,
      },
    )
  })

  test('the busy scenario runs 50 tenants of 10 users, and the idle scenario one user per idle tenant', () => {
    assert.match(scenario, /busy: \{[^}]*vus: busyTenants \* usersPerBusyTenant,/)
    assert.match(scenario, /idle: \{[^}]*vus: idleTenants,/)
  })

  test('the thresholds carry the objectives and the sign-in p95 of the doc', () => {
    const reads = objectivesSection.match(/^\| Reads\s*\|[^\n]*p95 under (\d+) ms/m)
    const writes = objectivesSection.match(/^\| Writes\s*\|[^\n]*p95 under (\d+) ms/m)
    const errors = objectivesSection.match(/^\| Errors\s*\|[^\n]*Under ([\d.]+)%/m)
    const signIn = cellSection.match(/p95 is under (\d+) s\b/)
    assert.ok(reads && writes && errors && signIn, 'performance.md does not state the objectives or the sign-in p95')

    assert.equal(thresholds.get('http_req_duration{scenario:busy,objective:reads,phase:run}'), `p(95)<${reads[1]}`)
    assert.equal(thresholds.get('http_req_duration{scenario:busy,objective:writes,phase:run}'), `p(95)<${writes[1]}`)
    const rate = thresholds.get('server_errors{scenario:busy,phase:run}')
    assert.ok(rate?.startsWith('rate<'), rate)
    assert.equal((Number(rate.slice('rate<'.length)) * 100).toFixed(4), Number(errors[1]).toFixed(4))
    assert.equal(thresholds.get('idle_sign_in{phase:run}'), `p(95)<${Number(signIn[1]) * 1000}`)
  })

  test('every threshold reads the run phase only, so the warm-up does not count', () => {
    assert.ok(thresholds.size > 0)
    for (const key of thresholds.keys()) assert.match(key, /phase:run/, key)
  })
})
