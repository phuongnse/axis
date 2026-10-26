import assert from 'node:assert/strict'
import { test } from 'node:test'
import { describeNotReady, parsePs } from './lib/compose-status.mjs'

const healthy = { Service: 'postgres', State: 'running', Health: 'healthy', ExitCode: 0 }

test('an unhealthy server is named with the output of its last health check', () => {
  const message = describeNotReady(
    [healthy, { Service: 'server', State: 'running', Health: 'unhealthy', ExitCode: 0 }],
    { server: 'curl: (7) Failed to connect to localhost port 8080\n' },
  )
  assert.equal(message, 'server: unhealthy. Last check: curl: (7) Failed to connect to localhost port 8080')
})

test('an exited publisher is named with its exit code and healthy services are left out', () => {
  const message = describeNotReady([
    healthy,
    { Service: 'worker', State: 'running', Health: 'healthy', ExitCode: 0 },
    { Service: 'publisher', State: 'exited', Health: '', ExitCode: 1 },
  ])
  assert.equal(message, 'publisher: exited with 1')
})

test('a service that has no health check and runs is ready', () => {
  assert.equal(describeNotReady([{ Service: 'publisher', State: 'running', Health: '', ExitCode: 0 }]), '')
})

test('nothing is reported when every container is healthy', () => {
  assert.equal(describeNotReady([healthy, { ...healthy, Service: 'server' }]), '')
})

test('a service that is still starting is reported without a check output when it has none', () => {
  assert.equal(
    describeNotReady([{ Service: 'worker', State: 'running', Health: 'starting', ExitCode: 0 }]),
    'worker: starting.',
  )
})

test('parsePs reads an array and one object per line', () => {
  const containers = [{ Service: 'a' }, { Service: 'b' }]
  assert.deepEqual(parsePs(JSON.stringify(containers)), containers)
  assert.deepEqual(parsePs(containers.map((container) => JSON.stringify(container)).join('\n')), containers)
  assert.deepEqual(parsePs(''), [])
})
