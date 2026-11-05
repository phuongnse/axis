import { expect, test } from '../test'
import { createFromBuiltIn, expectHeadRequestGoesToAdmin, prepareOrganization, publish, remapAdmin } from '../approvals'
import { csrfHeaders } from '../csrf'
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
})
