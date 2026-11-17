import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { chooseOption } from './antd'

/** The texts of a table's Filter button and its builder, as the English catalogue has them. */
export const filterTexts = {
  'shell.table.filter': 'Filter',
  'shell.table.filterApply': 'Apply',
  'shell.table.filterAll': 'All',
  'shell.table.filterAny': 'Any',
  'shell.table.filterAddCondition': 'Add condition',
  'shell.table.filterAddGroup': 'Add group',
  'shell.table.filterRemove': 'Remove',
  'shell.table.filterColumn': 'Column',
  'shell.table.filterOperator': 'Operator',
  'shell.table.filterValue': 'Value',
  'shell.table.filterClearAll': 'Clear all',
  'shell.table.filterGroupAll': 'All of {count} conditions',
  'shell.table.filterGroupAny': 'Any of {count} conditions',
  'shell.table.filterOp.eq': 'is',
  'shell.table.filterOp.contains': 'contains',
  'shell.table.filterOp.gt': 'is more than',
  'shell.table.filterOp.empty': 'is empty',
}

/** The filter builder's dialog. */
export const dialog = () => screen.getByRole('dialog')

export const clickInDialog = (name: string, index = 0) =>
  userEvent.click(within(dialog()).getAllByRole('button', { name })[index])

/** Picks `title` in the `index`th picker named `name` of the filter dialog. */
export async function pick(name: string, index: number, title: string) {
  await userEvent.click(within(dialog()).getAllByRole('combobox', { name })[index])
  await chooseOption(title)
}
