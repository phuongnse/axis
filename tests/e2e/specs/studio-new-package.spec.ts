import { mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { startSpecPublisher } from '../ai-authoring'
import { runAdmin } from '../admin-tool'
import { waitForDialog } from '../dialogs'
import { startDirectoryServer } from '../directory-server'
import { startPackageHost } from '../package-host'
import { pickOption } from '../select'
import { addRealmAccount, setRealmPassword } from '../tenant-realm'
import {
  giveRoles,
  newApplication,
  newPackage,
  signInAdministrator,
  signInWithCode,
  studioDeveloper,
  turnOnApplicationPackages,
} from '../studio-packages'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak. The build and the test run
// add a publisher that waits for the other publishers' locks, and the package host's Python build and tests.
test.setTimeout(480_000)

const tenant = 'packages-made'
const host = `${tenant}.localhost`
const application = 'PackageTools'
const packageId = 'purchasing-tools'
const checksId = 'checks'

// The failing assertion is on line 10. The line numbers stay as they are, because the spec checks that line.
const testChecks = [
  'import unittest',
  '',
  '',
  'class ChecksCase(unittest.TestCase):',
  '    def test_passes(self):',
  '        print("checking the sum")',
  '        self.assertEqual(2, 1 + 1)',
  '',
  '    def test_fails(self):',
  '        self.assertEqual(1, 2)',
  '',
].join('\n')

/** The paths that `GET .../package-files` lists for the application in `page`. */
function packageFilePaths(page: Page): Promise<string[]> {
  return page.evaluate(async (app) => {
    const listed = (await (await fetch(`/api/studio/apps/${app}/package-files`)).json()) as { items: { path: string }[] }
    return listed.items.map((item) => item.path)
  }, application)
}

test('a developer creates a package in Studio, opens its workspace, saves a file, sees a manifest error in Problems, creates and renames a folder with a file in it, then builds and tests a Python package', async () => {
  const created = await runAdmin([
    'tenant',
    'create',
    tenant,
    '--host',
    host,
    '--administrator',
    `first-admin@${tenant}.test`,
  ])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  const developer = await addRealmAccount(tenant, { email: `developer@${tenant}.test` })

  // Offset 11, because offsets 0 to 6 and 8 to 10 are taken, and offset 7 is the port of the control plane. The main
  // E2E server's tenant has no directory row, so it cannot store the application packages switch.
  const applications = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-new-package-'))
  const server = await startDirectoryServer(11, {
    Studio__AuthorRole: 'author',
    Studio__DeveloperRole: 'developer',
    Studio__ApplicationsPath: applications,
    // The switch reaches the server at its next read of the directory.
    Tenancy__RefreshInterval: '00:00:02',
  })
  let browser: Browser | undefined
  try {
    browser = await chromium.launch({
      args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
    })

    // The developer signs in first, with no role, so the denied case is checked before anything is given. The session
    // reads the given roles at every request, so this page is used again after the roles are given.
    const developerPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
    await signInWithCode(developerPage, host, developer)
    expect(await studioDeveloper(developerPage)).toBe(false)

    const administratorPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
    const firstAdmin = await setRealmPassword(tenant, `first-admin@${tenant}.test`)
    await signInAdministrator(administratorPage, host, firstAdmin)

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
    for (const cell of ['Purchasing tools', packageId, 'TypeScript', '0.1.0']) {
      await expect(row.getByRole('cell', { name: cell, exact: true })).toBeVisible()
    }

    await row.getByRole('link', { name: 'Purchasing tools', exact: true }).click()
    await expect(developerPage).toHaveURL(new RegExp(`[?&]package=${packageId}(&|$)`))
    await expect(developerPage.getByRole('heading', { name: 'Purchasing tools', exact: true })).toBeVisible()
    await expect(
      developerPage.getByTestId('studio-package-tree').getByText('axis-package.json', { exact: true }),
    ).toBeVisible()

    // The starter's code is index.js. An edit saves with Ctrl+S, and the status line shows nothing unsaved.
    const tree = developerPage.getByTestId('studio-package-tree')
    await tree.getByText('index.js', { exact: true }).click()
    const adapter = developerPage.getByRole('textbox', { name: 'index.js', exact: true })
    await adapter.click()
    await developerPage.keyboard.press('Control+End')
    await developerPage.keyboard.press('Enter')
    await developerPage.keyboard.type('// edited')
    // Escape closes the word completion, so Ctrl+S does not follow a picked word.
    await developerPage.keyboard.press('Escape')
    await expect(developerPage.getByTestId('studio-package-status')).toContainText('1 unsaved file')
    await developerPage.keyboard.press('Control+S')
    await expect(developerPage.getByTestId('studio-package-status')).toContainText('No unsaved files')

    // An invalid manifest saves, and the folder's check answers AXC0132 at its file in the Problems tab.
    await tree.getByText('axis-package.json', { exact: true }).click()
    await developerPage.getByRole('textbox', { name: 'axis-package.json', exact: true }).click()
    await developerPage.keyboard.press('Control+A')
    await developerPage.keyboard.type('x')
    await developerPage.keyboard.press('Control+S')
    const problems = developerPage.getByTestId('studio-package-output').getByRole('tabpanel')
    await expect(problems).toContainText('AXC0132')
    await expect(problems).toContainText(`packages/${packageId}/axis-package.json`)

    // A folder and a file in it are made from the explorer's menus. Renaming the folder moves the file with it.
    await developerPage
      .getByRole('region', { name: 'Files', exact: true })
      .getByRole('button', { name: 'New', exact: true })
      .click()
    await developerPage.getByRole('menuitem', { name: 'New folder…', exact: true }).click()
    const newFolder = await waitForDialog(developerPage, 'New folder')
    await newFolder.getByRole('textbox', { name: 'Name', exact: true }).fill('lib')
    await newFolder.getByRole('button', { name: 'Create', exact: true }).click()

    // The folder is made from its own menu, not F2: a click on a folder also folds it, and F2 acts on the clicked node.
    await tree.getByText('lib', { exact: true }).click({ button: 'right' })
    await developerPage.getByRole('menuitem', { name: 'New file…', exact: true }).click()
    const newFile = await waitForDialog(developerPage, 'New file')
    await newFile.getByRole('textbox', { name: 'Name', exact: true }).fill('util.js')
    await newFile.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(developerPage.getByRole('textbox', { name: 'lib/util.js', exact: true })).toBeVisible()

    await tree.getByText('lib', { exact: true }).click({ button: 'right' })
    await developerPage.getByRole('menuitem', { name: 'Rename…', exact: true }).click()
    const rename = await waitForDialog(developerPage, 'Rename lib')
    const renameField = rename.getByRole('textbox', { name: 'Name', exact: true })
    await renameField.clear()
    await renameField.fill('shared')
    await rename.getByRole('button', { name: 'Rename', exact: true }).click()

    await expect(tree.getByText('shared', { exact: true })).toBeVisible()
    await expect(tree.getByText('util.js', { exact: true })).toBeVisible()
    await expect(tree.getByText('lib', { exact: true })).toHaveCount(0)
    await expect(developerPage.getByRole('textbox', { name: 'shared/util.js', exact: true })).toBeVisible()
    await expect
      .poll(async () => {
        const paths = await packageFilePaths(developerPage)
        return paths.includes('shared/util.js') && !paths.includes('lib/util.js')
      })
      .toBe(true)

    // A Python package with a test file. The file is written from the page, through the files route, so its session is used.
    await developerPage
      .getByRole('navigation', { name: 'Menu', exact: true })
      .getByRole('link', { name: 'Packages', exact: true })
      .click()
    await expect(developerPage).toHaveURL(/[?&]view=packages(&|$)/)

    await developerPage.getByRole('button', { name: 'New package…', exact: true }).click()
    const newPython = await waitForDialog(developerPage, 'New package')
    await newPython.getByRole('textbox', { name: 'ID', exact: true }).fill(checksId)
    await pickOption(newPython.getByRole('combobox', { name: 'Language', exact: true }), 'Python')
    await newPython.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(newPython).toBeHidden({ timeout: 30_000 })

    const written = await developerPage.evaluate(
      ({ application, id, content }) =>
        fetch(`/api/studio/apps/${application}/packages/${id}/files/test_checks.py`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json', 'If-None-Match': '*', 'X-Axis-Csrf': '1' },
          body: JSON.stringify({ content }),
        }).then((response) => response.status),
      { application, id: checksId, content: testChecks },
    )
    expect(written).toBe(200)

    const checksRow = developerPage
      .getByTestId('studio-packages')
      .getByRole('row')
      .filter({ has: developerPage.getByTestId(`studio-package-${checksId}`) })
    await checksRow.getByRole('link', { name: 'Checks', exact: true }).click()
    await expect(developerPage).toHaveURL(new RegExp(`[?&]package=${checksId}(&|$)`))
    await expect(developerPage.getByRole('heading', { name: 'Checks', exact: true })).toBeVisible()
    await expect(
      developerPage.getByTestId('studio-package-tree').getByText('test_checks.py', { exact: true }),
    ).toBeVisible()

    // The build and the test run need a package host and a publisher that builds application packages. The publisher runs
    // alone under the publishers' lock, and both stop when these steps end, see docs/testing.md.
    const output = developerPage.getByTestId('studio-package-output')
    const passed = output.getByTestId('studio-test-result').and(developerPage.locator('[data-outcome="passed"]'))
    const failed = output.getByTestId('studio-test-result').and(developerPage.locator('[data-outcome="failed"]'))
    const packageHost = await startPackageHost()
    let publisher: Awaited<ReturnType<typeof startSpecPublisher>> | undefined
    try {
      publisher = await startSpecPublisher(applications, {
        Extensions__ApplicationPackages: 'true',
        Extensions__Runners__Application: packageHost.address,
        // Port 0 keeps the publisher's egress proxy off the fixed port of the shared worker's proxy.
        Extensions__Runners__ProxyPort: '0',
      })

      await developerPage.getByRole('button', { name: 'Build', exact: true }).click()
      // The publisher may wait for the other publishers' locks first, so the build waits longer than a page's usual wait.
      await expect(developerPage.getByTestId('studio-package-build-state')).toHaveText('Built', { timeout: 180_000 })
      await expect(developerPage.getByTestId('studio-package-build-output')).toContainText(
        `Checked the manifest of ${checksId} 0.1.0.`,
      )

      await developerPage.getByRole('button', { name: 'Run tests', exact: true }).click()
      await expect(passed).toHaveCount(1, { timeout: 180_000 })
      await expect(failed).toHaveCount(1)
      await expect(passed).toContainText('test_checks.ChecksCase.test_passes')

      // A syntax error on line 2 fails the build with a problem that has a line, so the Problems tab has a place to link.
      const broken = await developerPage.evaluate(
        ({ application, id, content }) =>
          fetch(`/api/studio/apps/${application}/packages/${id}/files/broken.py`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json', 'If-None-Match': '*', 'X-Axis-Csrf': '1' },
            body: JSON.stringify({ content }),
          }).then((response) => response.status),
        { application, id: checksId, content: ['x = 1', 'def broken(:', ''].join('\n') },
      )
      expect(broken).toBe(200)

      await developerPage.getByRole('button', { name: 'Build', exact: true }).click()
      await expect(developerPage.getByTestId('studio-package-build-state')).toHaveText('Failed', { timeout: 180_000 })
      await output.getByRole('tab', { name: 'Problems', exact: true }).click()
      await output.getByRole('link', { name: 'packages/checks/broken.py', exact: true }).click()
      await expect(developerPage).toHaveURL(/[?&]file=broken\.py&line=2(&|$)/)
      await expect(developerPage.getByTestId('studio-package-status')).toContainText('Line 2, column 1')
    } catch (error) {
      await test.info().attach('publisher-output', { body: publisher?.output() ?? '', contentType: 'text/plain' })
      await test.info().attach('package-host-output', { body: packageHost.output(), contentType: 'text/plain' })
      throw error
    } finally {
      await publisher?.stop()
      await packageHost.stop()
    }

    // The failed test's place is a link to its file at that line. The Tests tab holds the last test run, so the build's
    // Output tab is left first.
    await output.getByRole('tab', { name: 'Tests', exact: true }).click()
    await failed.getByRole('link', { name: 'packages/checks/test_checks.py, line 10', exact: true }).click()
    await expect(developerPage).toHaveURL(/[?&]file=test_checks\.py&line=10(&|$)/)
    await expect(developerPage.getByTestId('studio-package-status')).toContainText('Line 10, column 1')

    // A reload opens the same file at the same line, and Back returns to the broken file at its line.
    await developerPage.reload()
    await expect(developerPage.getByRole('textbox', { name: 'test_checks.py', exact: true })).toBeVisible()
    await expect(developerPage.getByTestId('studio-package-status')).toContainText('Line 10, column 1')
    await developerPage.goBack()
    await expect(developerPage).toHaveURL(/[?&]file=broken\.py&line=2(&|$)/)
    await expect(developerPage.getByTestId('studio-package-status')).toContainText('Line 2, column 1')
  } finally {
    await browser?.close()
    await server.stop()
    await rm(applications, { recursive: true, force: true })
  }
})
