import { mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { chromium, type Browser, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { waitForDialog } from '../dialogs'
import { startDirectoryServer } from '../directory-server'
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

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)

const tenant = 'packages-made'
const host = `${tenant}.localhost`
const application = 'PackageTools'
const packageId = 'purchasing-tools'

/** The paths that `GET .../package-files` lists for the application in `page`. */
function packageFilePaths(page: Page): Promise<string[]> {
  return page.evaluate(async (app) => {
    const listed = (await (await fetch(`/api/studio/apps/${app}/package-files`)).json()) as { items: { path: string }[] }
    return listed.items.map((item) => item.path)
  }, application)
}

test('a developer creates a package in Studio, opens its workspace, saves a file, sees a manifest error in Problems, and creates and renames a folder with a file in it', async () => {
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
  } finally {
    await browser?.close()
    await server.stop()
    await rm(applications, { recursive: true, force: true })
  }
})
