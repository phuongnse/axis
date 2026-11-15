import { defaultHighlightStyle } from '@codemirror/language'
import { EditorView } from '@codemirror/view'
import { tags } from '@lezer/highlight'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { colourTokens } from '../platform/designTokens'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import ExpressionEditor from './ExpressionEditor'
import type { ScopeOption } from './expressionScope'
import { withAntdStyles } from '../test/antdStyles'

const resourceId = 'd9ccbf89-5004-4d52-8885-2ee377b7caa5'
const checkPath = `/api/studio/apps/PurchaseRequests/expressions/check`
const scopePath = `/api/studio/apps/PurchaseRequests/expressions/scope`
const path = '/validations/0/expression'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/**
 * A fetch that answers the check route with `check` and the scope route with `scope`, or with 404 when the test gives
 * none, as a place that the server does not know.
 */
function routes(check: () => Response | Promise<Response>, scope?: unknown) {
  return vi.fn(async (input: string) => {
    if (input === scopePath) {
      return scope === undefined ? json({}, 404) : json(scope)
    }
    return check()
  })
}

/** The expressions sent to the check route, in order. */
function checkedExpressions(fetchMock: ReturnType<typeof vi.fn>): string[] {
  return fetchMock.mock.calls
    .filter(([input]) => input === checkPath)
    .map(([, init]) => (JSON.parse((init as RequestInit).body as string) as { expression: string }).expression)
}

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderEditor(
  value: string,
  onChange = vi.fn(),
  extra: ReactNode = null,
  required = true,
  options: ScopeOption[] = [],
) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <ExpressionEditor
            application="PurchaseRequests"
            resourceId={resourceId}
            path={path}
            name="expression"
            value={value}
            required={required}
            draft={{}}
            options={options}
            onChange={onChange}
          />
          {extra}
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

function viewOf(name = 'expression'): EditorView {
  return EditorView.findFromDOM(screen.getByRole('textbox', { name }))!
}

function editTo(text: string) {
  const view = viewOf()
  view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } })
}

function underlined() {
  return document.querySelectorAll('.cm-lintRange-error')
}

afterEach(() => localStorage.clear())

describe('ExpressionEditor checks', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      routes(() => json({ type: 'text', diagnostics: [] })),
    )
  })

  function root() {
    return screen.getByRole('textbox', { name: 'expression' }).closest('[aria-busy]')!
  }

  /** Stubs the check route so that each answer waits until the test resolves it. */
  function holdChecks(scope?: unknown) {
    const answers: ((response: Response) => void)[] = []
    vi.stubGlobal(
      'fetch',
      routes(
        () =>
          new Promise<Response>((resolve) => {
            answers.push(resolve)
          }),
        scope,
      ),
    )
    return answers
  }

  it('is busy until its first check answers', async () => {
    const answers = holdChecks()
    renderEditor('title > 5')
    expect(root()).toHaveAttribute('aria-busy', 'true')

    await waitFor(() => expect(answers).toHaveLength(1))
    expect(root()).toHaveAttribute('aria-busy', 'true')
    answers[0](json({ type: 'boolean', diagnostics: [] }))
    await waitFor(() => expect(root()).toHaveAttribute('aria-busy', 'false'))
  })

  it('is busy again after an edit until the next answer', async () => {
    const answers = holdChecks()
    renderEditor('title > 5')
    await waitFor(() => expect(answers).toHaveLength(1))
    answers[0](json({ type: 'boolean', diagnostics: [] }))
    await waitFor(() => expect(root()).toHaveAttribute('aria-busy', 'false'))

    act(() => editTo('x'))
    expect(root()).toHaveAttribute('aria-busy', 'true')
    await waitFor(() => expect(answers).toHaveLength(2))
    expect(root()).toHaveAttribute('aria-busy', 'true')
    answers[1](json({ type: 'text', diagnostics: [] }))
    await waitFor(() => expect(root()).toHaveAttribute('aria-busy', 'false'))
  })

  it('is not busy after a check answers 400', async () => {
    const answers = holdChecks()
    renderEditor('title > 5')
    await waitFor(() => expect(answers).toHaveLength(1))
    answers[0](json({ title: 'The path holds no expression.' }, 400))
    await waitFor(() => expect(root()).toHaveAttribute('aria-busy', 'false'))
  })

  it('is not busy when a check cannot reach the server', async () => {
    vi.stubGlobal(
      'fetch',
      routes(async () => {
        throw new TypeError('Failed to fetch')
      }),
    )
    renderEditor('title > 5')
    await waitFor(() => expect(root()).toHaveAttribute('aria-busy', 'false'))
  })

  it('sends one check after five edits inside 300 ms, carrying the latest text', async () => {
    vi.useFakeTimers()
    try {
      const fetchMock = vi.mocked(fetch)
      renderEditor('title')
      await act(() => vi.advanceTimersByTimeAsync(0))
      fetchMock.mockClear()

      for (const text of ['title ', 'title >', 'title > ', 'title > 5', 'title > 50']) {
        editTo(text)
        await act(() => vi.advanceTimersByTimeAsync(50))
      }
      expect(checkedExpressions(fetchMock)).toEqual([])

      await act(() => vi.advanceTimersByTimeAsync(300))
      expect(checkedExpressions(fetchMock)).toEqual(['title > 50'])
    } finally {
      vi.useRealTimers()
    }
  })

  it('keeps the newer answer when an older one arrives after it', async () => {
    const answers = holdChecks()
    renderEditor('title > 5')
    await waitFor(() => expect(answers).toHaveLength(1))
    answers[0](json({ type: 'boolean', diagnostics: [] }))
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no'))

    editTo('title > 5 and')
    await waitFor(() => expect(answers).toHaveLength(2))
    editTo('title > 5 and ')
    await waitFor(() => expect(answers).toHaveLength(3))

    answers[2](json({ type: 'boolean', diagnostics: [] }))
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no'))
    answers[1](json({ type: null, diagnostics: [{ code: 'AXC0030', message: 'Old.', start: 0, end: 5 }] }))
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)))

    expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no')
    expect(screen.queryByTestId('studio-expression-error')).not.toBeInTheDocument()
    expect(underlined()).toHaveLength(0)
  })

  it('underlines the range of a diagnostic and shows its message, then its code', async () => {
    vi.stubGlobal(
      'fetch',
      routes(() => json({ type: null, diagnostics: [{ code: 'AXC0030', message: 'Bad name.', start: 0, end: 5 }] })),
    )
    renderEditor('title > 5')

    await waitFor(() => expect(underlined()).toHaveLength(1))
    expect(underlined()[0]).toHaveTextContent('title')
    await waitFor(() => expect(screen.getByTestId('studio-expression-error')).toHaveTextContent('Bad name. AXC0030'))
    expect(screen.getByText('Bad name.')).toBeInTheDocument()
    expect(screen.getByText('AXC0030').tagName).toBe('SMALL')
    expect(screen.queryByTestId('studio-expression-type')).not.toBeInTheDocument()
  })

  it('keeps the last result and reports every edit when a check gets no answer or 400', async () => {
    let calls = 0
    vi.stubGlobal(
      'fetch',
      routes(async () => {
        calls++
        if (calls === 1) {
          return json({ type: 'boolean', diagnostics: [] })
        }
        if (calls === 2) {
          throw new TypeError('Failed to fetch')
        }
        return json({ title: 'The path holds no expression.' }, 400)
      }),
    )
    const onChange = renderEditor('title > 5')
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no'))

    editTo('title > 50')
    await waitFor(() => expect(calls).toBe(2))
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)))
    expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no')

    editTo('title > 500')
    await waitFor(() => expect(calls).toBe(3))
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)))
    expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no')
    expect(onChange).toHaveBeenLastCalledWith(path, 'title > 500')
  })

  it.each([true, false])(
    'does not check an empty text when required is %s, and checks it once it has text',
    async (required) => {
      const fetchMock = vi.mocked(fetch)
      renderEditor('', vi.fn(), null, required)
      await waitFor(() =>
        expect(screen.getByRole('textbox', { name: 'expression' }).closest('[aria-busy]')).toHaveAttribute(
          'aria-busy',
          'false',
        ),
      )
      await act(() => new Promise((resolve) => setTimeout(resolve, 20)))
      expect(checkedExpressions(fetchMock)).toEqual([])

      editTo('title')
      await waitFor(() => expect(checkedExpressions(fetchMock)).toEqual(['title']))
    },
  )

  it('shows what the place needs until the check answers, then what the expression gives', async () => {
    const answers = holdChecks({ type: 'boolean', fields: [], functions: [] })
    renderEditor('title > 5')

    await waitFor(() =>
      expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Needs to give yes or no'),
    )
    await waitFor(() => expect(answers).toHaveLength(1))
    answers[0](json({ type: 'boolean', diagnostics: [] }))
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no'))
    expect(screen.getByTestId('studio-expression-type')).not.toHaveTextContent('Needs')
  })

  it('shows no Needs line for a place that takes any type', async () => {
    const answers = holdChecks({ type: null, fields: [], functions: [] })
    renderEditor('title > 5')

    await waitFor(() => expect(answers).toHaveLength(1))
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)))
    expect(screen.queryByTestId('studio-expression-type')).not.toBeInTheDocument()
  })

  it('loads the scope once, with the draft as it stands, and not again after an edit', async () => {
    const fetchMock = routes(() => json({ type: 'text', diagnostics: [] }), { type: null, fields: [], functions: [] })
    vi.stubGlobal('fetch', fetchMock)
    renderEditor('title > 5')
    await waitFor(() => expect(root()).toHaveAttribute('aria-busy', 'false'))
    editTo('title > 50')
    await waitFor(() => expect(checkedExpressions(fetchMock)).toEqual(['title > 5', 'title > 50']))

    const scopeCalls = fetchMock.mock.calls.filter(([input]) => input === scopePath)
    expect(scopeCalls).toHaveLength(1)
    expect(JSON.parse((scopeCalls[0] as unknown as [string, RequestInit])[1].body as string)).toEqual({
      resourceId,
      path,
      draft: {},
    })
  })

  it('does not report the value it was given as an edit', async () => {
    vi.stubGlobal(
      'fetch',
      routes(() => json({ type: 'boolean', diagnostics: [] })),
    )
    const onChange = renderEditor('title > 5')
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no'))
    expect(onChange).not.toHaveBeenCalled()
  })
})

describe('ExpressionEditor completion', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      routes(() => json({ type: 'text', diagnostics: [] })),
    )
  })

  it('opens with Ctrl+Space and inserts the picked option from the keyboard', async () => {
    const user = userEvent.setup()
    // The list sorts by label, so 'amount' comes first and ArrowDown reaches 'length()'.
    renderEditor('', vi.fn(), null, false, [
      { label: 'amount', type: 'field', cursorInside: false },
      { label: 'length()', type: 'function', cursorInside: true },
    ])

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await user.keyboard('{Control>}[Space]{/Control}')
    await screen.findByRole('option', { name: /amount/ })
    await act(() => new Promise((resolve) => setTimeout(resolve, 100)))
    await user.keyboard('{ArrowDown}')
    await waitFor(() => expect(screen.getByRole('option', { name: /length/ })).toHaveAttribute('aria-selected', 'true'))
    await user.keyboard('{Enter}')

    const view = viewOf()
    expect(view.state.doc.toString()).toBe('length()')
    expect(view.state.selection.main.head).toBe(7)
  })

  it('offers exactly the functions of the scope, with the cursor inside the brackets of one that takes parameters', async () => {
    const user = userEvent.setup()
    const functions = ['length', 'now'].map((name) => ({
      name,
      parameters: name === 'length' ? ['text'] : [],
      result: name === 'length' ? 'integer' : 'date-time',
      group: 'Text',
      example: `${name}()`,
      description: '',
    }))
    vi.stubGlobal(
      'fetch',
      routes(() => json({ type: 'text', diagnostics: [] }), { type: null, fields: [], functions }),
    )
    renderEditor('', vi.fn(), null, false, [{ label: 'amount', type: 'field', cursorInside: false }])

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalledWith(scopePath, expect.anything()))
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))
    await user.keyboard('{Control>}[Space]{/Control}')
    await screen.findByRole('option', { name: /amount/ })

    expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual(['amount', 'length()', 'now()'])
    await user.click(screen.getByRole('option', { name: /length/ }))
    expect(viewOf().state.doc.toString()).toBe('length()')
    expect(viewOf().state.selection.main.head).toBe(7)

    editTo('')
    await user.keyboard('{Control>}[Space]{/Control}')
    await user.click(await screen.findByRole('option', { name: /now/ }))
    expect(viewOf().state.doc.toString()).toBe('now()')
    expect(viewOf().state.selection.main.head).toBe(5)
  })

  it('offers no function when the scope is not answered', async () => {
    const user = userEvent.setup()
    renderEditor('', vi.fn(), null, false, [{ label: 'amount', type: 'field', cursorInside: false }])

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await user.keyboard('{Control>}[Space]{/Control}')
    await screen.findByRole('option', { name: /amount/ })

    expect(screen.getAllByRole('option')).toHaveLength(1)
  })
})

describe.each(['light', 'dark'] as const)('ExpressionEditor in %s mode', (mode) => {
  beforeEach(() => {
    localStorage.setItem('axis.themeMode', mode)
    vi.stubGlobal(
      'fetch',
      routes(() => json({ type: 'boolean', diagnostics: [] })),
    )
  })

  it('renders inside the theme root with an accessible name, and Tab leaves it', async () => {
    const user = userEvent.setup()
    renderEditor('title > 5', vi.fn(), <button type="button">Next</button>)

    const editor = screen.getByRole('textbox', { name: 'expression' })
    expect(editor.closest(`[data-theme-mode="${mode}"]`)).not.toBeNull()

    await user.click(editor)
    await user.tab()
    expect(screen.getByRole('button', { name: 'Next' })).toHaveFocus()
  })

  describe('with antd styles', () => {
    withAntdStyles()

    it('draws its tokens in the token colours, with none of the default highlight colours', async () => {
      renderEditor('title > 5 and true')

      const span = (text: string) => screen.getByText(text, { selector: '.cm-line span' })
      expect(span('title')).toHaveStyle({ color: colourTokens.primaryText[mode] })
      expect(span('>')).toHaveStyle({ color: colourTokens.textMuted[mode] })
      expect(span('5')).toHaveStyle({ color: colourTokens.infoText[mode] })
      expect(span('and')).toHaveStyle({ color: colourTokens.warningText[mode] })
      const defaults = [defaultHighlightStyle.style([tags.keyword]), defaultHighlightStyle.style([tags.number])]
      for (const element of document.querySelectorAll('.cm-line span')) {
        for (const name of defaults) {
          expect(element).not.toHaveClass(name!)
        }
      }
    })
  })
})
