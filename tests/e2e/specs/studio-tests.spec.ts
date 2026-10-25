import { spawnSync } from 'node:child_process'
import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '../test'
import { startServer } from '../activate'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'

const approvalBelowThreshold = '09d75317-fd9e-411e-8322-317778fc1b2b'

test.describe('hoa runs the test scenarios of the purchase request sample from Studio', () => {
  test.describe.configure({ mode: 'serial' })
  // A second server starts, and the requests wait behind the publish requests that other specs queue. Each scenario
  // of the sample creates and drops a database.
  test.setTimeout(300_000)

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    // The committed sample, copied to a folder of its own, so no other spec's writes reach it.
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-tests-'))
    await cp(purchaseRequests, copy, { recursive: true })
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server.
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  test('Run tests runs every scenario, and every scenario passes', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)
    await page.getByTestId('studio-run-tests').click()

    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 240_000,
    })
    await expect(page.getByTestId('studio-test-result')).toHaveCount(5)
    await expect(page.locator('[data-testid="studio-test-result"][data-outcome="passed"]')).toHaveCount(5)
  })

  test('Run runs one broken scenario, and selecting its failure opens the failing step', async ({ page }) => {
    const scenarioFile = path.join(copy, 'scenarios', 'approval-below-threshold.json')
    const scenario = JSON.parse(await readFile(scenarioFile, 'utf8')) as { steps: { values: Record<string, unknown> }[] }
    // The last step expects the approved status. Rejected breaks that expectation, at step 6.
    scenario.steps[5].values.status = 'rejected'
    await writeFile(scenarioFile, JSON.stringify(scenario, null, 2))

    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${approvalBelowThreshold}`)
    await page.getByTestId('studio-run-scenario').click()

    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'failed', {
      timeout: 240_000,
    })
    const failed = page.locator('[data-testid="studio-test-result"][data-outcome="failed"]')
    await expect(failed).toHaveCount(1)
    await expect(failed).toContainText('Step 6')

    await failed.getByRole('button').click()
    await expect(page).toHaveURL(/path=%2Fsteps%2F5/)
    await expect(page.getByTestId('studio-field-/steps/5')).toBeInViewport()
  })

  test('GET /api/me shows the test controls to an author and not to anyone else', async ({ page }) => {
    await useSession(page, 'hoa')
    const author = (await (await page.request.get('/api/me')).json()) as { studioTest: boolean }
    expect(author.studioTest).toBe(true)

    await useSession(page, 'anna')
    const employee = (await (await page.request.get('/api/me')).json()) as { studioTest: boolean }
    expect(employee.studioTest).toBe(false)
  })
})

test('the test controls do not show when the server has Studio:Tests off', async ({ page }) => {
  const copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-tests-off-'))
  await cp(purchaseRequests, copy, { recursive: true })
  const off = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Tests: 'false', Studio__Folders__0: copy })
  try {
    await useSession(page, 'hoa')
    await page.goto(`${off.url}/studio?app=PurchaseRequests`)
    // Publish shows only once GET /api/me has answered, so the test controls have been decided too.
    await expect(page.getByTestId('studio-publish')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Validate', exact: true })).toBeVisible()
    await expect(page.getByTestId('studio-run-tests')).toHaveCount(0)

    await page.getByTestId(`studio-resource-${approvalBelowThreshold}`).click()
    await expect(page.getByTestId('studio-resource-details')).toContainText('scenarios/approval-below-threshold.json')
    await expect(page.getByTestId('studio-run-scenario')).toHaveCount(0)
  } finally {
    await off.stop()
    await rm(copy, { recursive: true, force: true })
  }
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
