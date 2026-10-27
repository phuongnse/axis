import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '../test'
import { startServer } from '../activate'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The process is copied to a folder of its own, so no other spec's writes reach it.
test.describe('Studio redraws a large process after a panel edit', () => {
  const file = 'processes/purchase-request-approval.json'
  const folder = '/api/studio/apps/PurchaseRequests'
  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-process-redraw-'))
    await cp(purchaseRequests, copy, { recursive: true })
    // The approval process grows to 40 steps: 30 chained updateRecord steps follow markRejected, before done.
    const processFile = path.join(copy, file)
    const process = JSON.parse(await readFile(processFile, 'utf8')) as { steps: Record<string, unknown>[] }
    const steps = process.steps
    const markRejected = steps.find((step) => step.name === 'markRejected')!
    const done = steps.findIndex((step) => step.name === 'done')
    const fillers = Array.from({ length: 30 }, (_, index) => ({
      name: `filler${index + 1}`,
      type: 'operation',
      operation: 'updateRecord',
      set: { status: "'rejected'" },
      next: index < 29 ? `filler${index + 2}` : 'done',
    }))
    markRejected.next = 'filler1'
    steps.splice(done, 0, ...fillers)
    expect(steps.length).toBe(40)
    await writeFile(processFile, JSON.stringify(process, null, 2))
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  test('a panel edit on a 40-step process shows on the canvas within 2 seconds', async ({ page }) => {
    await useSession(page, 'hoa')
    const list = await page.request.get(`${server.url}${folder}/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const id = items.find((item) => item.file === file)!.id

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`)
    await expect(page.locator('[data-testid^="studio-graph-node-"]')).toHaveCount(40)
    // Node 23 is filler15, a step in the middle of the chain.
    const index = 23
    await page.getByTestId(`studio-graph-node-${index}`).click()
    const name = page
      .getByTestId('studio-process-design')
      .getByRole('textbox', { name: 'Step name', exact: true })
    await expect(name).toHaveValue('filler15')

    const node = page.getByTestId(`studio-graph-node-${index}`)
    const started = Date.now()
    await name.fill('renamedFiller')
    // The 2 s bound comes from docs/reference/process-designer.md.
    await expect(node).toContainText('renamedFiller', { timeout: 2_000 })
    expect(Date.now() - started).toBeLessThan(2_000)
  })
})
