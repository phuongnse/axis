import { cp, readFile, rm, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test, type Page, type TestInfo } from '@playwright/test'
import { activateFolder, startServer } from '../activate'
import { appPath, createRequest, decide, expectStatus, submit, waitForTask } from '../purchase-requests'
import { purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'

// Each activation starts a second server, which takes a few seconds.
test.setTimeout(180_000)

/**
 * Release B is the purchase request sample with a lower finance threshold and a reworded section title of
 * the review form. The threshold falls from 10000 to 1000, and the title "Decision" becomes "Your decision".
 * The journeys use totals of 1, 100, 500, 10000 and 9000000 elsewhere, and all of them fall on the same side
 * of both thresholds. No other spec uses the section title.
 */
async function releaseB(testInfo: TestInfo) {
  const folder = testInfo.outputPath('purchase-requests-b')
  await rm(folder, { recursive: true, force: true })
  await cp(purchaseRequests, folder, { recursive: true })

  const rule = path.join(folder, 'rules', 'finance-threshold.json')
  const ruleJson = JSON.parse(await readFile(rule, 'utf8'))
  ruleJson.expression = '1000'
  await writeFile(rule, JSON.stringify(ruleJson, null, 2) + '\n')

  const texts = path.join(folder, 'texts', 'en.json')
  const textsJson = JSON.parse(await readFile(texts, 'utf8'))
  textsJson.texts['purchaseRequest.sections.decision'] = 'Your decision'
  await writeFile(texts, JSON.stringify(textsJson, null, 2) + '\n')
  return folder
}

/**
 * Starts a second server whose Studio edits a fresh copy of the purchase request sample, and returns it with the
 * copy's folder. The server activates the committed sample when it starts, which also resets the live release.
 * Publishing from it changes the live sample for the whole run, so the journeys that use it sit in the serial
 * group below.
 */
async function studioServer(testInfo: TestInfo) {
  const folder = testInfo.outputPath('studio-publish')
  await rm(folder, { recursive: true, force: true })
  await cp(purchaseRequests, folder, { recursive: true })
  const server = await startServer({ Studio__Folders__0: folder })
  return { server, folder }
}

/**
 * Edits the JSON of the resource in `file` through Studio's JSON view and saves it. `change` edits the content
 * as it is in the copy `folder`.
 */
async function editInStudio(
  page: Page,
  serverUrl: string,
  folder: string,
  file: string,
  change: (content: Record<string, any>) => void,
) {
  const list = await page.request.get(`${serverUrl}/api/studio/apps/PurchaseRequests/resources`)
  const { items } = (await list.json()) as { items: { id: string; file: string }[] }
  const resource = items.find((item) => item.file === file)!
  const content = JSON.parse(await readFile(path.join(folder, file), 'utf8'))
  change(content)

  await page.goto(`${serverUrl}/studio?app=PurchaseRequests&resource=${resource.id}`)
  await page.getByRole('tab', { name: 'JSON' }).click()
  await page.getByRole('textbox', { name: 'JSON' }).fill(JSON.stringify(content, null, 2))
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByTestId('studio-saved')).toBeVisible()
}

/** Clicks Publish, fills the dialog's label and confirms. */
async function publishWithLabel(page: Page, label: string) {
  await page.getByRole('button', { name: 'Publish' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('textbox', { name: 'Label (optional)' }).fill(label)
  await dialog.getByRole('button', { name: 'Publish' }).click()
}

/**
 * Opens the manager's task for the request titled `title` from My tasks, expects the section title `shown`
 * and not `hidden` in light and dark mode, and approves the task. The theme stays in local storage, so it
 * is dark afterwards.
 */
async function reviewAndApprove(page: Page, title: string, shown: string, hidden: string) {
  const row = await waitForTask(page, title)
  await row.getByRole('link', { name: 'Manager approval' }).click()
  await expect(page.getByLabel('Title', { exact: true })).toHaveValue(title)
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') await page.getByRole('switch', { name: 'Dark mode' }).click()
    const theme = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(theme.getByRole('heading', { name: shown, exact: true })).toBeVisible()
    await expect(theme.getByRole('heading', { name: hidden, exact: true })).toHaveCount(0)
  }
  await page.getByRole('button', { name: 'Approve' }).click()
  await expect(page).toHaveURL(/\/purchasing\/mytasks$/)
}

// Both tests change the active release of the sample, so they must not run side by side.
test.describe('the active release of the purchase request sample changes', () => {
  test.describe.configure({ mode: 'serial' })

  // The rest of the run expects the sample as it is committed.
  test.afterAll(async () => {
    test.setTimeout(180_000)
    await activateFolder(purchaseRequests)
  })

  test('a waiting request finishes on the release it started on', async ({ page }, testInfo) => {
    // A retry starts on the committed sample, not on release B of the failed attempt.
    await activateFolder(purchaseRequests)
    await useSession(page, 'anna')
    const { title, id } = await createRequest(page, 'Pinned release', 5000)
    await submit(page, id, title)
    await useSession(page, 'chau')
    await waitForTask(page, title)

    await activateFolder(await releaseB(testInfo))

    await reviewAndApprove(page, title, 'Decision', 'Your decision')
    // The old threshold of 10000 still applies, so the manager's approval ends the process.
    await expectStatus(page, id, 'approved')
    await useSession(page, 'dung')
    const tasks = await page.request.get(`${appPath}/tasks?pageSize=100`)
    expect(tasks.status()).toBe(200)
    const { items } = (await tasks.json()) as { items: { subject: { id: string } }[] }
    expect(items.filter((item) => item.subject.id === id)).toEqual([])
  })

  test('a request submitted after the new release runs on it', async ({ page }, testInfo) => {
    await activateFolder(await releaseB(testInfo))
    await useSession(page, 'anna')
    const { title, id } = await createRequest(page, 'New release', 5000)
    await submit(page, id, title)

    await useSession(page, 'chau')
    await reviewAndApprove(page, title, 'Your decision', 'Decision')

    // The new threshold of 1000 applies, so the request also needs the finance reviewer.
    await useSession(page, 'dung')
    await decide(page, title, 'Finance approval', 'Approve')
    await expectStatus(page, id, 'approved')
  })

  test('an author publishes from Studio and the site shows the new release', async ({ page }, testInfo) => {
    test.setTimeout(240_000)
    const { server, folder } = await studioServer(testInfo)
    const heading = `Name ${Date.now().toString(36)}`
    try {
      await useSession(page, 'hoa')
      await editInStudio(page, server.url, folder, 'texts/en.json', (content) => {
        expect(content.texts['supplier.name']).toBe('Name')
        content.texts['supplier.name'] = heading
      })

      await publishWithLabel(page, 'Supplier heading')

      await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
        timeout: 60_000,
      })
      await expect(page.getByTestId('studio-release-number')).toBeVisible()
      await useSession(page, 'anna')
      await page.goto('/purchasing/suppliers')
      await expect(page.getByRole('columnheader', { name: heading })).toBeVisible()
    } finally {
      await server.stop()
    }
  })

  test('a publish with a missing binding fails with its diagnostic and the site stays as it was', async (
    { page },
    testInfo,
  ) => {
    test.setTimeout(240_000)
    const { server, folder } = await studioServer(testInfo)
    const heading = `Name ${Date.now().toString(36)}`
    try {
      await useSession(page, 'hoa')
      await editInStudio(page, server.url, folder, 'texts/en.json', (content) => {
        content.texts['supplier.name'] = heading
      })
      await editInStudio(page, server.url, folder, 'connectors/purchasing-system.json', (content) => {
        content.baseUrl.binding = 'NoSuchBinding'
      })

      await publishWithLabel(page, 'Missing binding')

      await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'failed', {
        timeout: 60_000,
      })
      await expect(page.getByTestId('studio-release-number')).toHaveCount(0)
      await expect(page.getByTestId('studio-diagnostics')).toContainText('AXC0100')
      await useSession(page, 'anna')
      await page.goto('/purchasing/suppliers')
      await expect(page.getByRole('columnheader', { name: 'Name', exact: true })).toBeVisible()
      await expect(page.getByRole('columnheader', { name: heading })).toHaveCount(0)
    } finally {
      await server.stop()
    }
  })
})
