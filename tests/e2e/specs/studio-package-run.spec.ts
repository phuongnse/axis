import { mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin, startPublisher } from '../admin-tool'
import { waitForDialog } from '../dialogs'
import { startDirectoryServer } from '../directory-server'
import { startPackageHost } from '../package-host'
import { openSelect, pickOption } from '../select'
import { required } from '../server-settings'
import {
  giveRoles,
  newApplication,
  newPackage,
  signInAdministrator,
  signInWithCode,
  studioDeveloper,
  turnOnApplicationPackages,
} from '../studio-packages'
import { addRealmAccount, setRealmPassword, type RealmAccount } from '../tenant-realm'

// A developer saves a named run input, reloads and finds it in Saved input. Then the developer runs the TypeScript
// starter's `echo` and reads Succeeded, the output and the package host's log. The spec starts its own package host and
// its own publisher with application packages on, because the shared E2E stack has neither, see the package run spec in
// docs/testing.md.

// Each test waits for a build that runs `npm ci` and for the publishers' lock, so the tests take minutes.
test.setTimeout(360_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'packages-run'
const host = `${tenant}.localhost`
const application = 'RunTools'
const packageId = 'run-tools'
// What a build of a Node.js package writes once `npm ci` has installed the dependencies of its lock.
const installedLine = "Installed the dependencies that 'package-lock.json' locks."

let developer: RealmAccount
let applications: string | undefined
let packageHost: Awaited<ReturnType<typeof startPackageHost>> | undefined
let publisher: Awaited<ReturnType<typeof startPublisher>> | undefined
let server: Awaited<ReturnType<typeof startDirectoryServer>> | undefined
let browser: Browser | undefined
// The developer's page is kept for both tests, because a TOTP code cannot be used twice within its period.
let developerPage: Page

/** Runs each cleanup in turn, even after one fails, and then throws the first failure. */
async function cleanUp(...steps: (() => Promise<unknown> | undefined)[]) {
  let failure: unknown
  for (const step of steps) {
    try {
      await step()
    } catch (error) {
      failure ??= error
    }
  }
  if (failure !== undefined) throw failure
}

test.beforeAll(async () => {
  // The publisher waits for the publishers' lock, which a cell spec or an AI authoring spec may hold for minutes.
  test.setTimeout(900_000)
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  developer = await addRealmAccount(tenant, { email: `developer@${tenant}.test` })

  applications = await mkdtemp(path.join(os.tmpdir(), 'axis-package-run-'))
  packageHost = await startPackageHost()
  // The publisher reads the tenant directory when it starts, so it starts after `tenant create`. It takes the publishers'
  // lock alone, because it claims the work of every directory tenant and a cell's publisher would fail this run.
  publisher = await startPublisher(
    {
      Extensions__ApplicationPackages: 'true',
      Extensions__Runners__Application: packageHost.address,
      Extensions__Runners__ProxyPort: '0',
      Studio__ApplicationsPath: applications,
      Studio__AuthorRole: 'author',
      Studio__DeveloperRole: 'developer',
      ConnectionStrings__Platform: required('AXIS_E2E_DATABASE', 'the PostgreSQL connection string of the runtime role'),
    },
    { alone: true },
  )

  // Offset 12, because offsets 0 to 6 and 8 to 11 are taken, and offset 7 is the port of the control plane.
  server = await startDirectoryServer(12, {
    Studio__AuthorRole: 'author',
    Studio__DeveloperRole: 'developer',
    Studio__ApplicationsPath: applications,
    // The switch and the roles reach the server at its next read of the directory.
    Tenancy__RefreshInterval: '00:00:02',
  })
  browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })

  // The developer signs in first, with no role. The session reads the given roles at every request, so it stays signed in.
  developerPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  await signInWithCode(developerPage, host, developer)

  const administratorPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
  const administrator = await setRealmPassword(tenant, `first-admin@${tenant}.test`)
  await signInAdministrator(administratorPage, host, administrator)
  await turnOnApplicationPackages(administratorPage, host)
  await giveRoles(administratorPage, host, developer.id, ['author', 'developer'])

  // The switch and the roles reach the server within a few seconds, so the answer is polled, not waited for in time.
  await expect.poll(() => studioDeveloper(developerPage), { timeout: 30_000 }).toBe(true)

  await newApplication(developerPage, host, application)
  await newPackage(developerPage, packageId, 'TypeScript')
  const row = developerPage
    .getByTestId('studio-packages')
    .getByRole('row')
    .filter({ has: developerPage.getByTestId(`studio-package-${packageId}`) })
  await row.getByRole('link', { name: 'Run tools', exact: true }).click()
  await expect(developerPage.getByRole('heading', { name: 'Run tools', exact: true })).toBeVisible()
})

test.afterEach(async ({}, testInfo) => {
  if (testInfo.status === testInfo.expectedStatus) return
  // The reason of a failure is in the output of the publisher and of the package host.
  await testInfo.attach('publisher-output.txt', { body: publisher?.output() ?? 'The publisher did not start.' })
  await testInfo.attach('package-host-output.txt', { body: packageHost?.output() ?? 'The package host did not start.' })
})

test.afterAll(async () => {
  await cleanUp(
    () => browser?.close(),
    () => server?.stop(),
    // The publisher holds the publishers' lock until it stops, so it stops before the package host it calls.
    () => publisher?.stop(),
    () => packageHost?.stop(),
    () => (applications ? rm(applications, { recursive: true, force: true }) : undefined),
  )
})

test('a developer saves a run input under a name, and Saved input lists it after a reload', async () => {
  await developerPage.getByRole('button', { name: 'Run…', exact: true }).click()
  const input = developerPage.getByRole('region', { name: 'Input', exact: true })
  await input.getByRole('textbox', { name: 'text', exact: true }).fill('hello')

  await openSelect(input.getByRole('combobox', { name: 'Saved input', exact: true }))
  await developerPage.getByRole('menuitem', { name: 'Manage saved inputs…', exact: true }).click()
  const dialog = await waitForDialog(developerPage, 'Saved inputs')
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('greeting')
  await dialog.getByRole('button', { name: 'Save input', exact: true }).click()
  // A save that the server accepts closes the dialog, so the name is looked for in Saved input after the reload.
  await expect(dialog).toBeHidden()

  await developerPage.reload()
  await expect(developerPage.getByRole('heading', { name: 'Run tools', exact: true })).toBeVisible()
  await developerPage.getByRole('button', { name: 'Run…', exact: true }).click()
  const reopened = developerPage.getByRole('region', { name: 'Input', exact: true })
  const savedInput = reopened.getByRole('combobox', { name: 'Saved input', exact: true })
  const choices = await openSelect(savedInput)
  await expect(choices.getByRole('option', { name: 'greeting', exact: true })).toBeVisible()

  await pickOption(savedInput, 'greeting')
  await expect(reopened.getByRole('textbox', { name: 'text', exact: true })).toHaveValue('hello')
})

test('Run reads a succeeded outcome, the output JSON and the log of the package host', async () => {
  const input = developerPage.getByRole('region', { name: 'Input', exact: true })
  await input.getByRole('button', { name: 'Run', exact: true }).click()

  // The outcome waits for the build, which runs `npm ci` before the operation.
  await expect(developerPage.getByTestId('studio-run-outcome')).toHaveText('Succeeded', { timeout: 120_000 })
  await expect(developerPage.getByTestId('studio-run-duration')).toBeVisible()
  const output = developerPage.getByRole('region', { name: 'Output', exact: true })
  await expect(output.getByRole('textbox', { name: 'Output', exact: true })).toContainText('"text": "hello"')
  await expect(developerPage.getByTestId('studio-run-log')).toContainText(installedLine)
})
