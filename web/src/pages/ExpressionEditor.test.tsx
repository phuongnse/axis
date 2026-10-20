import { EditorView } from '@codemirror/view'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import ExpressionEditor from './ExpressionEditor'
import type { ScopeOption } from './expressionScope'

const resourceId = 'd9ccbf89-5004-4d52-8885-2ee377b7caa5'
const checkPath = `/api/studio/apps/PurchaseRequests/expressions/check`
const path = '/validations/0/expression'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
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
      vi.fn(async () => json({ type: 'text', diagnostics: [] })),
    )
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
    const answers: ((response: Response) => void)[] = []
    const fetchMock = vi.fn(
      () =>
        new Promise<Response>((resolve) => {
          answers.push(resolve)
        }),
    )
    vi.stubGlobal('fetch', fetchMock)
    renderEditor('title > 5')
    await waitFor(() => expect(answers).toHaveLength(1))
    answers[0](json({ type: 'boolean', diagnostics: [] }))
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('boolean'))

    editTo('title > 5 and')
    await waitFor(() => expect(answers).toHaveLength(2))
    editTo('title > 5 and ')
    await waitFor(() => expect(answers).toHaveLength(3))

    answers[2](json({ type: 'boolean', diagnostics: [] }))
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('boolean'))
    answers[1](json({ type: null, diagnostics: [{ code: 'AXC0030', message: 'Old.', start: 0, end: 5 }] }))
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)))

    expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('boolean')
    expect(screen.queryByTestId('studio-expression-error')).not.toBeInTheDocument()
    expect(underlined()).toHaveLength(0)
  })

  it('underlines the range of a diagnostic and shows its code and message', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        json({ type: null, diagnostics: [{ code: 'AXC0030', message: 'Bad name.', start: 0, end: 5 }] }),
      ),
    )
    renderEditor('title > 5')

    await waitFor(() => expect(underlined()).toHaveLength(1))
    expect(underlined()[0]).toHaveTextContent('title')
    expect(screen.getByTestId('studio-expression-error')).toHaveTextContent('AXC0030: Bad name.')
    expect(screen.queryByTestId('studio-expression-type')).not.toBeInTheDocument()
  })

  it('keeps the last result and reports every edit when a check gets no answer or 400', async () => {
    let calls = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
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
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('boolean'))

    editTo('title > 50')
    await waitFor(() => expect(calls).toBe(2))
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)))
    expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('boolean')

    editTo('title > 500')
    await waitFor(() => expect(calls).toBe(3))
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)))
    expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('boolean')
    expect(onChange).toHaveBeenLastCalledWith(path, 'title > 500')
  })

  it('does not check an empty optional expression, and checks it once it has text', async () => {
    const fetchMock = vi.mocked(fetch)
    renderEditor('', vi.fn(), null, false)
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)))
    expect(checkedExpressions(fetchMock)).toEqual([])

    editTo('title')
    await waitFor(() => expect(checkedExpressions(fetchMock)).toEqual(['title']))
  })

  it('does not report the value it was given as an edit', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => json({ type: 'boolean', diagnostics: [] })),
    )
    const onChange = renderEditor('title > 5')
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('boolean'))
    expect(onChange).not.toHaveBeenCalled()
  })
})

describe('ExpressionEditor completion', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => json({ type: 'text', diagnostics: [] })),
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
    await user.keyboard('{Enter}')

    const view = viewOf()
    expect(view.state.doc.toString()).toBe('length()')
    expect(view.state.selection.main.head).toBe(7)
  })
})

describe.each(['light', 'dark'])('ExpressionEditor in %s mode', (mode) => {
  beforeEach(() => {
    localStorage.setItem('axis.themeMode', mode)
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => json({ type: 'boolean', diagnostics: [] })),
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
})
