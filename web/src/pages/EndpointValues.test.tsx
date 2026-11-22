import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { optionTexts, tabTo } from '../test/antd'
import { withAntdStyles } from '../test/antdStyles'
import { designTokensFor } from '../platform/designTokens'
import { ExpressionCheckContext } from './expressionCheckContext'
import { EndpointValues } from './EndpointValues'

const fields = ['title', 'department', 'status', 'currency']
const values = { title: 'title', department: 'department', status: "'draft'" }

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderValues(data: unknown = values, onChange = vi.fn(), known: string[] | null = fields, readOnly = false) {
  render(
    <ConfigProvider componentDisabled={readOnly}>
      <ThemeModeProvider>
        <ThemeRoot>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <ExpressionCheckContext.Provider
              value={{ application: 'Shop', resourceId: 'bc241668', kind: 'endpoint', draft: () => ({}), names: null }}
            >
              <EndpointValues fields={known ?? undefined} values={data} onChange={onChange} />
            </ExpressionCheckContext.Provider>
          </TextProvider>
        </ThemeRoot>
      </ThemeModeProvider>
    </ConfigProvider>,
  )
  return onChange
}

/** Add mapping is a picker while the entity's fields are known, and a plain button while they are not. */
const addMapping = [
  { when: 'known', known: fields, role: 'combobox' as const },
  { when: 'unknown', known: null, role: 'button' as const },
]

beforeEach(() =>
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) =>
      String(input).endsWith('/expressions/scope')
        ? new Response(null, { status: 404 })
        : new Response(JSON.stringify({ type: 'text', diagnostics: [] }), { status: 200 }),
    ),
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
    // The first check waits for the lazy editor chunk, which can take long on a cold, busy machine.
    await screen.findAllByTestId('studio-expression-type', undefined, { timeout: 5000 })

    const root = screen.getByTestId('studio-field-/values')
    expect(root.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(root).toBeVisible()
    for (const key of Object.keys(values)) {
      const row = screen.getByTestId(`studio-field-/values/${key}`)
      expect(row).toBeVisible()
      expect(within(row).getByRole('combobox', { name: 'Field' })).toBeVisible()
      expect(within(row).getByRole('textbox', { name: 'Expression' })).toBeVisible()
      expect(within(row).getByRole('button', { name: 'Remove' })).toBeVisible()
      expect(within(row).queryByRole('button', { name: 'Actions' })).toBeNull()
    }
    expect(screen.getByRole('combobox', { name: 'Add mapping' })).toBeVisible()
  })
})

describe('EndpointValues', () => {
  const part = { name: platformEnglish['shell.studio.field.endpoint.values'] }

  it('heads the rows with the title and the help that describes them', () => {
    renderValues()

    const region = screen.getByRole('region', part)
    expect(region).toContainElement(screen.getByTestId('studio-field-/values'))
    expect(within(region).getByText(platformEnglish['shell.studio.field.endpoint.values.help'])).toBeVisible()
  })

  it('says there are no values when the endpoint has none', () => {
    renderValues({})

    expect(screen.getByRole('region', part)).toContainElement(
      screen.getByText(platformEnglish['shell.studio.endpoint.noValues']),
    )
    expect(screen.getByRole('combobox', { name: 'Add mapping' })).toBeVisible()
  })

  it.each(addMapping)(
    'puts Add mapping on the right of the heading and nowhere else in the part while the fields are $when',
    ({ known, role }) => {
      renderValues(values, vi.fn(), known)

      const part = screen.getByTestId('studio-section-values')
      const body = screen.getByTestId('studio-field-/values')
      expect(within(part).getAllByRole(role, { name: 'Add mapping' })).toHaveLength(1)
      expect(within(body).queryByRole(role, { name: 'Add mapping' })).toBeNull()
      expect(
        within(part).getByRole(role, { name: 'Add mapping' }).compareDocumentPosition(body) &
          Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy()
    },
  )

  it.each(addMapping)(
    'shows no Add mapping and no row menu when the editor is read-only and the fields are $when',
    ({ known }) => {
      renderValues(values, vi.fn(), known, true)

      expect(screen.queryByRole('combobox', { name: 'Add mapping' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Add mapping' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Actions' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Remove' })).toBeNull()
    },
  )

  it('removes a mapping by its key', async () => {
    const onChange = renderValues()

    await userEvent.click(
      within(screen.getByTestId('studio-field-/values/department')).getByRole('button', { name: 'Remove' }),
    )
    expect(onChange).toHaveBeenLastCalledWith('/values/department', undefined)
  })

  describe('with antd styles', () => {
    withAntdStyles()

    beforeEach(() => localStorage.setItem('axis.themeMode', 'dark'))

    it('draws Remove as the trash icon in dangerText and names it on keyboard focus in dark mode', async () => {
      renderValues()
      // The first check waits for the lazy editor chunk, as the theme test does.
      await screen.findAllByTestId('studio-expression-type', undefined, { timeout: 5000 })

      const row = screen.getByTestId('studio-field-/values/department')
      const remove = within(row).getByRole('button', { name: 'Remove' })
      expect(remove.querySelector('.anticon-delete')).not.toBeNull()
      expect(remove).toHaveStyle({ color: designTokensFor('dark').colour.dangerText })
      expect(within(row).queryByRole('button', { name: 'Actions' })).toBeNull()
      await tabTo(remove)

      // Other tooltips the tab passed may stay open, so the one of Remove is found among them.
      await waitFor(() => expect(screen.getAllByRole('tooltip').map((tip) => tip.textContent)).toContain('Remove'))
    })
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

    expect(screen.getByRole('region', part)).toContainElement(screen.getByTestId('studio-json-only'))
    expect(screen.getByTestId('studio-json-only')).toBeVisible()
  })
})
