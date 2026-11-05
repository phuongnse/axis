import { expect, test } from '../test'
import {
  choose,
  createFromBuiltIn,
  decideFromInbox,
  detailValue,
  expectHeadRequestGoesToAdmin,
  openRequestDetail,
  prepareOrganization,
  publish,
  remapAdmin,
  submitRequest,
} from '../approvals'
import { csrfHeaders } from '../csrf'
import { fieldLabel } from '../fields'
import { pickOption } from '../select'
import { journeyURL } from '../server-settings'
import { useJourneySession } from '../sessions'

// Publishing waits behind the other journeys on the shared publisher, so the two tests each get their own budget.
test.setTimeout(420_000)

test.describe('the first business changes data', () => {
  // The second test uses the application and site path that the first one creates, so they run in order.
  test.describe.configure({ mode: 'serial' })

  const stamp = Date.now().toString(36)
  const name = `DataApprovals${stamp}`
  let sitePath: string

  test('an author creates an application from Approvals, maps its admin slots and publishes release 1', async ({
    page,
  }) => {
    await useJourneySession(page, 'author')
    sitePath = await createFromBuiltIn(page, name)
    await remapAdmin(page, name)
    await publish(page, name, 1)

    const rows = page.getByTestId('studio-release-row')
    await expect(rows).toHaveAttribute('data-number', '1')
    await expect(rows).toHaveAttribute('data-active', 'true')
  })

  test('a department head submits a request, and it goes to an administrator, not to the head', async ({ page }) => {
    await useJourneySession(page, 'manager')
    await prepareOrganization(page.request, name, stamp)

    const typeName = `Head request ${stamp}`
    const response = await page.request.post(`${journeyURL}/api/apps/${name}/entities/approvals/RequestType/records`, {
      data: {
        values: {
          name: typeName,
          code: `HT${stamp.toUpperCase()}`,
          steps: [{ sequence: 1, approver: 'departmentHead' }],
        },
      },
      headers: csrfHeaders,
    })
    expect(response.status(), await response.text()).toBe(201)

    await expectHeadRequestGoesToAdmin(page, sitePath, typeName)
  })

  test("an administrator adds a purchase type with a director's step, and the director's delegate decides that step", async ({
    page,
  }) => {
    await useJourneySession(page, 'manager')
    await page.goto(`${journeyURL}/${sitePath}/approvals/requesttypes`)
    await page.getByRole('button', { name: 'New', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/requesttypeform/new$`))

    const typeName = `Purchase ${stamp}`
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill(typeName)
    await page.getByRole('textbox', { name: 'Code', exact: true }).fill(`PU${stamp.toUpperCase()}`)
    await page.getByRole('button', { name: 'Add row', exact: true }).click()
    await page.getByRole('button', { name: 'Add row', exact: true }).click()
    await page.getByLabel(fieldLabel('Sequence 1')).fill('1')
    await pickOption(page.getByRole('combobox', { name: 'Approver 1', exact: true }), 'Line manager')
    await page.getByLabel(fieldLabel('Sequence 2')).fill('2')
    await pickOption(page.getByRole('combobox', { name: 'Approver 2', exact: true }), 'Named employee')
    await page.getByLabel(fieldLabel('Minimum amount 2')).fill('1000')
    await choose(page, 'steps-1-employee', 'Employee 2', `Approver ${stamp}`)
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/${sitePath}/approvals/requesttypes$`))
    await expect(page.getByRole('row', { name: new RegExp(typeName) })).toBeVisible()

    const title = `Purchase request ${Date.now().toString(36)}`
    await useJourneySession(page, 'employee')
    await submitRequest(page, sitePath, typeName, title, '1500')

    await useJourneySession(page, 'manager')
    await decideFromInbox(page, sitePath, title)

    await useJourneySession(page, 'employee')
    await openRequestDetail(page, sitePath, title)
    await expect(async () => {
      await page.reload()
      await expect(detailValue(page, 'Status')).toHaveText('Submitted', { timeout: 1_000 })
      await expect(detailValue(page, 'Steps required')).toHaveText('2', { timeout: 1_000 })
      await expect(detailValue(page, 'Steps decided')).toHaveText('1', { timeout: 1_000 })
      await expect(detailValue(page, "Approver's account")).toHaveText('manager-2', { timeout: 1_000 })
      await expect(detailValue(page, 'On behalf of')).toHaveText('approver', { timeout: 1_000 })
    }).toPass({ timeout: 30_000 })

    await useJourneySession(page, 'manager-2')
    await decideFromInbox(page, sitePath, title)

    await useJourneySession(page, 'employee')
    await page.goto(`${journeyURL}/${sitePath}/approvals/myrequests`)
    await expect(async () => {
      await page.reload()
      await expect(page.getByRole('row', { name: new RegExp(title) })).toContainText('Approved', { timeout: 1_000 })
    }).toPass({ timeout: 30_000 })

    await openRequestDetail(page, sitePath, title)
    await expect(detailValue(page, 'Status')).toHaveText('Approved')
    const decisions = page.locator('tbody tr[data-row-key]')
    await expect(decisions).toHaveCount(2)
    await expect(decisions.filter({ hasText: 'manager-2' })).toHaveCount(1)
    await expect(decisions.filter({ hasText: 'manager-2' })).toContainText('approver')
  })
})
