import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

/** How long a field waits for the designer to load, as `slow` in the Studio tests. */
const wait = { timeout: 5000 }

/** Makes field `index` ready to edit and returns the element that holds its settings: for now its row of the Fields table. */
export function openEntityField(index: number): Promise<HTMLElement> {
  return screen.findByTestId(`studio-field-/fields/${index}`, {}, wait)
}

/** Clicks `action` on the row of field `index`. */
export async function entityFieldAction(index: number, action: 'Move up' | 'Move down' | 'Remove'): Promise<void> {
  const row = await openEntityField(index)
  const name = action === 'Remove' ? 'Remove field' : action
  await userEvent.click(within(row).getByRole('button', { name }))
}

/** Clicks Add field. */
export async function addEntityField(): Promise<void> {
  await userEvent.click(await screen.findByRole('button', { name: 'Add field' }, wait))
}
