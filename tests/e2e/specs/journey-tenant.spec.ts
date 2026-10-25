import { spawnSync } from 'node:child_process'
import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test } from '../test'
import { csrfHeaders } from '../csrf'
import { confirmDialog, waitForDialog } from '../dialogs'
import { journeyApplications, journeyURL } from '../server-settings'
import { useJourneySession } from '../sessions'

// The journey server is the second server of the run, and a publish request waits behind the ones that other
// specs queue.
test.setTimeout(180_000)

/** The count that `query` returns from the database `database` in the E2E PostgreSQL container. */
function count(database: string, query: string): number {
  const container = process.env.AXIS_E2E_POSTGRES_CONTAINER
  if (!container) {
    throw new Error('AXIS_E2E_POSTGRES_CONTAINER is not set. Run the E2E tests with scripts/e2e.sh.')
  }
  const result = spawnSync('docker', ['exec', container, 'psql', '-U', 'axis', '-d', database, '-tAc', query], {
    encoding: 'utf8',
  })
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
  return Number(result.stdout.trim())
}

/** The id in the manifest of the application folder under the journey applications folder that is called `name`. */
async function applicationId(name: string): Promise<string> {
  for (const folder of await readdir(journeyApplications)) {
    try {
      const manifest = JSON.parse(await readFile(path.join(journeyApplications, folder, 'application.json'), 'utf8'))
      if (manifest.name === name) return manifest.id as string
    } catch {
      // A folder without a manifest is not an application.
    }
  }
  throw new Error(`No application called ${name} under ${journeyApplications}.`)
}

test.describe('the journey tenant publishes a Studio application', () => {
  test.describe.configure({ mode: 'serial' })

  // Letters and digits, starting with a letter.
  const name = `Journey${Date.now().toString(36)}`

  test('an author creates a blank application on the journey host and publishes release 1', async ({ page }) => {
    await useJourneySession(page, 'hoa')
    await page.goto(`${journeyURL}/studio`)

    await page.getByRole('button', { name: 'New application', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`app=${name}$`))

    await page.getByRole('button', { name: 'Publish', exact: true }).click()
    await confirmDialog(page, 'Publish')
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 60_000,
    })
    const rows = page.getByTestId('studio-release-row')
    await expect(rows).toHaveCount(1)
    await expect(rows).toHaveAttribute('data-number', '1')
  })

  test('the publish activates a release on the journey tenant and none on the main tenant', async () => {
    const id = await applicationId(name)
    const query = `SELECT count(*) FROM axis.release_activations WHERE application_id = '${id}'`
    expect(count('axis', query)).toBe(0)
    expect(count('axis_journey', query)).toBe(1)
  })
})

test('a requester has a journey session and cannot create an application there', async ({ page }) => {
  // useJourneySession checks that GET /api/me on the journey host answers as the requester.
  await useJourneySession(page, 'anna')
  const response = await page.request.post(`${journeyURL}/api/studio/apps`, {
    headers: csrfHeaders,
    data: { name: 'Denied' },
  })
  expect(response.status()).toBe(403)
})
