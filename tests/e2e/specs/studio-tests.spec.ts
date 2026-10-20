import { spawnSync } from 'node:child_process'
import { cp, mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '@playwright/test'
import { startServer } from '../activate'
import { csrfHeaders } from '../csrf'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'

test('hoa runs the test scenarios of the purchase request sample, and every scenario passes', async ({ page }) => {
  // A second server starts, and the request waits behind the publish requests that other specs queue. Each scenario
  // of the sample creates and drops a database.
  test.setTimeout(300_000)

  // The committed sample, copied to a folder of its own. The other Studio specs edit the shared copy while they run,
  // and a snapshot taken in the middle of an edit could fail for a reason that has nothing to do with this test.
  const copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-tests-'))
  await cp(purchaseRequests, copy, { recursive: true })
  // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server.
  const server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  try {
    await useSession(page, 'hoa')
    const response = await page.request.post(`${server.url}/api/studio/apps/PurchaseRequests/test`, {
      headers: csrfHeaders,
    })
    expect(response.status()).toBe(202)
    const location = response.headers()['location']
    expect(location).toBeTruthy()

    type TestRequest = { status: string; scenarios: { name: string; outcome: string }[] | null }
    const read = async () => (await (await page.request.get(new URL(location, server.url).href)).json()) as TestRequest
    await expect
      .poll(async () => (await read()).status, { timeout: 240_000, intervals: [1_000, 2_000, 5_000] })
      .toBe('succeeded')

    const { scenarios } = await read()
    expect(scenarios?.length).toBeGreaterThan(0)
    expect(scenarios?.filter((scenario) => scenario.outcome !== 'passed')).toEqual([])
  } finally {
    await server.stop()
    await rm(copy, { recursive: true, force: true })
  }
})

test('GET /api/me shows the test controls to an author and not to anyone else', async ({ page }) => {
  await useSession(page, 'hoa')
  const author = (await (await page.request.get('/api/me')).json()) as { studioTest: boolean }
  expect(author.studioTest).toBe(true)

  await useSession(page, 'anna')
  const employee = (await (await page.request.get('/api/me')).json()) as { studioTest: boolean }
  expect(employee.studioTest).toBe(false)
})

test('only axis_scenario can create databases, and it is not a superuser', () => {
  const container = process.env.AXIS_E2E_POSTGRES_CONTAINER
  if (!container) {
    throw new Error('AXIS_E2E_POSTGRES_CONTAINER is not set. Run the E2E tests with scripts/e2e.sh.')
  }

  const roles = ['axis_schema', 'axis_runtime', 'axis_preview_schema', 'axis_preview_runtime', 'axis_scenario']
  const query = `SELECT rolname || ':' || rolcreatedb || ':' || rolsuper FROM pg_roles WHERE rolname IN (${roles
    .map((role) => `'${role}'`)
    .join(',')}) ORDER BY rolname`
  const result = spawnSync('docker', ['exec', container, 'psql', '-U', 'axis', '-d', 'axis', '-tAc', query], {
    encoding: 'utf8',
  })
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
  expect(result.stdout.trim().split('\n')).toEqual([
    'axis_preview_runtime:false:false',
    'axis_preview_schema:false:false',
    'axis_runtime:false:false',
    'axis_scenario:true:false',
    'axis_schema:false:false',
  ])
})
