import { randomUUID } from 'node:crypto'
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { expect, test } from '../test'
import { startServer } from '../activate'
import { e2eApp } from '../server-settings'
import { pickOption } from '../select'
import { useSession } from '../sessions'
import {
  answerLeave,
  chooseValueSource,
  closeEntityField,
  openEntityFieldByAddress,
  openKind,
  openTab,
  saveResource,
} from '../studio-resources'

// The journey starts its own server and walks through the whole path of a field's value source.
test.setTimeout(180_000)

// A throwaway application with the entity Order, whose `reference` text field is the second field, and a sequence
// OrderNumber. The journey does not publish, so the committed samples are not changed.
test('an author opens a field by its address, changes its value source, stays when asked before leaving and finds the entity on Used by', async ({
  page,
}, testInfo) => {
  const name = `FieldJourney${Date.now().toString(36)}`
  const folder = testInfo.outputPath(name)
  const orderId = randomUUID()
  const sequenceId = randomUUID()
  await rm(folder, { recursive: true, force: true })
  await mkdir(path.join(folder, 'entities'), { recursive: true })
  await mkdir(path.join(folder, 'sequences'), { recursive: true })
  await writeFile(
    path.join(folder, 'application.json'),
    JSON.stringify({ id: randomUUID(), kind: 'application', name, formatVersion: 1 }),
  )
  await writeFile(
    path.join(folder, 'entities', 'order.json'),
    JSON.stringify({
      id: orderId,
      kind: 'entity',
      name: 'Order',
      formatVersion: 1,
      fields: [
        { name: 'title', type: 'text' },
        { name: 'reference', type: 'text', maxLength: 20, default: 'NEW' },
      ],
    }),
  )
  await writeFile(
    path.join(folder, 'sequences', 'order-number.json'),
    JSON.stringify({ id: sequenceId, kind: 'sequence', name: 'OrderNumber', formatVersion: 1, format: 'ORD-{yyyy}-{n:5}' }),
  )

  const server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: folder })
  try {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=${name}`)
    await openKind(page, 'entity')
    await page.getByTestId(`studio-resource-${orderId}`).click()
    await expect(page.getByTestId('studio-entity-designer')).toBeVisible()

    // The address opens the field's drawer, which holds the field's name.
    const drawer = await openEntityFieldByAddress(page, 'reference')
    await expect(page).toHaveURL(/[?&]field=reference(&|$)/)
    await expect(drawer.getByTestId('studio-field-/fields/1/name').getByRole('textbox')).toHaveValue('reference')

    // Closing the drawer with a draft does not ask, but a move to another kind does, and Stay keeps the draft.
    await chooseValueSource(page, 'Sequence')
    await pickOption(page.getByTestId('studio-field-/fields/1/sequence').getByRole('combobox'), 'OrderNumber')
    await closeEntityField(page)
    await expect(page).not.toHaveURL(/[?&]field=/)
    await answerLeave(page, 'sequence', 'Stay')
    await expect(page).toHaveURL(new RegExp(`[?&]resource=${orderId}(&|$)`))

    // The saved file names the sequence, and the switch dropped the default.
    await saveResource(page)
    const saved = JSON.parse(await readFile(path.join(folder, 'entities', 'order.json'), 'utf8')) as {
      fields: unknown[]
    }
    expect(saved.fields[1]).toEqual({ name: 'reference', type: 'text', maxLength: 20, sequence: 'OrderNumber' })

    // The sequence's Used by tab lists the entity that now refers to it.
    await openKind(page, 'sequence')
    await page.getByTestId(`studio-resource-${sequenceId}`).click()
    await openTab(page, 'Used by')
    await expect(page.getByTestId('studio-used-by').getByRole('link', { name: 'Order', exact: true })).toBeVisible()
  } finally {
    await server.stop()
  }
})
