import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
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
  onTextsWritten: vi.fn(),
}

const sections = [
  { name: 'request', fields: [] },
  { title: { textKey: 'review.unnamed' }, fields: [] },
  { name: 'decision', fields: [] },
]

const steps = [
  { title: { textKey: 'review.stepOne' }, sections: ['request'] },
  { title: { textKey: 'review.stepTwo' }, sections: ['decision'] },
]

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderSteps(value: unknown, onChange = vi.fn(), available: unknown = sections) {
  render(
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
    </ThemeModeProvider>,
  )
  return onChange
}

/** The names of the options of the open dropdown. */
function optionsOf(): string[] {
  const open = document.querySelector('.ant-select-dropdown:not(.ant-select-dropdown-hidden)')!
  return [...open.querySelectorAll('.ant-select-item-option')].map((option) => option.textContent ?? '')
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('FormSteps in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with a card per step', () => {
    renderSteps(steps)

    const root = screen.getByTestId('studio-field-/steps')
    expect(root.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(root).toBeVisible()
    for (const index of [0, 1]) {
      const card = screen.getByTestId(`studio-field-/steps/${index}`)
      expect(card).toBeVisible()
      expect(within(card).getByRole('button', { name: 'Move up' })).toBeVisible()
      expect(within(card).getByRole('button', { name: 'Move down' })).toBeVisible()
      expect(within(card).getByRole('button', { name: 'Remove' })).toBeVisible()
    }
    expect(screen.getByRole('button', { name: 'Add step' })).toBeVisible()
  })
})

describe('FormSteps', () => {
  it('adds the first step by creating the steps', async () => {
    const onChange = renderSteps(undefined)

    await userEvent.click(screen.getByRole('button', { name: 'Add step' }))

    expect(onChange).toHaveBeenLastCalledWith('/steps', [{ title: { textKey: '' }, sections: [] }])
  })

  it('adds a step after the last one', async () => {
    const onChange = renderSteps(steps)

    await userEvent.click(screen.getByRole('button', { name: 'Add step' }))

    expect(onChange).toHaveBeenLastCalledWith('/steps', [...steps, { title: { textKey: '' }, sections: [] }])
  })

  it('moves a step down and up by swapping it with its neighbour', async () => {
    const onChange = renderSteps(steps)

    await userEvent.click(
      within(screen.getByTestId('studio-field-/steps/0')).getByRole('button', { name: 'Move down' }),
    )
    expect(onChange).toHaveBeenLastCalledWith('/steps', [steps[1], steps[0]])

    await userEvent.click(within(screen.getByTestId('studio-field-/steps/1')).getByRole('button', { name: 'Move up' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps', [steps[1], steps[0]])
  })

  it('cannot move the first step up or the last step down', () => {
    renderSteps(steps)

    expect(within(screen.getByTestId('studio-field-/steps/0')).getByRole('button', { name: 'Move up' })).toBeDisabled()
    expect(
      within(screen.getByTestId('studio-field-/steps/1')).getByRole('button', { name: 'Move down' }),
    ).toBeDisabled()
  })

  it('lists only the named sections and gives the step the one picked', async () => {
    const onChange = renderSteps(steps)
    const card = screen.getByTestId('studio-field-/steps/0')

    await userEvent.click(within(card).getByRole('combobox', { name: 'Sections' }))
    await waitFor(() => expect(optionsOf()).toEqual(['request', 'decision']))
    const dropdown = document.querySelector<HTMLElement>('.ant-select-dropdown:not(.ant-select-dropdown-hidden)')!
    await userEvent.click(within(dropdown).getByTitle('decision'))

    expect(onChange).toHaveBeenLastCalledWith('/steps/0/sections', ['request', 'decision'])
  })

  it('removes one step by its place and removes the steps when the last one goes', async () => {
    const onChange = renderSteps(steps)
    await userEvent.click(within(screen.getByTestId('studio-field-/steps/1')).getByRole('button', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/1', undefined)
  })

  it('removes /steps when the only step is removed', async () => {
    const onChange = renderSteps([steps[0]])

    await userEvent.click(within(screen.getByTestId('studio-field-/steps/0')).getByRole('button', { name: 'Remove' }))

    expect(onChange).toHaveBeenLastCalledWith('/steps', undefined)
  })

  it('shows the title texts of a step', () => {
    renderSteps(steps)

    const card = screen.getByTestId('studio-field-/steps/1')
    expect(within(card).getByRole('textbox', { name: 'en' })).toHaveValue('Decision')
  })

  it('leaves steps that are not a list to the JSON view', () => {
    renderSteps({ title: 'x' })

    expect(screen.getByTestId('studio-json-only')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Add step' })).toBeNull()
  })
})
