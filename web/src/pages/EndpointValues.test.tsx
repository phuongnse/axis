import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { optionTexts } from '../test/antd'
import { ExpressionCheckContext } from './expressionCheckContext'
import { EndpointValues } from './EndpointValues'

const fields = ['title', 'department', 'status', 'currency']
const values = { title: 'title', department: 'department', status: "'draft'" }

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderValues(data: unknown = values, onChange = vi.fn(), known: string[] | null = fields) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <ExpressionCheckContext.Provider
            value={{ application: 'Shop', resourceId: 'bc241668', kind: 'endpoint', draft: {}, names: null }}
          >
            <EndpointValues fields={known ?? undefined} values={data} onChange={onChange} />
          </ExpressionCheckContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

beforeEach(() =>
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify({ type: 'text', diagnostics: [] }), { status: 200 })),
  ),
)

afterEach(() => {
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe.each(['light', 'dark'])('EndpointValues in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with a row per mapping', async () => {
    renderValues()
    await screen.findAllByTestId('studio-expression-type')

    const root = screen.getByTestId('studio-field-/values')
    expect(root.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(root).toBeVisible()
    for (const key of Object.keys(values)) {
      const row = screen.getByTestId(`studio-field-/values/${key}`)
      expect(row).toBeVisible()
      expect(within(row).getByRole('combobox', { name: 'Field' })).toBeVisible()
      expect(within(row).getByRole('textbox', { name: 'Expression' })).toBeVisible()
      expect(within(row).getByRole('button', { name: 'Remove' })).toBeVisible()
    }
    expect(screen.getByRole('combobox', { name: 'Add mapping' })).toBeVisible()
  })
})

describe('EndpointValues', () => {
  it('removes a mapping by its key', async () => {
    const onChange = renderValues()

    await userEvent.click(within(screen.getByTestId('studio-field-/values/department')).getByRole('button'))
    expect(onChange).toHaveBeenLastCalledWith('/values/department', undefined)
  })

  it('renames a field in place, keeping the order of the others', async () => {
    const onChange = renderValues()

    const row = screen.getByTestId('studio-field-/values/department')
    await userEvent.click(within(row).getByRole('combobox', { name: 'Field' }))
    await userEvent.click(await screen.findByTitle('currency'))

    expect(onChange).toHaveBeenLastCalledWith('/values', {
      title: 'title',
      currency: 'department',
      status: "'draft'",
    })
    expect(Object.keys(onChange.mock.calls[0][1] as object)).toEqual(['title', 'currency', 'status'])
  })

  it('adds a mapping for an unmapped field with an empty expression', async () => {
    const onChange = renderValues()

    await userEvent.click(screen.getByRole('combobox', { name: 'Add mapping' }))
    const options = optionTexts()
    expect(options).toEqual(['currency'])
    await userEvent.click(await screen.findByTitle('currency'))

    expect(onChange).toHaveBeenLastCalledWith('/values/currency', '')
  })

  it('escapes a key in the pointer of its row', () => {
    renderValues({ 'a/b': 'x' }, vi.fn(), null)

    expect(screen.getByTestId('studio-field-/values/a~1b')).toBeVisible()
  })

  it('types the field while the entity fields are unknown', async () => {
    const onChange = renderValues({ title: 'title' }, vi.fn(), null)

    await userEvent.type(screen.getByRole('textbox', { name: 'Field' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/values', { titleX: 'title' })
  })

  it('points to the JSON view for a value that is not an object', () => {
    renderValues(['x'])

    expect(screen.getByTestId('studio-json-only')).toBeVisible()
  })
})
