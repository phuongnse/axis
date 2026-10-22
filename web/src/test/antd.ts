import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect } from 'vitest'

// The one place that knows Ant Design's class names. A change in Ant Design's markup should change only this file.

const openDropdownSelector = '.ant-select-dropdown:not(.ant-select-dropdown-hidden):not(.ant-slide-up-leave)'

/** The first dropdown that is open and not leaving. */
export function shownDropdown(): HTMLElement | null {
  return document.querySelector<HTMLElement>(openDropdownSelector)
}

/** Waits for an open dropdown, with an option titled `containing` when given. */
export function openDropdown(containing?: string): Promise<HTMLElement> {
  return waitFor(() => {
    const dropdown = [...document.querySelectorAll<HTMLElement>(openDropdownSelector)].find(
      (candidate) => containing === undefined || within(candidate).queryByTitle(containing) !== null,
    )
    expect(dropdown).toBeDefined()
    return dropdown!
  })
}

/** The texts of the options in `scope`, the whole document by default. */
export function optionTexts(scope: ParentNode = document): string[] {
  return [...scope.querySelectorAll('.ant-select-item-option')].map((option) => option.textContent ?? '')
}

/**
 * Opens the picker in `field` when given, then clicks the option titled `title`. A title also marks
 * a selected value, so only an option of an open dropdown counts. The newest such option wins.
 */
export async function chooseOption(title: string, field?: HTMLElement): Promise<void> {
  if (field) await userEvent.click(within(field).getByRole('combobox'))
  const option = await waitFor(() => {
    const found = [...document.querySelectorAll<HTMLElement>('.ant-select-item-option')].filter(
      (element) => element.title === title && element.closest('.ant-select-dropdown')?.matches(openDropdownSelector),
    )
    expect(found.length).toBeGreaterThan(0)
    return found[found.length - 1]
  })
  await userEvent.click(option)
}

/** The clear button of a select, or null when it shows none. */
export function clearButton(scope: ParentNode = document): HTMLElement | null {
  return scope.querySelector<HTMLElement>('.ant-select-clear')
}

/** Hovers the select in `field` and clicks its clear button. */
export async function clearSelect(field: HTMLElement): Promise<void> {
  await userEvent.hover(within(field).getByRole('combobox'))
  await userEvent.click(clearButton(field)!)
}

/** Removes the tag showing `text` in `field`, or the first tag when no text is given. */
export async function removeTag(field: HTMLElement, text?: string): Promise<void> {
  const tag = text
    ? within(field).getByText(text).closest<HTMLElement>('.ant-select-selection-item')
    : field.querySelector<HTMLElement>('.ant-select-selection-item')
  await userEvent.click(tag!.querySelector('.ant-select-selection-item-remove')!)
}

/** The `.ant-select` element that holds `combobox`. */
export function selectRoot(combobox: HTMLElement): HTMLElement {
  const root = combobox.closest<HTMLElement>('.ant-select')
  if (!root) throw new Error('The combobox is not inside a select')
  return root
}

/** The form item that holds `element`. */
export function formItem(element: HTMLElement): HTMLElement {
  const item = element.closest<HTMLElement>('.ant-form-item')
  if (!item) throw new Error('The element is not inside a form item')
  return item
}

export function hasFieldError(element: HTMLElement): boolean {
  return formItem(element).classList.contains('ant-form-item-has-error')
}

export function isRequiredLabel(label: HTMLElement): boolean {
  return label.classList.contains('ant-form-item-required')
}

export function isSortableHeader(header: HTMLElement): boolean {
  return header.classList.contains('ant-table-column-has-sorters')
}

/** Whether `element` sits in a spin area that is still blurred while it loads. */
export function isBlurred(element: HTMLElement): boolean {
  return element.closest('.ant-spin-blur, .ant-spin-spinning') !== null
}

/** The label of a segmented control's item with `text`. */
export function segmentLabel(text: string): HTMLElement {
  return screen.getByText(text, { selector: '.ant-segmented-item-label' })
}

/** The description of an empty state that shows `text`. */
export function emptyDescription(text: string): HTMLElement {
  return screen.getByText(text, { selector: '.ant-empty-description' })
}
