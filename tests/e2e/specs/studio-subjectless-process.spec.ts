import { cp, mkdtemp, readFile, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { startServer } from '../activate'
import { pickOption } from '../select'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'
import { createResource, openFormTab } from '../studio-resources'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The journey creates files. It runs on a second server over a folder of its own, copied from the committed sample, so
// no other spec's writes reach it.
test.describe('Studio builds a process without a subject and its triggers in the Form tab', () => {
  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-subjectless-process-'))
    await cp(purchaseRequests, copy, { recursive: true })
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server and the live
    // release of the main E2E server is not activated again.
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  async function diagnostics(page: Page): Promise<unknown> {
    return (await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)).json()
  }

  async function pick(page: Page, field: string, name: string) {
    await pickOption(page.getByTestId(`studio-field-${field}`).getByRole('combobox'), name)
  }

  async function save(page: Page) {
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()
  }

  async function saved(file: string): Promise<Record<string, unknown>> {
    return JSON.parse(await readFile(path.join(copy, file), 'utf8')) as Record<string, unknown>
  }

  /** A new trigger starts as an event trigger. Picking an event and then clearing it removes the key, so the file keeps none. */
  async function dropEvent(page: Page) {
    await pick(page, '/event', 'ReminderSent')
    const field = page.getByTestId('studio-field-/event')
    await field.hover()
    await field.getByRole('img', { name: 'close-circle', exact: true }).click()
    await expect(field.getByTitle('ReminderSent', { exact: true })).toHaveCount(0)
  }

  test('an author builds the process, a scheduled job and a delete trigger without the JSON view, and the diagnostics stay', async ({
    page,
  }) => {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)
    // The sample has no event. An event is only there so that a new trigger's `event` can be picked and cleared.
    await createResource(page, 'event', 'ReminderSent')
    await openFormTab(page)
    await pick(page, '/entity', 'PurchaseRequest')
    await save(page)
    const before = await diagnostics(page)

    await createResource(page, 'process', 'DailyReminder')
    await openFormTab(page)
    await page
      .locator('label')
      .filter({ has: page.getByRole('radio', { name: 'Parameters', exact: true }) })
      .click()
    await expect(page.getByTestId('studio-field-/entity')).toHaveCount(0)
    await page.getByRole('button', { name: 'Add parameter', exact: true }).click()
    await page.getByRole('textbox', { name: 'Parameter name', exact: true }).fill('daysOverdue')
    await pick(page, '/parameters/0/type', 'integer')
    await save(page)

    await createResource(page, 'trigger', 'EveryMorning')
    await openFormTab(page)
    await pick(page, '/type', 'schedule')
    await dropEvent(page)
    await page.getByTestId('studio-field-/schedule').getByRole('textbox').fill('0 8 * * 1-5')
    await page.getByTestId('studio-field-/timeZone').getByRole('textbox').fill('Asia/Ho_Chi_Minh')
    await pick(page, '/process', 'DailyReminder')
    const schedule = page.getByTestId('studio-field-/parameters')
    await schedule.getByRole('button', { name: 'Add', exact: true }).click()
    await schedule.getByRole('spinbutton', { name: 'daysOverdue', exact: true }).fill('1')
    await save(page)

    await createResource(page, 'trigger', 'RequestDeleted')
    await openFormTab(page)
    await pick(page, '/type', 'dataChange')
    await dropEvent(page)
    await pick(page, '/entity', 'PurchaseRequest')
    await page.getByTestId('studio-field-/on').getByRole('button', { name: 'Add', exact: true }).click()
    await pick(page, '/on/0', 'delete')
    await pick(page, '/process', 'DailyReminder')
    await expect(page.getByTestId('studio-field-/fields')).toHaveCount(0)
    const deleted = page.getByTestId('studio-field-/parameters')
    await deleted.getByRole('button', { name: 'Add', exact: true }).click()
    const value = page.getByTestId('studio-field-/parameters/daysOverdue')
    await expect(value.locator('.cm-editor')).toBeVisible()
    await value.getByRole('textbox').fill('length(title)')
    await save(page)

    expect(await saved('processes/daily-reminder.json')).toMatchObject({
      name: 'DailyReminder',
      parameters: [{ name: 'daysOverdue', type: 'integer' }],
    })
    expect(await saved('processes/daily-reminder.json')).not.toHaveProperty('entity')
    expect(await saved('triggers/every-morning.json')).toMatchObject({
      type: 'schedule',
      schedule: '0 8 * * 1-5',
      timeZone: 'Asia/Ho_Chi_Minh',
      process: 'DailyReminder',
      parameters: { daysOverdue: 1 },
    })
    expect(await saved('triggers/every-morning.json')).not.toHaveProperty('event')
    expect(await saved('triggers/request-deleted.json')).toMatchObject({
      type: 'dataChange',
      entity: 'PurchaseRequest',
      on: ['delete'],
      process: 'DailyReminder',
      parameters: { daysOverdue: 'length(title)' },
    })
    expect(await saved('triggers/request-deleted.json')).not.toHaveProperty('event')
    expect(await saved('triggers/request-deleted.json')).not.toHaveProperty('fields')
    expect(await diagnostics(page)).toEqual(before)
  })
})
