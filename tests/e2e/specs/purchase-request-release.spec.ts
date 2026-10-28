import { randomUUID } from 'node:crypto'
import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test } from '../test'
import type { Browser, Page, TestInfo } from '@playwright/test'
import { activateFolder, startServer } from '../activate'
import { csrfHeaders } from '../csrf'
import { confirmDialog, waitForDialog } from '../dialogs'
import { expectShown, fieldLabel, fieldValue } from '../fields'
import { guardStep } from '../hang-guard'
import { appPath, createRequest, decide, expectStatus, submit, waitForTask } from '../purchase-requests'
import { previewURL, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'
import { openTab } from '../studio-resources'
import { chooseTheme } from '../shell'

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
 * as it is in the copy `folder`. A click here once waited for the browser until the test timeout ran out, so the
 * edit gets a limit of its own, and a hang leaves a screenshot, the console and the machine state in the report.
 * Only the journey that tests the edit uses it. The others set up their change with `writeInStudio`.
 */
async function editInStudio(
  page: Page,
  serverUrl: string,
  folder: string,
  file: string,
  change: (content: Record<string, any>) => void,
) {
  await guardStep(page, test.info(), `The Studio edit of ${file}`, 60_000, async () => {
    const list = await page.request.get(`${serverUrl}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const resource = items.find((item) => item.file === file)!
    const content = JSON.parse(await readFile(path.join(folder, file), 'utf8'))
    change(content)

    await page.goto(`${serverUrl}/studio?app=PurchaseRequests&resource=${resource.id}`)
    await openTab(page, 'JSON')
    await page.getByRole('textbox', { name: 'JSON', exact: true }).fill(JSON.stringify(content, null, 2))
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()
  })
}

/**
 * Replaces the member at the JSON pointer `pointer` of the resource in `file` through Studio's resource API, as
 * the signed-in author, without the browser. A refused write fails at once with its status.
 */
async function writeInStudio(page: Page, serverUrl: string, file: string, pointer: string, value: unknown) {
  const resources = `${serverUrl}/api/studio/apps/PurchaseRequests/resources`
  const list = await page.request.get(resources)
  expect(list.status()).toBe(200)
  const { items } = (await list.json()) as { items: { id: string; file: string; version: string }[] }
  const resource = items.find((item) => item.file === file)!
  const response = await page.request.patch(`${resources}/${resource.id}`, {
    headers: {
      ...csrfHeaders,
      'Content-Type': 'application/json-patch+json',
      'If-Match': `"${resource.version}"`,
    },
    data: JSON.stringify([{ op: 'replace', path: pointer, value }]),
  })
  expect(response.status(), await response.text()).toBe(200)
}

/** Opens Studio for the purchase request sample and waits for Publish. A hang here leaves evidence. */
async function openStudio(page: Page, serverUrl: string) {
  await guardStep(page, test.info(), 'Opening Studio', 60_000, async () => {
    await page.goto(`${serverUrl}/studio?app=PurchaseRequests`)
    await expect(page.getByRole('button', { name: 'Publish', exact: true })).toBeVisible()
  })
}

/**
 * Opens a new browser context on the preview host of the main E2E server, signs `user` in through Keycloak and
 * returns the context and its page. The caller closes the context.
 */
async function signInToPreview(browser: Browser, baseURL: string, user: string) {
  const context = await browser.newContext({
    baseURL: previewURL(baseURL),
    storageState: { cookies: [], origins: [] },
  })
  const page = await context.newPage()
  await page.goto('/api/auth/sign-in?returnUrl=/')
  await page.locator('#username').fill(user)
  await page.locator('#password').fill('axis')
  await page.locator('#kc-login').click()
  await page.waitForURL(`${previewURL(baseURL)}/`)
  return { context, page }
}

/** Clicks Publish, fills the dialog's label and confirms. */
async function publishWithLabel(page: Page, label: string) {
  await page.getByRole('button', { name: 'Publish', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByRole('textbox', { name: 'Label (optional)', exact: true }).fill(label)
  await confirmDialog(page, 'Publish')
}

/**
 * Opens the manager's task for the request titled `title` from My tasks, expects the section title `shown`
 * and not `hidden` in light and dark mode, and approves the task. The theme stays in local storage, so it
 * is dark afterwards.
 */
async function reviewAndApprove(page: Page, title: string, shown: string, hidden: string) {
  const row = await waitForTask(page, title)
  await row.getByRole('link', { name: 'Manager approval', exact: true }).click()
  await expectShown(fieldValue(page, 'title'), title)
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') await chooseTheme(page, 'Dark')
    const theme = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(theme.getByRole('heading', { name: shown, exact: true })).toBeVisible()
    await expect(theme.getByRole('heading', { name: new RegExp(hidden) })).toHaveCount(0)
  }
  await page.getByRole('button', { name: 'Approve', exact: true }).click()
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
    // Starts on the committed sample, whatever an earlier test left active.
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
      const texts = JSON.parse(await readFile(path.join(folder, 'texts/en.json'), 'utf8'))
      expect(texts.texts['supplier.name']).toBe('Name')
      await writeInStudio(page, server.url, 'texts/en.json', '/texts/supplier.name', heading)
      await openStudio(page, server.url)

      await publishWithLabel(page, 'Supplier heading')

      await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
        timeout: 60_000,
      })
      await expect(page.getByTestId('studio-release-number')).toBeVisible()
      await useSession(page, 'anna')
      await page.goto('/purchasing/suppliers')
      await expect(page.getByRole('columnheader', { name: heading, exact: true })).toBeVisible()
    } finally {
      await server.stop()
    }
  })

  test('an author previews from Studio and the preview host shows the new label while the main host does not', async (
    { baseURL, browser, page },
    testInfo,
  ) => {
    test.setTimeout(240_000)
    const { server, folder } = await studioServer(testInfo)
    const heading = `Name ${Date.now().toString(36)}`
    try {
      await useSession(page, 'hoa')
      const texts = JSON.parse(await readFile(path.join(folder, 'texts/en.json'), 'utf8'))
      expect(texts.texts['supplier.name']).toBe('Name')
      await writeInStudio(page, server.url, 'texts/en.json', '/texts/supplier.name', heading)
      await openStudio(page, server.url)

      await page.getByTestId('studio-preview').click()

      await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
        timeout: 60_000,
      })
      await expect(page.getByTestId('studio-release-number')).toHaveCount(0)
      await expect(page.getByTestId('studio-preview-link')).toHaveAttribute(
        'href',
        `${previewURL(server.url)}/purchasing`,
      )

      // The preview tenant shares its database with the main E2E server, which is the one with a Keycloak redirect
      // address, so anna signs in to the preview host of that server, as in the preview tenant journey.
      const { context, page: preview } = await signInToPreview(browser, baseURL!, 'anna')
      try {
        await preview.goto('/purchasing/suppliers')
        await expect(preview.getByRole('columnheader', { name: heading, exact: true })).toBeVisible()
      } finally {
        await context.close()
      }

      await useSession(page, 'anna')
      await page.goto('/purchasing/suppliers')
      await expect(page.getByRole('columnheader', { name: 'Name', exact: true })).toBeVisible()
      await expect(page.getByRole('columnheader', { name: new RegExp(heading) })).toHaveCount(0)
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
      const connector = JSON.parse(await readFile(path.join(folder, 'connectors/purchasing-system.json'), 'utf8'))
      expect(connector.baseUrl.binding).toBeDefined()
      await writeInStudio(page, server.url, 'texts/en.json', '/texts/supplier.name', heading)
      await writeInStudio(page, server.url, 'connectors/purchasing-system.json', '/baseUrl/binding', 'NoSuchBinding')
      await openStudio(page, server.url)

      await publishWithLabel(page, 'Missing binding')

      await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'failed', {
        timeout: 60_000,
      })
      await expect(page.getByTestId('studio-release-number')).toHaveCount(0)
      await expect(page.getByTestId('studio-diagnostics')).toContainText('AXC0100')
      await useSession(page, 'anna')
      await page.goto('/purchasing/suppliers')
      await expect(page.getByRole('columnheader', { name: 'Name', exact: true })).toBeVisible()
      await expect(page.getByRole('columnheader', { name: new RegExp(heading) })).toHaveCount(0)
    } finally {
      await server.stop()
    }
  })

  test('an author rolls back to an earlier release and Studio warns that the folder differs', async ({
    page,
  }, testInfo) => {
    test.setTimeout(300_000)
    const { server } = await studioServer(testInfo)
    const stamp = Date.now().toString(36)
    const headingA = `Name A ${stamp}`
    const headingB = `Name B ${stamp}`
    const status = page.getByTestId('studio-request-status')
    try {
      await useSession(page, 'hoa')
      await writeInStudio(page, server.url, 'texts/en.json', '/texts/supplier.name', headingA)
      await openStudio(page, server.url)
      await publishWithLabel(page, `First ${stamp}`)
      await expect(status).toHaveAttribute('data-status', 'succeeded', {
        timeout: 60_000,
      })
      // The folder is the active release now, so there is nothing to warn about.
      await expect(page.getByTestId('studio-release-row').filter({ hasText: `First ${stamp}` })).toHaveAttribute(
        'data-active',
        'true',
      )
      await expect(page.getByTestId('studio-folder-differs')).toHaveCount(0)

      await writeInStudio(page, server.url, 'texts/en.json', '/texts/supplier.name', headingB)
      await openStudio(page, server.url)
      await publishWithLabel(page, `Second ${stamp}`)
      await expect(page.getByTestId('studio-release-row').filter({ hasText: `Second ${stamp}` })).toHaveAttribute(
        'data-active',
        'true',
        { timeout: 60_000 },
      )

      const first = page.getByTestId('studio-release-row').filter({ hasText: `First ${stamp}` })
      const number = await first.getAttribute('data-number')
      await first.getByRole('button', { name: 'Roll back', exact: true }).click()
      const dialog = await waitForDialog(page)
      await expect(dialog).toContainText(`Release ${number} First ${stamp}`)
      await confirmDialog(page, 'Roll back')

      await expect(status).toContainText('Rolled back', { timeout: 60_000 })
      await expect(status).toHaveAttribute('data-status', 'succeeded')
      // The rollback adds a row, without a label, and only that newest row of the release is marked.
      await expect(
        page.locator(`[data-testid="studio-release-row"][data-number="${number}"][data-active="true"]`),
      ).toHaveCount(1)
      await expect(page.getByTestId('studio-release-row').and(page.locator('[data-active="true"]'))).toHaveCount(1)
      // The folder still has heading B, so it differs from the release that is active again.
      await expect(page.getByTestId('studio-folder-differs')).toBeVisible()

      await useSession(page, 'anna')
      await page.goto('/purchasing/suppliers')
      await expect(page.getByRole('columnheader', { name: headingA, exact: true })).toBeVisible()
      await expect(page.getByRole('columnheader', { name: new RegExp(headingB) })).toHaveCount(0)
    } finally {
      await server.stop()
    }
  })

  test('a rollback across a rename migration is refused and the later release stays active', async ({
    page,
  }, testInfo) => {
    test.setTimeout(300_000)
    // A throwaway application, because a rename migration in the purchase request sample would stop the
    // committed sample from activating again.
    const name = `Rollback${Date.now().toString(36)}`
    const folder = testInfo.outputPath('studio-rollback')
    const orderId = randomUUID()
    const order = (field: string) =>
      JSON.stringify({
        id: orderId,
        kind: 'entity',
        name: 'Order',
        formatVersion: 1,
        fields: [
          { name: 'number', type: 'text', required: true, maxLength: 20 },
          { name: field, type: 'text' },
        ],
      })
    await rm(folder, { recursive: true, force: true })
    await mkdir(path.join(folder, 'entities'), { recursive: true })
    await mkdir(path.join(folder, 'migrations'), { recursive: true })
    await writeFile(
      path.join(folder, 'application.json'),
      JSON.stringify({
        id: randomUUID(),
        kind: 'application',
        name,
        formatVersion: 1,
      }),
    )
    await writeFile(path.join(folder, 'entities', 'order.json'), order('note'))
    const server = await startServer({ Studio__Folders__0: folder })
    const rows = page.getByTestId('studio-release-row')
    try {
      await useSession(page, 'hoa')
      await page.goto(`${server.url}/studio?app=${name}`)
      await publishWithLabel(page, 'Before the rename')
      await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
        timeout: 60_000,
      })
      await expect(rows).toHaveCount(1)

      await writeFile(path.join(folder, 'entities', 'order.json'), order('remark'))
      await writeFile(
        path.join(folder, 'migrations', '0001-rename-note.json'),
        JSON.stringify({
          id: randomUUID(),
          kind: 'migration',
          name: 'RenameNote',
          formatVersion: 1,
          entity: 'Order',
          changes: [{ type: 'renameField', from: 'note', to: 'remark' }],
        }),
      )
      await publishWithLabel(page, 'After the rename')
      await expect(rows).toHaveCount(2, { timeout: 60_000 })
      await expect(rows.and(page.locator('[data-number="2"]'))).toHaveAttribute('data-active', 'true')

      const earlier = page.locator('[data-testid="studio-release-row"][data-number="1"]')
      await earlier.getByRole('button', { name: 'Roll back', exact: true }).click()
      await confirmDialog(page, 'Roll back')

      await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'failed', {
        timeout: 60_000,
      })
      await expect(page.getByTestId('studio-diagnostics')).toContainText('AXC0174')
      await expect(page.locator('[data-testid="studio-release-row"][data-number="2"]')).toHaveAttribute(
        'data-active',
        'true',
      )
      await expect(page.locator('[data-testid="studio-release-row"][data-number="1"]')).not.toHaveAttribute(
        'data-active',
        'true',
      )
    } finally {
      await server.stop()
    }
  })

  test('an author takes a change from edit to preview, publish and rollback without the admin CLI, and a waiting request finishes on its release', async (
    { baseURL, browser, page },
    testInfo,
  ) => {
    test.setTimeout(420_000)
    const { server, folder } = await studioServer(testInfo)
    const title = `Suppliers ${Date.now().toString(36)}`
    const label = `Journey ${title}`
    const status = page.getByTestId('studio-request-status')
    const suppliers = '/purchasing/suppliers'
    try {
      // A request that waits for its manager before the publish. The total stays under the finance threshold,
      // so the manager's approval ends the process.
      await useSession(page, 'anna')
      const request = await createRequest(page, 'Journey', 500)
      await submit(page, request.id, request.title)
      await useSession(page, 'chau')
      await waitForTask(page, request.title)

      // The release that is active now, which the request is pinned to, and the newest number so far.
      await useSession(page, 'hoa')
      const history = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/releases`)
      expect(history.status()).toBe(200)
      const body = (await history.json()) as {
        active: { releaseId: string; number: number }
        items: { number: number }[]
      }
      const before = body.active
      const maxNumber = Math.max(...body.items.map((item) => item.number))

      await editInStudio(page, server.url, folder, 'texts/en.json', (content) => {
        expect(content.texts['pages.suppliers.title']).toBe('Suppliers')
        content.texts['pages.suppliers.title'] = title
      })

      await page.getByRole('button', { name: 'Validate', exact: true }).click()
      await expect(status).toHaveAttribute('data-status', 'succeeded', { timeout: 60_000 })
      const diagnostics = page.getByTestId('studio-diagnostics')
      await expect(diagnostics.locator('li')).toHaveCount(0)
      await expect(diagnostics).toContainText('No diagnostics.')

      await page.getByTestId('studio-preview').click()
      await expect(status).toHaveAttribute('data-status', 'succeeded', { timeout: 60_000 })
      const preview = await signInToPreview(browser, baseURL!, 'anna')
      try {
        await preview.page.goto(suppliers)
        await expect(preview.page.getByText(title, { exact: true }).first()).toBeVisible()
      } finally {
        await preview.context.close()
      }
      await useSession(page, 'anna')
      await page.goto(suppliers)
      await expect(page.getByText('Suppliers', { exact: true }).first()).toBeVisible()
      await expect(page.getByText(new RegExp(title))).toHaveCount(0)

      await useSession(page, 'hoa')
      await page.goto(`${server.url}/studio?app=PurchaseRequests`)
      await publishWithLabel(page, label)
      await expect(status).toHaveAttribute('data-status', 'succeeded', { timeout: 60_000 })
      const published = page.getByTestId('studio-release-row').filter({ hasText: label })
      await expect(published).toHaveAttribute('data-number', String(maxNumber + 1))
      await expect(published).toHaveAttribute('data-active', 'true')
      await useSession(page, 'anna')
      await page.goto(suppliers)
      await expect(page.getByText(title, { exact: true }).first()).toBeVisible()

      // The request approved after the publish finishes on the release it started on.
      await useSession(page, 'chau')
      await decide(page, request.title, 'Manager approval', 'Approve')
      // The manager sees the status read-only, which the helper can read as a value.
      await expectStatus(page, request.id, 'approved')
      await useSession(page, 'giang')
      await expect(async () => {
        const response = await page.request.get(`${appPath}/processes/PurchaseRequestApproval/instances?pageSize=100`)
        expect(response.status()).toBe(200)
        const { items } = (await response.json()) as {
          items: { subjectId: string; state: string; releaseId: string }[]
        }
        const instance = items.find((item) => item.subjectId === request.id)
        expect(instance).toMatchObject({ state: 'completed', releaseId: before.releaseId })
      }).toPass({ timeout: 30_000 })

      await useSession(page, 'hoa')
      await page.goto(`${server.url}/studio?app=PurchaseRequests`)
      await page
        .locator(`[data-testid="studio-release-row"][data-number="${before.number}"]`)
        .first()
        .getByRole('button', { name: 'Roll back', exact: true })
        .click()
      await confirmDialog(page, 'Roll back')
      await expect(status).toContainText('Rolled back', { timeout: 60_000 })
      await expect(status).toHaveAttribute('data-status', 'succeeded')
      await useSession(page, 'anna')
      await page.goto(suppliers)
      await expect(page.getByText('Suppliers', { exact: true }).first()).toBeVisible()
      await expect(page.getByText(new RegExp(title))).toHaveCount(0)
    } finally {
      await server.stop()
    }
  })
})
