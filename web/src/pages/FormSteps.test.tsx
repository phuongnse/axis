import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { chooseOption, optionTexts, shownDropdown, shownMenu } from '../test/antd'
import { FormSteps } from './FormSteps'
import { LabelTextsContext } from './labelTextsContext'

const names = {
  locales: ['en', 'vi'],
  texts: [
    { key: 'review.stepOne', texts: { en: 'Request', vi: 'Yêu cầu' }, usedBy: 1 },
    { key: 'review.stepTwo', texts: { en: 'Decision', vi: 'Quyết định' }, usedBy: 1 },
  ],
} as unknown as StudioNames

const labelContext = {
  application: 'Shop',
  file: 'forms/review.json',
  name: 'Review',
  saved: {},
  texts: {},
  onTextsChange: vi.fn(),
}

const sections = [
  { name: 'request', fields: [] },
  { title: { textKey: 'review.unnamed' }, fields: [] },
  { name: 'decision', fields: [] },
]

const steps = [
  { name: 'step1', title: { textKey: 'review.stepOne' }, sections: ['request'] },
  { name: 'step2', title: { textKey: 'review.stepTwo' }, sections: ['decision'] },
]

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderSteps(value: unknown, onChange = vi.fn(), available: unknown = sections, readOnly = false) {
  render(
    <ConfigProvider componentDisabled={readOnly}>
      <ThemeModeProvider>
        <ThemeRoot>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <StudioNamesContext.Provider value={names}>
              <LabelTextsContext.Provider value={labelContext}>
                <FormSteps steps={value} sections={available} onChange={onChange} />
              </LabelTextsContext.Provider>
            </StudioNamesContext.Provider>
          </TextProvider>
        </ThemeRoot>
      </ThemeModeProvider>
    </ConfigProvider>,
  )
  return onChange
}

/** Opens the "…" menu of the step at `index` and returns the menu. */
async function openStepMenu(index: number) {
  await userEvent.click(
    within(screen.getByTestId(`studio-field-/steps/${index}`)).getByRole('button', { name: 'Actions' }),
  )
  return within(await shownMenu('Actions'))
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('FormSteps in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with a row per step and a "…" menu on each', () => {
    renderSteps(steps)

    const root = screen.getByTestId('studio-field-/steps')
    expect(root.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(root).toBeVisible()
    for (const index of [0, 1]) {
      const row = screen.getByTestId(`studio-field-/steps/${index}`)
      expect(row).toBeVisible()
      expect(within(row).getByRole('button', { name: 'Actions' })).toBeVisible()
    }
    expect(screen.getByRole('button', { name: 'Add step' })).toBeVisible()
  })
})

describe('FormSteps', () => {
  it('puts Add step on the right of the heading and nowhere else in the part', () => {
    renderSteps(steps)

    const part = screen.getByTestId('studio-section-steps')
    const body = screen.getByTestId('studio-field-/steps')
    expect(within(part).getAllByRole('button', { name: 'Add step' })).toHaveLength(1)
    expect(within(body).queryByRole('button', { name: 'Add step' })).toBeNull()
    expect(
      within(part).getByRole('button', { name: 'Add step' }).compareDocumentPosition(body) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
  })

  it('draws Add step as a text button in the title row', () => {
    renderSteps(steps)

    const part = screen.getByTestId('studio-section-steps')
    expect(within(part).getByRole('button', { name: 'Add step' })).toHaveClass('ant-btn-variant-text')
  })

  it('keeps no step button of its own in a row: each step moves and deletes from its menu', () => {
    renderSteps(steps)

    for (const index of [0, 1]) {
      const row = within(screen.getByTestId(`studio-field-/steps/${index}`))
      expect(row.queryByRole('button', { name: 'Move up' })).toBeNull()
      expect(row.queryByRole('button', { name: 'Move down' })).toBeNull()
      expect(row.queryByRole('button', { name: 'Delete step' })).toBeNull()
    }
  })

  it('lists Move up, Move down and a destructive Delete step in each step menu, in that order', async () => {
    renderSteps(steps)

    const menu = await openStepMenu(0)
    expect(menu.getAllByRole('menuitem').map((entry) => entry.textContent)).toEqual([
      'Move up',
      'Move down',
      'Delete step',
    ])
    expect(menu.getByRole('menuitem', { name: 'Delete step' })).toHaveClass('ant-dropdown-menu-item-danger')
  })

  it('hides Add step and every step menu when the editor is read-only', () => {
    renderSteps(steps, vi.fn(), sections, true)

    expect(screen.getByTestId('studio-field-/steps/0')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Add step' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Actions' })).toBeNull()
  })

  it('adds the first step by creating the steps', async () => {
    const onChange = renderSteps(undefined)

    await userEvent.click(screen.getByRole('button', { name: 'Add step' }))

    expect(onChange).toHaveBeenLastCalledWith('/steps', [{ name: 'step1', title: { textKey: '' }, sections: [] }])
  })

  it('adds a step after the last one', async () => {
    const onChange = renderSteps(steps)

    await userEvent.click(screen.getByRole('button', { name: 'Add step' }))

    expect(onChange).toHaveBeenLastCalledWith('/steps', [
      ...steps,
      { name: 'step3', title: { textKey: '' }, sections: [] },
    ])
  })

  it('names a new step with the first free name, ignoring letter case', async () => {
    const existing = [{ name: 'Step1', title: { textKey: 'review.stepOne' }, sections: ['request'] }]
    const onChange = renderSteps(existing)

    await userEvent.click(screen.getByRole('button', { name: 'Add step' }))

    expect(onChange).toHaveBeenLastCalledWith('/steps', [
      ...existing,
      { name: 'step2', title: { textKey: '' }, sections: [] },
    ])
  })

  it('moves a step down and up by swapping it with its neighbour', async () => {
    const onChange = renderSteps(steps)

    await userEvent.click((await openStepMenu(0)).getByRole('menuitem', { name: 'Move down' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps', [steps[1], steps[0]])

    await userEvent.click((await openStepMenu(1)).getByRole('menuitem', { name: 'Move up' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps', [steps[1], steps[0]])
  })

  it('cannot move the first step up or the last step down', async () => {
    renderSteps(steps)

    expect(await (await openStepMenu(0)).findByRole('menuitem', { name: 'Move up' })).toHaveAttribute(
      'aria-disabled',
      'true',
    )
    await userEvent.click(document.body)
    expect(await (await openStepMenu(1)).findByRole('menuitem', { name: 'Move down' })).toHaveAttribute(
      'aria-disabled',
      'true',
    )
  })

  it('lists only the named sections and gives the step the one picked', async () => {
    const onChange = renderSteps(steps)
    const row = screen.getByTestId('studio-field-/steps/0')

    const sections = within(row).getByRole('combobox', { name: 'Sections' })
    expect(sections).toHaveAccessibleDescription(platformEnglish['shell.studio.field.form.steps.sections.help'])
    await userEvent.click(sections)
    await waitFor(() => expect(optionTexts(shownDropdown()!)).toEqual(['request', 'decision']))
    await chooseOption('decision')

    expect(onChange).toHaveBeenLastCalledWith('/steps/0/sections', ['request', 'decision'])
  })

  it('removes one step by its place and removes the steps when the last one goes', async () => {
    const onChange = renderSteps(steps)

    await userEvent.click((await openStepMenu(1)).getByRole('menuitem', { name: 'Delete step' }))

    expect(onChange).toHaveBeenLastCalledWith('/steps/1', undefined)
  })

  it('removes /steps when the only step is removed', async () => {
    const onChange = renderSteps([steps[0]])

    await userEvent.click((await openStepMenu(0)).getByRole('menuitem', { name: 'Delete step' }))

    expect(onChange).toHaveBeenLastCalledWith('/steps', undefined)
  })

  it('shows the title texts of a step', () => {
    renderSteps(steps)

    const row = screen.getByTestId('studio-field-/steps/1')
    expect(within(row).getByRole('group', { name: 'Title' })).toHaveAccessibleDescription(
      platformEnglish['shell.studio.field.form.steps.title.help'],
    )
    expect(within(row).getByRole('textbox', { name: 'English' })).toHaveValue('Decision')
  })

  it('leaves steps that are not a list to the JSON view', () => {
    renderSteps({ title: 'x' })

    expect(screen.getByTestId('studio-json-only')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Add step' })).toBeNull()
  })
})
