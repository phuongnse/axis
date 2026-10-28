import { randomUUID } from 'node:crypto'
import { cp, readFile, rm, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test } from '../test'
import type { APIRequest, APIRequestContext, TestInfo } from '@playwright/test'
import { activateFolder } from '../activate'
import { csrfHeaders } from '../csrf'
import { expectShown, fieldValue } from '../fields'
import { sessionFile, useSession } from '../sessions'

// The record API needs a session with a grant, so these journeys start signed in as the employee requester.
test.use({ storageState: sessionFile('requester') })

// Each activation starts a second server, which takes a few seconds.
test.setTimeout(180_000)

const releaseA = path.resolve(import.meta.dirname, '..', 'fixtures', 'release-app')
const tasksPath = '/api/apps/ReleaseApp/tasks'

/**
 * Release B is release A with one reworded text: the label of the ticket's topic field. It keeps the text
 * key, so the site texts of B differ from A only in that value.
 */
async function releaseB(testInfo: TestInfo) {
  const folder = testInfo.outputPath('release-b')
  await rm(folder, { recursive: true, force: true })
  await cp(releaseA, folder, { recursive: true })
  const file = path.join(folder, 'texts', 'en.json')
  const json = JSON.parse(await readFile(file, 'utf8'))
  json.texts['ticket.topic'] = 'Headline'
  await writeFile(file, JSON.stringify(json, null, 2) + '\n')
  return folder
}

/**
 * Creates a ticket, starts its review and returns the topic and the task id. The task is assigned to manager.
 * Specs run in parallel against one database, so each test looks only for the task it starts.
 */
async function startReview(request: APIRequestContext, apiRequest: APIRequest, baseURL: string | undefined) {
  const topic = `Pinned ${randomUUID()}`
  const created = await request.post('/api/apps/ReleaseApp/entities/Ticket/records', {
    data: { values: { topic } },
    headers: csrfHeaders,
  })
  expect(created.status()).toBe(201)
  const ticket = await created.json()
  const started = await request.post('/api/apps/ReleaseApp/processes/TicketReview/instances', {
    data: { subjectId: ticket.id },
    headers: { ...csrfHeaders, 'Idempotency-Key': randomUUID() },
  })
  expect(started.status()).toBe(201)

  // This context holds manager's saved session and shares no cookies with the browser or the request fixture.
  const manager = await apiRequest.newContext({ baseURL, storageState: sessionFile('manager') })
  let taskId = ''
  try {
    // The worker creates the task some time after the start.
    await expect(async () => {
      const response = await manager.get(`${tasksPath}?pageSize=100`)
      expect(response.ok()).toBe(true)
      const page = await response.json()
      const task = page.items.find((item: { subject: { id: string } }) => item.subject.id === ticket.id)
      expect(task).toBeDefined()
      taskId = task.id
    }).toPass({ timeout: 30_000 })
  } finally {
    await manager.dispose()
  }
  return { topic, taskId }
}

// Both tests change the active release of one application, so they must not run side by side.
test.describe('the active release of the release application changes', () => {
  test.describe.configure({ mode: 'serial' })

  test('a task keeps the label of the release it started on', async ({
    page,
    request,
    playwright,
    baseURL,
  }, testInfo) => {
    await activateFolder(releaseA)
    const { topic, taskId } = await startReview(request, playwright.request, baseURL)
    await activateFolder(await releaseB(testInfo))
    await useSession(page, 'manager')

    await page.goto(`/release-app/tasks/${taskId}`)

    await expectShown(fieldValue(page, 'topic'), topic)
    await expect(page.getByLabel(/Headline/)).toHaveCount(0)
  })

  test('a task started after the new release shows its label', async ({
    page,
    request,
    playwright,
    baseURL,
  }, testInfo) => {
    await activateFolder(await releaseB(testInfo))
    const { topic, taskId } = await startReview(request, playwright.request, baseURL)
    await useSession(page, 'manager')

    await page.goto(`/release-app/tasks/${taskId}`)

    await expectShown(fieldValue(page, 'topic'), topic)
    await expect(page.getByLabel(/Topic/)).toHaveCount(0)
  })
})

test('activating a folder that does not compile fails with the server output', async ({}, testInfo) => {
  const broken = testInfo.outputPath('broken')
  await cp(releaseA, broken, { recursive: true })
  const file = path.join(broken, 'application.json')
  const json = JSON.parse(await readFile(file, 'utf8'))
  delete json.name
  await writeFile(file, JSON.stringify(json, null, 2) + '\n')

  await expect(activateFolder(broken)).rejects.toThrow(/exited with code[\s\S]*Server output:[\s\S]*compile/i)
})
