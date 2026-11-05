import { access } from 'node:fs/promises'
import path from 'node:path'
import { expect, test } from '../test'
import {
  applicationFolder,
  createFromBuiltIn,
  expectHeadRequestGoesToAdmin,
  prepareOrganization,
  publish,
  remapAdmin,
} from '../approvals'
import { fieldLabel } from '../fields'
import { journeyURL } from '../server-settings'
import { pickOption } from '../select'
import { useJourneySession } from '../sessions'
import { openKind } from '../studio-resources'

// Publishing waits behind the other journeys on the shared publisher, so the two tests each get their own budget.
test.setTimeout(420_000)

test.describe('the second business changes the module', () => {
  // The second test uses the application and site path that the first one creates, so they run in order.
  test.describe.configure({ mode: 'serial' })

  const stamp = Date.now().toString(36)
  const name = `ModuleApprovals${stamp}`
  let sitePath: string

  test('an author adds a field to the built-in Request entity as a layer, and it publishes as release 1', async ({
    page,
  }) => {
    await useJourneySession(page, 'author')
    sitePath = await createFromBuiltIn(page, name)

    await openKind(page, 'entity')
    await page
      .getByTestId('studio-group-module-entities')
      .getByRole('button', { name: 'approvals/Request', exact: true })
      .click()
    await expect(page.getByTestId('studio-entity-designer')).toBeVisible()

    await page.getByRole('button', { name: 'Add field', exact: true }).click()
    await page.getByTestId('studio-field-/fields/16/name').getByRole('textbox').fill('costCenter')
    await page.getByTestId('studio-save').click()

    await expect(page.getByTestId('studio-saved')).toBeVisible()
    await expect(page.getByTestId('studio-module-layer')).toBeVisible()
    await expect(
      access(path.join(await applicationFolder(name), 'customizations', 'approvals', 'Request.json')),
    ).resolves.toBeUndefined()

    await remapAdmin(page, name)
    await publish(page, name, 1)

    const rows = page.getByTestId('studio-release-row')
    await expect(rows).toHaveAttribute('data-number', '1')
    await expect(rows).toHaveAttribute('data-active', 'true')
  })

  test('a manager creates a Travel advance request type, and a department head request goes to an administrator', async ({
    page,
  }) => {
    await useJourneySession(page, 'manager')
    await prepareOrganization(page.request, name, stamp)

    await page.goto(`${journeyURL}/${sitePath}/approvals/requesttypes`)
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/requesttypeform/new$`))
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Travel advance')
    await page.getByRole('textbox', { name: 'Code', exact: true }).fill(`TA${stamp.toUpperCase()}`)
    await page.getByRole('button', { name: 'Add row', exact: true }).click()
    await page.getByLabel(fieldLabel('Sequence 1')).fill('1')
    await pickOption(page.getByRole('combobox', { name: 'Approver 1', exact: true }), 'Department head')
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/requesttypes$`))
    await expect(page.getByRole('row', { name: /Travel advance/ })).toBeVisible()

    await expectHeadRequestGoesToAdmin(page, sitePath, 'Travel advance')
  })
})
