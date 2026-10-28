import { expect, test } from '../test'
import { randomUUID } from 'node:crypto'
import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { startServer } from '../activate'
import { openSelect, pickOption } from '../select'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'
import { openFormTab } from '../studio-resources'

// A server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

type ProcessStep = {
  name: string
  type: string
  next?: string
  otherwise?: string
  branches?: { next: string }[]
  outcomes?: { next: string }[]
}

// A transition draws an edge only when its target step exists, and names match ignoring letter case.
function drawnEdgePaths(steps: ProcessStep[]): string[] {
  const names = new Set(steps.map((step) => step.name.toUpperCase()))
  const transitions = steps.flatMap((step, i) => [
    ...(step.branches ?? []).map((branch, j) => [`/steps/${i}/branches/${j}`, branch.next] as const),
    [`/steps/${i}/otherwise`, step.otherwise] as const,
    [`/steps/${i}/next`, step.next] as const,
    ...(step.outcomes ?? []).map((outcome, j) => [`/steps/${i}/outcomes/${j}`, outcome.next] as const),
  ])
  return transitions
    .filter(([, target]) => target !== undefined && names.has(target.toUpperCase()))
    .map(([transitionPath]) => transitionPath)
}

test.describe('Studio on a copy of its own', () => {
  // The tests share one server and one folder, and they change files in it, so they run in order in one worker.
  test.describe.configure({ mode: 'default' })

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-'))
    await cp(purchaseRequests, copy, { recursive: true })
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  test('an author opens Studio from the app switcher, searches for a resource and opens it', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const entity = items.find((item) => item.file === 'entities/purchase-request.json')!

    await page.goto(`${server.url}/`)
    await page.getByRole('banner').getByRole('button', { name: 'Switch to', exact: true }).click()
    await page.getByRole('menuitem', { name: 'Studio', exact: true }).click()
    // The sample is the only application, so it is picked without a choice.
    await expect(page).toHaveURL(/\/studio\?app=PurchaseRequests$/)
    await page.getByRole('searchbox', { name: 'Search by name or id', exact: true }).fill('PurchaseRequest')
    await page.getByTestId(`studio-resource-${entity.id}`).click()

    await expect(page.getByTestId('studio-resource-details')).toContainText('entities/purchase-request.json')
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-diagnostics')).toBeVisible()
  })

  test('an author changes the maxLength of an entity field and the file holds only that change', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const entity = items.find((item) => item.file === 'entities/purchase-request.json')!
    const file = path.join(copy, 'entities/purchase-request.json')
    const before = JSON.parse(await readFile(file, 'utf8')) as { fields: { name: string; maxLength: number }[] }
    const index = before.fields.findIndex((field) => field.name === 'title')
    // One more than the file holds, so the value always changes.
    const next = before.fields[index].maxLength + 1

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${entity.id}`)
    await page.getByTestId(`studio-field-/fields/${index}/maxLength`).getByRole('spinbutton').fill(String(next))
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()

    const after: unknown = JSON.parse(await readFile(file, 'utf8'))
    before.fields[index].maxLength = next
    // The string form also compares the order of the keys.
    expect(JSON.stringify(after)).toBe(JSON.stringify(before))
  })

  test('an author sees an expression error as they type, and its type once fixed, without saving', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const entity = items.find((item) => item.file === 'entities/purchase-request.json')!
    const file = path.join(copy, 'entities/purchase-request.json')
    const before = await readFile(file, 'utf8')

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${entity.id}`)
    const field = page.getByTestId('studio-field-/validations/0/expression')
    const editor = field.getByRole('textbox')
    await expect(field.getByTestId('studio-expression-type')).toContainText('boolean')

    await editor.fill('title > 5')
    await expect(field.locator('.cm-lintRange-error')).toBeVisible()
    await expect(field.getByTestId('studio-expression-error')).toBeVisible()

    await editor.fill('totalAmount > 0')
    await expect(field.locator('.cm-lintRange-error')).toHaveCount(0)
    await expect(field.getByTestId('studio-expression-type')).toContainText('boolean')

    expect(await readFile(file, 'utf8')).toBe(before)
  })

  test('an author completes the names of a policy filter without saving', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const policy = items.find((item) => item.file === 'policies/employee-access.json')!
    const file = path.join(copy, 'policies/employee-access.json')
    const before = await readFile(file, 'utf8')

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${policy.id}`)
    const field = page.getByTestId('studio-field-/grants/0/filter')
    await expect(field.getByTestId('studio-expression-type')).toContainText('boolean')
    const editor = field.getByRole('textbox')
    await editor.click()
    await editor.press('ControlOrMeta+A')
    await editor.pressSequentially('req')
    await expect(page.getByRole('option', { name: 'requester', exact: true })).toBeVisible()

    await editor.press('ControlOrMeta+A')
    await editor.pressSequentially('cur')
    await expect(page.getByRole('option', { name: 'currentUser()', exact: true })).toBeVisible()

    expect(await readFile(file, 'utf8')).toBe(before)
  })

  test('an author follows a Used by entry from a department to the entity that uses it', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const department = items.find((item) => item.file === 'entities/department.json')!
    const entity = items.find((item) => item.file === 'entities/purchase-request.json')!

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${department.id}`)
    await page
      .getByTestId('studio-used-by')
      .getByTestId('studio-reference')
      .filter({ hasText: 'entities/purchase-request.json · Path: /fields/3/target' })
      .getByRole('button', { name: 'PurchaseRequest', exact: true })
      .click()

    await expect(page).toHaveURL(new RegExp(`resource=${entity.id}`))
    await expect(page.getByTestId('studio-resource-details')).toContainText('entities/purchase-request.json')
  })

  test('an author picks a data source of a page from the names and the Uses list shows it', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const pageResource = items.find((item) => item.file === 'pages/requests-by-department.json')!
    const file = path.join(copy, 'pages/requests-by-department.json')
    const original = await readFile(file, 'utf8')

    try {
      await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${pageResource.id}`)
      await openFormTab(page)
      await pickOption(
        page.getByTestId('studio-field-/widgets/0/dataSource').getByRole('combobox'),
        'PurchaseRequestList',
      )
      await page.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(page.getByTestId('studio-saved')).toBeVisible()

      await page.reload()
      await expect(
        page
          .getByTestId('studio-uses')
          .getByTestId('studio-reference')
          .filter({ hasText: 'pages/requests-by-department.json · Path: /widgets/0/dataSource' })
          .getByRole('button', { name: 'PurchaseRequestList', exact: true }),
      ).toBeVisible()
    } finally {
      await writeFile(file, original)
    }
  })

  test('an author opens a grant field picker and it offers the fields of the granted entity', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const policy = items.find((item) => item.file === 'policies/employee-access.json')!
    const names = (await (await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/names`)).json()) as {
      entities: { name: string; fields: { name: string }[] }[]
    }
    const expected = names.entities
      .find((entity) => entity.name === 'PurchaseRequest')!
      .fields.map((field) => field.name)

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${policy.id}`)
    const choices = await openSelect(page.getByTestId('studio-field-/grants/1/fields/0').getByRole('combobox'))

    await expect(choices.getByRole('option')).toHaveText(expected)
  })

  test('an author sees what the employee role may do in the policy matrix', async ({ page }) => {
    await useSession(page, 'hoa')

    await page.goto(`${server.url}/studio?app=PurchaseRequests&view=policies`)

    const entity = page.getByTestId('studio-matrix-cell-employee-entity-PurchaseRequest')
    await expect(entity).toContainText('read')
    await expect(entity).toContainText('create')
    await expect(entity).toContainText('update')
    await expect(page.getByTestId('studio-matrix-cell-employee-process-PurchaseRequestApproval')).toContainText('start')
  })

  test('an author gives a new role read on Supplier in the policy matrix and the access policy is created', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    const roleFile = path.join(copy, 'roles/matrix-tester.json')
    const policyFile = path.join(copy, 'policies/matrix-tester-access.json')
    await rm(roleFile, { force: true })
    await rm(policyFile, { force: true })

    try {
      await writeFile(
        roleFile,
        JSON.stringify({
          id: randomUUID(),
          kind: 'role',
          name: 'matrix-tester',
          formatVersion: 1,
          label: { textKey: 'role.employee' },
        }),
      )
      const diagnosticsBefore = await (
        await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
      ).json()

      await page.goto(`${server.url}/studio?app=PurchaseRequests&view=policies`)
      await page.getByTestId('studio-matrix-cell-matrix-tester-entity-Supplier').click()
      await page.getByRole('button', { name: 'Add grant', exact: true }).click()
      await page.getByTestId('studio-matrix-actions').getByRole('checkbox', { name: 'read', exact: true }).check()
      await page.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(page.getByTestId('studio-matrix-saved')).toBeVisible()

      const written = JSON.parse(await readFile(policyFile, 'utf8')) as Record<string, unknown>
      expect(written).toEqual({
        id: expect.any(String),
        kind: 'policy',
        name: 'MatrixTesterAccess',
        formatVersion: 1,
        roles: ['matrix-tester'],
        grants: [{ entity: 'Supplier', actions: ['read'] }],
      })
      const diagnosticsAfter = await (
        await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
      ).json()
      expect(diagnosticsAfter).toEqual(diagnosticsBefore)
    } finally {
      await rm(roleFile, { force: true })
      await rm(policyFile, { force: true })
    }
  })

  test('an author gives a new role read and export on a data source in the policy matrix and finds both in the policy file', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    const roleFile = path.join(copy, 'roles/matrix-exporter.json')
    const policyFile = path.join(copy, 'policies/matrix-exporter-access.json')
    await rm(roleFile, { force: true })
    await rm(policyFile, { force: true })

    try {
      await writeFile(
        roleFile,
        JSON.stringify({
          id: randomUUID(),
          kind: 'role',
          name: 'matrix-exporter',
          formatVersion: 1,
          label: { textKey: 'role.employee' },
        }),
      )
      const diagnosticsBefore = await (
        await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
      ).json()

      await page.goto(`${server.url}/studio?app=PurchaseRequests&view=policies`)
      await page.getByTestId('studio-matrix-cell-matrix-exporter-dataSource-PurchaseRequestList').click()
      await page.getByRole('button', { name: 'Add grant', exact: true }).click()
      const actions = page.getByTestId('studio-matrix-actions')
      await actions.getByRole('checkbox', { name: 'read', exact: true }).check()
      await actions.getByRole('checkbox', { name: 'export', exact: true }).check()
      await expect(actions.getByRole('checkbox', { name: 'import', exact: true })).toHaveCount(0)
      await page.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(page.getByTestId('studio-matrix-saved')).toBeVisible()

      const written = JSON.parse(await readFile(policyFile, 'utf8')) as Record<string, unknown>
      expect(written).toEqual({
        id: expect.any(String),
        kind: 'policy',
        name: 'MatrixExporterAccess',
        formatVersion: 1,
        roles: ['matrix-exporter'],
        grants: [{ dataSource: 'PurchaseRequestList', actions: ['read', 'export'] }],
      })
      const diagnosticsAfter = await (
        await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
      ).json()
      expect(diagnosticsAfter).toEqual(diagnosticsBefore)
    } finally {
      await rm(roleFile, { force: true })
      await rm(policyFile, { force: true })
    }
  })

  test('an author saves a resource while the server restarts, and sees the saved version after the reconnect', async ({
    page,
  }) => {
    test.setTimeout(240_000)
    // A second server on a spare port, so the restart does not touch the server the other tests use.
    let restarted = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
    try {
      await useSession(page, 'hoa')
      expect((await page.request.get(`${restarted.url}/api/me`)).status()).toBe(200)
      const list = await page.request.get(`${restarted.url}/api/studio/apps/PurchaseRequests/resources`)
      const { items } = (await list.json()) as { items: { id: string; file: string }[] }
      const supplier = items.find((item) => item.file === 'entities/supplier.json')!
      const department = items.find((item) => item.file === 'entities/department.json')!
      const file = path.join(copy, 'entities/supplier.json')
      const before = JSON.parse(await readFile(file, 'utf8')) as { fields: { maxLength: number }[] }
      const next = before.fields[1].maxLength + 1

      await page.goto(`${restarted.url}/studio?app=PurchaseRequests&resource=${supplier.id}`)
      await page.getByTestId('studio-field-/fields/1/maxLength').getByRole('spinbutton').fill(String(next))
      await page.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(page.getByTestId('studio-saved')).toBeVisible()

      const { port } = restarted
      await restarted.stop()
      await page.getByTestId(`studio-resource-${department.id}`).click()
      await expect(page.getByTestId('studio-reconnecting')).toBeVisible()

      restarted = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy }, port)
      await expect(page.getByTestId('studio-reconnecting')).toBeHidden({ timeout: 90_000 })
      await page.getByTestId(`studio-resource-${supplier.id}`).click()
      await expect(page.getByTestId('studio-field-/fields/1/maxLength').getByRole('spinbutton')).toHaveValue(
        String(next),
      )
    } finally {
      await restarted.stop()
    }
  })

  test('an author selects a diagnostic and the editor shows the field at its path', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const endpoint = items.find((item) => item.file === 'endpoints/submit-purchase-request.json')!
    const file = path.join(copy, 'endpoints/submit-purchase-request.json')
    const original = await readFile(file, 'utf8')

    try {
      const content = JSON.parse(original) as { input: { target?: string }[] }
      content.input[2].target = 'NoSuchEntity'
      await writeFile(file, `${JSON.stringify(content, null, 2)}\n`)
      await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${endpoint.id}`)
      await expect(
        page.getByTestId('studio-field-/input/2/target').getByTestId('studio-reference-unknown'),
      ).toBeVisible()

      await page.goto(`${server.url}/studio?app=PurchaseRequests`)
      const diagnostic = page
        .getByTestId('studio-diagnostics')
        .getByRole('button')
        .filter({ hasText: 'Path: /input/2/target' })
      await expect(diagnostic).toContainText('AXC0012')
      await diagnostic.click()

      await expect(page).toHaveURL(/path=%2Finput%2F2%2Ftarget/)
      await expect(page.getByTestId('studio-field-/input/2/target')).toBeInViewport()
    } finally {
      await writeFile(file, original)
    }
  })

  test('an author adds a department in the seed grid and the file holds only that new record', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const seed = items.find((item) => item.file === 'seeds/departments.json')!
    const file = path.join(copy, 'seeds/departments.json')
    const original = await readFile(file, 'utf8')
    const before = JSON.parse(original) as { records: { id: string; values: Record<string, unknown> }[] }
    const diagnosticsBefore = await (
      await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
    ).json()
    // Short and unique, because the code holds at most 20 characters.
    const code = `T${Date.now().toString(36)}`

    try {
      await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${seed.id}`)
      await expect(page.getByTestId('studio-seed-row')).toHaveCount(before.records.length)
      await page.getByRole('button', { name: 'Add record', exact: true }).click()
      const row = page.getByTestId('studio-seed-row').nth(before.records.length)
      await row.getByRole('textbox', { name: 'code', exact: true }).fill(code)
      await row.getByRole('textbox', { name: 'name', exact: true }).fill('Test department')
      await page.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(page.getByTestId('studio-saved')).toBeVisible()

      const diagnosticsAfter = await (
        await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
      ).json()
      expect(diagnosticsAfter).toEqual(diagnosticsBefore)
      const after = JSON.parse(await readFile(file, 'utf8')) as typeof before
      expect(after.records).toHaveLength(before.records.length + 1)
      expect(after.records.slice(0, before.records.length)).toEqual(before.records)
      const added = after.records[before.records.length]
      expect(added.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/)
      expect(added.values).toEqual({ code, name: 'Test department' })
    } finally {
      await writeFile(file, original)
    }
  })

  test('an author adds a text key in the text table and both locales hold it', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const english = items.find((item) => item.file === 'texts/en.json')!
    const files = ['texts/en.json', 'texts/vi.json'].map((name) => path.join(copy, name))
    const originals = await Promise.all(files.map((file) => readFile(file, 'utf8')))
    const key = `e2e.key${Date.now().toString(36)}`

    try {
      await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${english.id}`)
      const add = page.getByTestId('studio-text-add')
      await add.getByRole('textbox', { name: 'New key', exact: true }).fill(key)
      await add.getByRole('textbox', { name: 'en', exact: true }).fill('E2E text')
      await add.getByRole('textbox', { name: 'vi', exact: true }).fill('Văn bản E2E')
      await add.getByRole('button', { name: 'Add key', exact: true }).click()
      await expect(page.locator(`[data-testid="studio-text-row"][data-key="${key}"]`)).toBeVisible()

      const [en, vi] = await Promise.all(
        files.map(async (file) => JSON.parse(await readFile(file, 'utf8')) as { texts: Record<string, string> }),
      )
      expect(en.texts[key]).toBe('E2E text')
      expect(vi.texts[key]).toBe('Văn bản E2E')
    } finally {
      await Promise.all(files.map((file, index) => writeFile(file, originals[index])))
    }
  })

  test('an author adds a connector operation in the connector editor and the file keeps the others', async ({
    page,
  }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const connector = items.find((item) => item.file === 'connectors/purchasing-system.json')!
    const file = path.join(copy, 'connectors/purchasing-system.json')
    const original = await readFile(file, 'utf8')
    const before = JSON.parse(original) as { operations: { name: string }[] }
    const diagnosticsBefore = await (
      await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
    ).json()
    const name = `ping${Date.now().toString(36)}`

    try {
      await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${connector.id}`)
      await expect(page.getByRole('textbox', { name: 'URL binding', exact: true })).toHaveValue('PurchasingSystemUrl')
      await page.getByRole('button', { name: 'Add operation', exact: true }).click()
      const added = before.operations.length
      await page.getByTestId(`studio-field-/operations/${added}/name`).getByRole('textbox').fill(name)
      await page.getByTestId(`studio-field-/operations/${added}/path`).getByRole('textbox').fill('/ping')
      await page.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(page.getByTestId('studio-saved')).toBeVisible()

      const diagnosticsAfter = await (
        await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
      ).json()
      expect(diagnosticsAfter).toEqual(diagnosticsBefore)
      const after = JSON.parse(await readFile(file, 'utf8')) as {
        operations: Record<string, unknown>[]
        baseUrl: unknown
        auth: unknown
      }
      expect(after.operations).toHaveLength(added + 1)
      expect(after.operations.slice(0, added)).toEqual(before.operations)
      expect(after.operations[added]).toEqual({ name, method: 'POST', path: '/ping' })
      expect(after.baseUrl).toEqual({ binding: 'PurchasingSystemUrl' })
      expect(after.auth).toEqual({ type: 'bearer', token: { binding: 'PurchasingSystemToken' } })
    } finally {
      await writeFile(file, original)
    }
  })

  test('an author edits an endpoint mapping row and the file holds only that expression changed', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const endpoint = items.find((item) => item.file === 'endpoints/submit-purchase-request.json')!
    const file = path.join(copy, 'endpoints/submit-purchase-request.json')
    const original = await readFile(file, 'utf8')
    const before = JSON.parse(original) as { values: Record<string, string> }
    const diagnosticsBefore = await (
      await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
    ).json()

    try {
      await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${endpoint.id}`)
      const row = page.getByTestId('studio-field-/values/title')
      const expression = row.getByRole('textbox')
      // The type line shows only once CodeMirror has replaced the read-only Suspense fallback.
      await expect(row.getByTestId('studio-expression-type')).toBeVisible()
      await expression.fill('trim(title)')
      await page.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(page.getByTestId('studio-saved')).toBeVisible()

      const diagnosticsAfter = await (
        await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/diagnostics`)
      ).json()
      expect(diagnosticsAfter).toEqual(diagnosticsBefore)
      const after = JSON.parse(await readFile(file, 'utf8')) as typeof before
      expect(after.values).toEqual({ ...before.values, title: 'trim(title)' })
      expect(Object.keys(after.values)).toEqual(Object.keys(before.values))
    } finally {
      await writeFile(file, original)
    }
  })

  test('an author gives a new page a title in two locales, writes the texts and saves', async ({ page }) => {
    await useSession(page, 'hoa')
    const pageFile = path.join(copy, 'pages/label-texts-e2e.json')
    const textFiles = ['en', 'vi'].map((locale) => path.join(copy, `texts/${locale}.json`))
    const [originalEn, originalVi] = await Promise.all(textFiles.map((file) => readFile(file, 'utf8')))
    const id = randomUUID()
    await writeFile(
      pageFile,
      JSON.stringify({ id, kind: 'page', name: 'LabelTextsE2e', formatVersion: 1, widgets: [{ type: 'taskInbox' }] }),
    )

    try {
      await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)
      await openFormTab(page)
      await expect(page.getByRole('textbox', { name: 'Text key', exact: true })).toHaveValue('pages.labelTextsE2e.title')
      await page.getByRole('textbox', { name: 'en', exact: true }).fill('Label texts')
      await page.getByRole('textbox', { name: 'vi', exact: true }).fill('Văn bản nhãn')
      await page.getByRole('button', { name: 'Write texts', exact: true }).click()
      await expect(page.getByTestId('studio-label-written')).toBeVisible()
      await page.getByRole('button', { name: 'Save', exact: true }).click()
      await expect(page.getByTestId('studio-saved')).toBeVisible()

      const [en, vi] = await Promise.all(
        textFiles.map(
          async (file) => (JSON.parse(await readFile(file, 'utf8')) as { texts: Record<string, string> }).texts,
        ),
      )
      expect(en['pages.labelTextsE2e.title']).toBe('Label texts')
      expect(vi['pages.labelTextsE2e.title']).toBe('Văn bản nhãn')
      const saved = JSON.parse(await readFile(pageFile, 'utf8')) as { title: { textKey: string } }
      expect(saved.title.textKey).toBe('pages.labelTextsE2e.title')
    } finally {
      await rm(pageFile, { force: true })
      await Promise.all([writeFile(textFiles[0], originalEn), writeFile(textFiles[1], originalVi)])
    }
  })

  test('an author selects the steps of a process on its graph', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const process = items.find((item) => item.file === 'processes/purchase-request-approval.json')!
    const file = path.join(copy, 'processes/purchase-request-approval.json')
    const { steps } = JSON.parse(await readFile(file, 'utf8')) as { steps: ProcessStep[] }
    const expected = drawnEdgePaths(steps)

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${process.id}`)
    const graph = page.getByTestId('studio-process-graph')
    await expect(graph.locator('[data-testid^="studio-graph-node-"]')).toHaveCount(steps.length)
    for (const [index, step] of steps.entries()) {
      await expect(page.getByTestId(`studio-graph-node-${index}`)).toHaveAccessibleName(`${step.name} · ${step.type}`)
    }
    const edges = graph.locator('[data-testid^="rf__edge-"]')
    await expect(edges).toHaveCount(expected.length)
    const ids = await edges.evaluateAll((elements) => elements.map((element) => element.getAttribute('data-testid')))
    expect(ids.sort()).toEqual(expected.map((transitionPath) => `rf__edge-${transitionPath}`).sort())

    const design = page.getByTestId('studio-process-design')
    await page.getByRole('button', { name: 'financeApproval · task', exact: true }).click()
    await expect(design.getByTestId('studio-step-3')).toBeVisible()
    await expect(page.getByTestId('studio-graph-node-3')).toHaveAttribute('aria-current', 'true')

    await page.getByTestId('rf__edge-/steps/3/outcomes/0').click()
    await expect(design.getByTestId('studio-step-3')).toBeVisible()
    await expect(page.getByTestId('studio-graph-node-3')).toHaveAttribute('aria-current', 'true')
  })

  test('someone without the author role finds no Studio in the app switcher and sees a forbidden page', async ({ page }) => {
    await useSession(page, 'anna')

    await page.goto(`${server.url}/`)
    const banner = page.getByRole('banner')
    await expect(banner.getByRole('button', { name: 'Anna Employee', exact: true })).toBeVisible()
    await banner.getByRole('button', { name: 'Switch to', exact: true }).click()
    await expect(page.getByRole('menu', { name: 'Switch to', exact: true })).toBeVisible()
    await expect(page.getByRole('menuitem', { name: /studio/i })).toHaveCount(0)
    await page.goto(`${server.url}/studio`)

    await expect(page.getByTestId('studio-forbidden')).toBeVisible()
  })

  test('an author without the publisher role sees Validate and no Publish', async ({ page }) => {
    // A second server whose publisher role nobody holds, so hoa is an author but not a publisher.
    const noPublisher = await startServer({
      ActivateOnStartup__0: e2eApp,
      Studio__PublisherRole: 'release-manager',
      Studio__Folders__0: copy,
    })
    try {
      await useSession(page, 'hoa')

      await page.goto(`${noPublisher.url}/studio?app=PurchaseRequests`)

      await expect(page.getByRole('button', { name: 'Validate', exact: true })).toBeVisible()
      await expect(page.getByRole('button', { name: /publish/i })).toHaveCount(0)
    } finally {
      await noPublisher.stop()
    }
  })
})
