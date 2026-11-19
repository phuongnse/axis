import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { shownMenu } from './antd'

/** How long a field waits for the designer to load, as `slow` in the Studio tests. */
const wait = { timeout: 5000 }

/** Finds the row of field `index` in the Fields table, once it is drawn. */
function entityFieldRow(index: number): Promise<HTMLElement> {
  return screen.findByTestId(`studio-field-/fields/${index}`, {}, wait)
}

/** The link of the first cell of field `index`'s row: the field's name, which opens the field drawer. */
function entityFieldLink(row: HTMLElement): HTMLElement {
  return within(within(row).getAllByRole('cell')[0]).getAllByRole('button')[0]
}

/** Makes field `index` ready to edit: clicks its name in the Fields table and returns the field drawer's dialog. */
export async function openEntityField(index: number): Promise<HTMLElement> {
  await userEvent.click(entityFieldLink(await entityFieldRow(index)))
  return (await screen.findByTestId('studio-field-drawer', {}, wait)).closest('[role="dialog"]') as HTMLElement
}

/** The name of each field in the Fields table, in order, read from the link in each row's first cell. */
export function entityFieldNames(): string[] {
  return screen.getAllByTestId(/^studio-field-\/fields\/\d+$/).map((row) => entityFieldLink(row).textContent ?? '')
}

/** Opens the "…" menu of field `index` and returns its entry `action`, unclicked. */
export async function entityFieldMenuItem(
  index: number,
  action: 'Move up' | 'Move down' | 'Remove',
): Promise<HTMLElement> {
  const row = await entityFieldRow(index)
  await userEvent.click(within(row).getByRole('button', { name: 'Actions' }))
  return within(await shownMenu('Actions')).getByRole('menuitem', { name: action })
}

/** Clicks `action` in the "…" menu of the row of field `index`. */
export async function entityFieldAction(index: number, action: 'Move up' | 'Move down' | 'Remove'): Promise<void> {
  await userEvent.click(await entityFieldMenuItem(index, action))
}

/** Clicks Add field. */
export async function addEntityField(): Promise<void> {
  await userEvent.click(await screen.findByRole('button', { name: 'Add field' }, wait))
}
