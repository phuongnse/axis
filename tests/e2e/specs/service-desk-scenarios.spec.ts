import { cp, mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import type { Page } from '@playwright/test'
import { startServer } from '../activate'
import { compareApplicationFolders } from '../folder-comparison'
import { pickAction, pickOption, pickOptions } from '../select'
import { e2eApp, serviceDesk } from '../server-settings'
import { useSession } from '../sessions'
import { expectForm, pick, save, sample } from '../service-desk-studio'
import { createResource } from '../studio-resources'
import { expect, test } from '../test'

interface SampleScenario {
  name: string
  now: string
  users: Record<string, string[]>
  steps: Record<string, unknown>[]
}

const scenarios = [
  ['TicketIsClosed', 'scenarios/ticket-is-closed.json'],
  ['TicketIsRejected', 'scenarios/ticket-is-rejected.json'],
  ['ReopenedTicketReturnsToTheAgent', 'scenarios/reopened-ticket-returns-to-the-agent.json'],
  ['AgentCannotActOnAnotherAgentsTicket', 'scenarios/agent-cannot-act-on-another-agents-ticket.json'],
  ['LeadCannotActForTheRequester', 'scenarios/lead-cannot-act-for-the-requester.json'],
  ['RequesterCannotReachAnotherTicket', 'scenarios/requester-cannot-reach-another-ticket.json'],
  ['RequesterDeletesADraftTicket', 'scenarios/requester-deletes-a-draft-ticket.json'],
  ['AgentCannotDeleteATicket', 'scenarios/agent-cannot-delete-a-ticket.json'],
  ['LeadIsRemindedOnTheNextWeekdayMorning', 'scenarios/lead-is-reminded-on-the-next-weekday-morning.json'],
  ['LeadIsToldWhichTicketWasDeleted', 'scenarios/lead-is-told-which-ticket-was-deleted.json'],
  ['LateTicketEscalatesToTheLead', 'scenarios/late-ticket-escalates-to-the-lead.json'],
  ['RequesterIsNotifiedOfTheResolution', 'scenarios/requester-is-notified-of-the-resolution.json'],
] as const

// Starting the server takes a few seconds, and writing the scenarios takes minutes.
test.setTimeout(300_000)

// A row with more than one select names the one to pick, since pick() would match them all.
async function pickNamed(page: Page, testId: string, name: string, option: string) {
  await pickOption(page.getByTestId(testId).getByRole('combobox', { name, exact: true }), option)
}

async function writeScenarioStep(
  page: Page,
  index: number,
  step: Record<string, unknown>,
  ids: Map<string, string>,
  enumFields: Set<string>,
) {
  const at = `studio-field-/steps/${index}`
  await pick(page, `${at}/type`, String(step.type))
  for (const key of ['entity', 'record', 'as', 'process', 'step', 'outcome', 'state'] as const) {
    if (key in step) {
      await pick(page, `${at}/${key}`, String(step[key]))
    }
  }
  if ('save' in step) {
    await page.getByTestId(`${at}/save`).getByRole('textbox').fill(String(step.save))
  }
  if ('assignee' in step) {
    const [[kind, name]] = Object.entries(step.assignee as Record<string, string>)
    await pickNamed(page, `${at}/assignee`, 'Assignee type', kind === 'role' ? 'Role' : 'User')
    await pickNamed(page, `${at}/assignee`, kind === 'role' ? 'Role' : 'User', name)
  }
  if ('by' in step) {
    await page.getByTestId(`${at}/by`).getByRole('textbox').fill(String(step.by))
  }
  if ('recipient' in step) {
    const [[kind, name]] = Object.entries(step.recipient as Record<string, string>)
    await pickNamed(page, `${at}/recipient`, 'Recipient type', kind === 'role' ? 'Role' : 'User')
    await pickNamed(page, `${at}/recipient`, kind === 'role' ? 'Role' : 'User', name)
  }
  if ('channel' in step) {
    await pick(page, `${at}/channel`, String(step.channel))
  }
  if ('values' in step && step.type === 'expectNotification') {
    const values = page.getByTestId(`${at}/values`)
    for (const [key, value] of Object.entries(step.values as Record<string, unknown>)) {
      await values.getByRole('button', { name: 'Add value', exact: true }).click()
      // The new row is the last one, under a fresh key. The rename detaches the input, so Tab goes through the page keyboard.
      await values.getByRole('textbox', { name: 'Value name', exact: true }).last().fill(key)
      await page.keyboard.press('Tab')
      // The box parses its text as JSON, so the text of the number 1 is `1`.
      await values.getByRole('textbox', { name: key, exact: true }).fill(String(value))
    }
  } else if ('values' in step) {
    for (const [key, value] of Object.entries(step.values as Record<string, unknown>)) {
      await pickAction(
        page.getByTestId(`${at}/values`).getByRole('combobox', { name: 'Add value', exact: true }),
        key,
        page.getByTestId(`${at}/values/${key}`),
      )
      if (enumFields.has(key)) {
        await pick(page, `${at}/values/${key}`, String(value))
      } else {
        await page
          .getByTestId(`${at}/values/${key}`)
          .getByRole('textbox')
          .fill(String(ids.get(String(value)) ?? value))
      }
    }
  }
  if ('expect' in step) {
    await pick(page, `${at}/expect`, String(step.expect))
  }
}

test.describe('the author writes the test scenarios in Studio', () => {
  // The scenarios are written in order, and the run at the end needs all twelve.
  test.describe.configure({ mode: 'serial' })

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-service-desk-scenarios-'))
    await cp(serviceDesk, copy, { recursive: true })
    await rm(path.join(copy, 'scenarios'), { recursive: true, force: true })
    server = await startServer({
      ActivateOnStartup__0: e2eApp,
      Studio__Folders__0: copy,
    })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  for (const [name, file] of scenarios) {
    test(`the author writes the ${name} scenario in the scenario editor`, async ({ page }) => {
      await useSession(page, 'author')
      await page.goto(`${server.url}/studio?app=ServiceDesk`)

      await createResource(page, 'scenario', name)
      await expect(page.getByTestId('studio-resource-details')).toContainText(file)
      await expectForm(page)

      const scenario = await sample<SampleScenario>(file)
      const ticket = await sample<{ fields: { name: string; type: string }[] }>('entities/ticket.json')
      const enumFields = new Set(ticket.fields.filter((field) => field.type === 'enum').map((field) => field.name))

      await page.getByTestId('studio-field-/now').getByRole('textbox').fill(scenario.now)
      for (const [index, [user, roles]] of Object.entries(scenario.users).entries()) {
        if (index > 0) {
          await page.getByRole('button', { name: 'Add user', exact: true }).click()
        }
        const newRow = page.getByTestId(`studio-field-/users/${index === 0 ? 'author' : 'user1'}`)
        const userId = newRow.getByRole('textbox', { name: 'User id', exact: true })
        await userId.fill(user)
        // The rename detaches this input, so Tab goes through the page keyboard rather than the locator.
        await page.keyboard.press('Tab')
        const row = page.getByTestId(`studio-field-/users/${user}`)
        await expect(row).toBeVisible()
        await pickOptions(row.getByRole('combobox', { name: 'Roles', exact: true }), roles)
        for (const role of roles) {
          await expect(row).toContainText(role)
        }
      }

      for (const [index, step] of scenario.steps.entries()) {
        if (index > 0) {
          await page.getByRole('button', { name: 'Add step', exact: true }).click()
        }
        await writeScenarioStep(page, index, step, new Map(), enumFields)
      }

      const diagnostics = await save(page)
      const errors = diagnostics.filter((diagnostic) => diagnostic.file === file && diagnostic.severity === 'error')
      expect(errors).toEqual([])
      await expectForm(page)
    })
  }

  test('the written scenarios match the sample', async ({ page }) => {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=ServiceDesk`)

    compareApplicationFolders(serviceDesk, copy)
    const response = await page.request.get(`${server.url}/api/studio/apps/ServiceDesk/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string }[] }
    expect(diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])
  })

  test('the author runs the twelve test scenarios from Studio', async ({ page }) => {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=ServiceDesk`)
    await page.getByTestId('studio-run-tests').click()

    // Each scenario creates and drops a database, so the run takes a while.
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 240_000,
    })
    await expect(page.getByTestId('studio-test-result')).toHaveCount(scenarios.length)
    await expect(page.locator('[data-testid="studio-test-result"][data-outcome="passed"]')).toHaveCount(
      scenarios.length,
    )
  })
})
