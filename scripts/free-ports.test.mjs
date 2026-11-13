import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { freePort, isPortClash, startOnFreePorts } from './lib/free-ports.mjs'

const clash = {
  status: 1,
  stdout: '',
  stderr: 'Error response from daemon: failed to bind host port for 127.0.0.1:1234:172.18.0.2:8080/tcp: address already in use',
}

// Runs startOnFreePorts with a fake start that returns the given results in turn, and counts the calls.
const run = async (results, options = {}) => {
  const calls = { pick: 0, start: 0, cleanUp: 0 }
  const result = await startOnFreePorts({
    pick: () => calls.pick++,
    start: () => results[calls.start++],
    cleanUp: () => calls.cleanUp++,
    ...options,
  })
  return { result, calls }
}

describe('startOnFreePorts', () => {
  test('a start that fails with a bind error picks new ports and starts again', async () => {
    const { result, calls } = await run([clash, { status: 0, stdout: 'ok', stderr: '' }])
    assert.equal(result.status, 0)
    assert.deepEqual(calls, { pick: 2, start: 2, cleanUp: 1 })
  })

  test('a start that fails with another error is not tried again', async () => {
    const failed = { status: 1, stdout: '', stderr: 'service "server" is unhealthy' }
    const { result, calls } = await run([failed, { status: 0 }])
    assert.equal(result, failed)
    assert.deepEqual(calls, { pick: 1, start: 1, cleanUp: 0 })
  })

  test('three bind errors return the last failed result', async () => {
    const last = { ...clash, stdout: 'last' }
    const { result, calls } = await run([clash, clash, last, { status: 0 }])
    assert.equal(result, last)
    assert.deepEqual(calls, { pick: 3, start: 3, cleanUp: 2 })
  })

  test('a start that succeeds the first time picks once', async () => {
    const { result, calls } = await run([{ status: 0 }])
    assert.equal(result.status, 0)
    assert.deepEqual(calls, { pick: 1, start: 1, cleanUp: 0 })
  })
})

describe('isPortClash', () => {
  test('knows the bind errors of Docker', () => {
    assert.ok(isPortClash(clash.stderr))
    assert.ok(isPortClash('Bind for 0.0.0.0:80 failed: port is already allocated'))
    assert.ok(isPortClash('Ports are not available: exposing port TCP 127.0.0.1:80'))
    assert.ok(!isPortClash('service "server" is unhealthy'))
  })
})

describe('freePort', () => {
  test('never returns the same port twice', async () => {
    const ports = []
    for (let i = 0; i < 200; i++) ports.push(await freePort())
    assert.equal(new Set(ports).size, ports.length)
  })
})
