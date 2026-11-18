import { defaultHighlightStyle } from '@codemirror/language'
import { EditorView } from '@codemirror/view'
import { tags } from '@lezer/highlight'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Profiler, type ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { formatValue } from '../platform/formatValue'
import { TextProvider } from '../platform/TextProvider'
import { colourTokens, sizeTokens } from '../platform/designTokens'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import type { ExpressionReading as Reading, StudioNames } from '../platform/studio'
import { PlatformContext, type Platform } from './context'
import ExpressionEditor from './ExpressionEditor'
import { rankEntries, type PanelEntry } from './expressionPanelEntries'
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
  options: ScopeOption[] | (() => ScopeOption[]) = [],
  onRender?: () => void,
) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <Profiler id="editor" onRender={() => onRender?.()}>
            <ExpressionEditor
              application="PurchaseRequests"
              resourceId={resourceId}
              path={path}
              name="expression"
              value={value}
              required={required}
              draft={() => ({})}
              options={typeof options === 'function' ? options : () => options}
              onChange={onChange}
            />
          </Profiler>
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

  /** The labels of the options in the list, in the order shown. Their text content holds the detail too. */
  function labels(): string[] {
    return [...document.querySelectorAll('.cm-completionLabel')].map((label) => label.textContent ?? '')
  }

  /** Picks the option with the label, by a click. */
  async function pick(user: ReturnType<typeof userEvent.setup>, label: string) {
    await waitFor(() => expect(labels()).toContain(label))
    const option = [...document.querySelectorAll('[role="option"]')].find(
      (item) => item.querySelector('.cm-completionLabel')?.textContent === label,
    )!
    await user.click(option)
  }

  /** Waits for the scope of the place to load, so that the functions are in the list. */
  async function scopeLoaded() {
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalledWith(scopePath, expect.anything()))
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))
  }

  function stubScope(scope: unknown) {
    vi.stubGlobal(
      'fetch',
      routes(() => json({ type: 'text', diagnostics: [] }), scope),
    )
  }

  const length = {
    name: 'length',
    parameters: ['text'],
    result: 'integer',
    group: 'Text',
    example: "length('abc')",
    description: 'Counts the characters of a text.',
  }

  it('opens with Ctrl+Space and inserts the picked option from the keyboard', async () => {
    const user = userEvent.setup()
    stubScope({ type: null, fields: [], functions: [length] })
    // Completion keeps the order of its sources, so ArrowUp from the first option wraps to the last, 'length()'.
    renderEditor('', vi.fn(), null, false, [{ label: 'amount', type: 'field', cursorInside: false }])

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await scopeLoaded()
    await user.keyboard('{Control>}[Space]{/Control}')
    await screen.findByRole('option', { name: /amount/ })
    await act(() => new Promise((resolve) => setTimeout(resolve, 100)))
    await user.keyboard('{ArrowUp}')
    await waitFor(() => expect(screen.getByRole('option', { name: /length/ })).toHaveAttribute('aria-selected', 'true'))
    await user.keyboard('{Enter}')

    const view = viewOf()
    expect(view.state.doc.toString()).toBe('length()')
    expect(view.state.selection.main.head).toBe(7)
  })

  it('offers an option that was added after the editor was drawn, without drawing the editor again', async () => {
    const user = userEvent.setup()
    stubScope({ type: null, fields: [], functions: [] })
    const options: ScopeOption[] = [{ label: 'amount', type: 'field', cursorInside: false }]
    const drawn = vi.fn()
    renderEditor('', vi.fn(), null, false, () => options, drawn)

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await scopeLoaded()
    await waitFor(() =>
      expect(screen.getByRole('textbox', { name: 'expression' }).closest('[aria-busy]')).toHaveAttribute(
        'aria-busy',
        'false',
      ),
    )
    options.push({ label: 'tax', type: 'field', cursorInside: false })
    drawn.mockClear()
    await user.keyboard('{Control>}[Space]{/Control}')

    await screen.findByRole('option', { name: /tax/ })
    expect(labels()).toContain('amount')
    expect(drawn).not.toHaveBeenCalled()
  })

  it('offers the functions of the scope after the fields and the language’s words, with the cursor inside the brackets of one that takes parameters', async () => {
    const user = userEvent.setup()
    const now = { ...length, name: 'now', parameters: [], result: 'date-time', example: 'now()', description: '' }
    stubScope({ type: null, fields: [], functions: [length, now] })
    renderEditor('', vi.fn(), null, false, [{ label: 'amount', type: 'field', cursorInside: false }])

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await scopeLoaded()
    await user.keyboard('{Control>}[Space]{/Control}')
    await screen.findByRole('option', { name: /amount/ })

    expect(labels()).toEqual(['amount', 'true', 'false', 'null', "'text'", "date('…')", 'not', 'length()', 'now()'])
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

    expect(labels()).toEqual(['amount', 'true', 'false', 'null', "'text'", "date('…')", 'not'])
  })

  it('builds an expression from picks, spaces and one Ctrl+Space, offering what fits after each value', async () => {
    const user = userEvent.setup()
    stubScope({ type: 'boolean', fields: [], functions: [] })
    renderEditor('', vi.fn(), null, false, [
      { label: 'totalAmount', type: 'field', cursorInside: false, valueType: 'decimal' },
    ])

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await scopeLoaded()
    await user.keyboard('{Control>}[Space]{/Control}')
    await pick(user, 'totalAmount')
    expect(document.querySelector('.cm-completionDetail')).toBeNull()

    await user.keyboard(' ')
    await waitFor(() => expect(labels()[0]).toBe('=='))
    await pick(user, 'is null')
    expect(viewOf().state.doc.toString()).toBe('totalAmount is null')

    await user.keyboard(' ')
    await waitFor(() => expect(labels()).toEqual(['and', 'or']))
    await pick(user, 'or')
    expect(viewOf().state.doc.toString()).toBe('totalAmount is null or')

    // After `or` the cursor follows no value, so the list does not open by itself.
    await user.keyboard(' ')
    await user.keyboard('{Control>}[Space]{/Control}')
    await pick(user, 'totalAmount')
    await user.keyboard(' ')
    await pick(user, '>')
    await user.keyboard(' 0')

    expect(viewOf().state.doc.toString()).toBe('totalAmount is null or totalAmount > 0')
  })

  it('opens the values of an enum field after its comparison and a quote, in order, and a pick closes the quote', async () => {
    const user = userEvent.setup()
    stubScope({ type: 'boolean', fields: [], functions: [] })
    renderEditor('', vi.fn(), null, false, [
      {
        label: 'status',
        type: 'field',
        cursorInside: false,
        valueType: 'enum',
        values: ['Draft', 'Submitted', 'Approved'],
      },
    ])

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await scopeLoaded()
    await user.keyboard('{Control>}[Space]{/Control}')
    await pick(user, 'status')
    await user.keyboard(' ')
    await pick(user, '==')
    await user.keyboard(" '")

    await waitFor(() => expect(labels()).toEqual(['Draft', 'Submitted', 'Approved']))
    await pick(user, 'Submitted')

    expect(viewOf().state.doc.toString()).toBe("status == 'Submitted'")
    expect(viewOf().state.selection.main.head).toBe(21)
  })

  it('explains a function with its signature, description and example, and a field with its type', async () => {
    const user = userEvent.setup()
    stubScope({ type: null, fields: [], functions: [length] })
    renderEditor('', vi.fn(), null, false, [
      { label: 'amount', type: 'field', cursorInside: false, valueType: 'decimal' },
    ])

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await scopeLoaded()
    await user.keyboard('{Control>}[Space]{/Control}')
    await screen.findByRole('option', { name: /amount/ })
    expect(document.querySelector('.cm-completionDetail')?.textContent).toBe('a decimal number')

    await act(() => new Promise((resolve) => setTimeout(resolve, 100)))
    await user.keyboard('{ArrowUp}')

    await waitFor(() => {
      const info = document.querySelector('.cm-completionInfo')
      expect(info?.textContent).toContain('length(text)')
      expect(info?.textContent).toContain('Counts the characters of a text.')
      expect([...(info?.querySelectorAll('code') ?? [])].map((code) => code.textContent)).toEqual([
        'length(text)',
        "length('abc')",
      ])
    })
    const detail = [...document.querySelectorAll('.cm-completionDetail')].at(-1)
    expect(detail?.textContent).toBe('length(text)')
  })
})

describe('ExpressionEditor signature', () => {
  const contains = {
    name: 'contains',
    parameters: ['text', 'text'],
    result: 'boolean',
    group: 'text',
    example: "contains(title, 'PR-')",
    description: 'Whether the first text holds the second.',
  }

  async function renderWithScope(scope: unknown, value: string) {
    vi.stubGlobal(
      'fetch',
      routes(() => json({ type: 'boolean', diagnostics: [] }), scope),
    )
    renderEditor(value)
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalledWith(scopePath, expect.anything()))
  }

  function moveTo(anchor: number) {
    act(() => viewOf().dispatch({ selection: { anchor } }))
  }

  it('shows the signature with the parameter at the cursor in bold, and hides it outside the call', async () => {
    const value = "contains(title, 'x')"
    await renderWithScope({ type: 'boolean', fields: [], functions: [contains] }, value)

    moveTo(16)
    const line = await screen.findByTestId('studio-expression-signature')
    expect(line.textContent).toContain('contains(text, text)')
    expect(line.querySelectorAll('strong')).toHaveLength(1)
    expect(line.querySelector('strong')!.previousSibling!.textContent).toBe(', ')
    expect(line.textContent).toContain('Whether the first text holds the second.')

    moveTo(value.length)
    expect(screen.queryByTestId('studio-expression-signature')).toBeNull()
  })

  it('bolds the repeating last parameter for an argument past the list, and nothing for other functions', async () => {
    const concat = { ...contains, name: 'concat', parameters: ['text', '…'], description: '' }
    const value = 'concat(a, b, c) contains(a, b, c)'
    await renderWithScope({ type: 'text', fields: [], functions: [concat, contains] }, value)

    moveTo(13)
    const repeating = await screen.findByTestId('studio-expression-signature')
    expect(repeating.textContent).toContain('concat(text, …)')
    expect(Array.from(repeating.querySelectorAll('strong'), (item) => item.textContent)).toEqual(['…'])

    moveTo(value.length - 1)
    const line = screen.getByTestId('studio-expression-signature')
    expect(line.textContent).toContain('contains(text, text)')
    expect(line.querySelectorAll('strong')).toHaveLength(0)
  })

  it('shows no line when the scope is not answered', async () => {
    await renderWithScope(undefined, "contains(title, 'x')")

    moveTo(16)
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))

    expect(screen.queryByTestId('studio-expression-signature')).toBeNull()
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

  it('renders inside the theme root with an accessible name, and Tab reaches Expand before it leaves', async () => {
    const user = userEvent.setup()
    renderEditor('title > 5', vi.fn(), <button type="button">Next</button>)

    const editor = screen.getByRole('textbox', { name: 'expression' })
    expect(editor.closest(`[data-theme-mode="${mode}"]`)).not.toBeNull()

    await user.click(editor)
    await user.tab()
    expect(screen.getByRole('button', { name: 'Expand' })).toHaveFocus()
    await user.tab()
    expect(screen.getByRole('button', { name: 'Next' })).toHaveFocus()
  })

  it('shows Expand inside the expression box, in its bordered editor', () => {
    renderEditor('title > 5')

    const box = screen.getByRole('textbox', { name: 'expression' }).closest('.cm-editor') as HTMLElement
    expect(within(box).getByRole('button', { name: 'Expand' })).toBeInTheDocument()
  })

  it('opens the expand dialog when Enter is pressed on Expand, reached from the box by Tab', async () => {
    const user = userEvent.setup()
    renderEditor('title > 5')

    await user.click(screen.getByRole('textbox', { name: 'expression' }))
    await user.tab()
    expect(screen.getByRole('button', { name: 'Expand' })).toHaveFocus()
    await user.keyboard('{Enter}')
    expect(await screen.findByRole('dialog')).toBeInTheDocument()
  })

  describe('with antd styles', () => {
    withAntdStyles()

    it('leaves room at the end of each line in the box, and none in the dialog', async () => {
      const user = userEvent.setup()
      renderEditor('title > 5')
      const boxLine = screen.getByRole('textbox', { name: 'expression' }).querySelector<HTMLElement>('.cm-line')!
      expect(parseFloat(getComputedStyle(boxLine).paddingRight)).toBeGreaterThanOrEqual(sizeTokens.smallControl)

      await user.click(screen.getByRole('button', { name: 'Expand' }))
      const dialog = await screen.findByRole('dialog')
      const content = within(dialog).getByRole('textbox', { name: 'expression' })
      // Eight lines of 1.4 times the 14px font, plus 8px of padding, which jsdom resolves from the calc().
      expect(getComputedStyle(content).minHeight).toBe('164.8px')
      const dialogLine = content.querySelector<HTMLElement>('.cm-line')!
      expect(parseFloat(getComputedStyle(dialogLine).paddingRight)).toBeLessThan(sizeTokens.smallControl)
      expect(within(dialog).queryByRole('button', { name: 'Expand' })).not.toBeInTheDocument()
    })

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

describe('ExpressionEditor expand', () => {
  const scope = {
    type: 'boolean',
    fields: [
      { name: 'date', labelKey: null, type: 'date' },
      { name: 'amount', labelKey: null, type: 'decimal' },
    ],
    functions: [
      {
        name: 'toDate',
        parameters: ['text'],
        result: 'date',
        group: 'Date',
        example: "toDate('2026-01-31')",
        description: 'Turns a text into a date.',
      },
    ],
  }

  async function openDialog(onChange = vi.fn()) {
    const user = userEvent.setup()
    vi.stubGlobal(
      'fetch',
      routes(() => json({ type: 'boolean', diagnostics: [] }), scope),
    )
    renderEditor('amount > 0', onChange, null, true, [{ label: 'refund()', type: 'rule', cursorInside: true }])
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalledWith(scopePath, expect.anything()))
    await user.click(screen.getByRole('button', { name: 'Expand' }))
    return { user, dialog: await screen.findByRole('dialog') }
  }

  it('lists one search across fields, operators and functions best first, and inserts a click at the cursor', async () => {
    const onChange = vi.fn()
    const { user, dialog } = await openDialog(onChange)
    expect(within(dialog).getByRole('textbox', { name: 'expression' })).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Expand' })).not.toBeInTheDocument()

    await user.type(within(dialog).getByRole('textbox', { name: 'Search fields, operators and functions' }), 'date')
    const list = within(dialog).getByRole('list', { name: 'Search fields, operators and functions' })
    await waitFor(() => expect(within(list).getAllByRole('listitem')).toHaveLength(3))
    expect(
      within(list)
        .getAllByRole('listitem')
        .map((item) => item.querySelector('code')!.textContent),
    ).toEqual(['date', "date('…')", 'toDate()'])

    const dialogView = EditorView.findFromDOM(within(dialog).getByRole('textbox', { name: 'expression' }))!
    dialogView.dispatch({ selection: { anchor: dialogView.state.doc.length } })
    await user.click(within(list).getByRole('button', { name: 'toDate()' }))

    expect(dialogView.state.doc.toString()).toBe('amount > 0toDate()')
    expect(dialogView.state.selection.main.head).toBe(17)
    expect(onChange).toHaveBeenLastCalledWith(path, 'amount > 0toDate()')
  })

  it('lists the fields, operators and functions in tabs, with the rules after the functions', async () => {
    const { user, dialog } = await openDialog()
    const names = (list: HTMLElement) =>
      within(list)
        .getAllByRole('listitem')
        .map((item) => item.querySelector('code')!.textContent)

    await waitFor(() => expect(names(within(dialog).getByRole('list', { name: 'Fields' }))).toEqual(['date', 'amount']))
    await user.click(within(dialog).getByRole('tab', { name: 'Operators' }))
    expect(names(within(dialog).getByRole('list', { name: 'Operators' })).slice(0, 2)).toEqual(['==', '!='])
    await user.click(within(dialog).getByRole('tab', { name: 'Functions' }))
    expect(names(within(dialog).getByRole('list', { name: 'Functions' }))).toEqual(['toDate()', 'refund()'])
  })

  it('shows that nothing matches', async () => {
    const { user, dialog } = await openDialog()

    await user.type(within(dialog).getByRole('textbox', { name: 'Search fields, operators and functions' }), 'zzzz')
    expect(within(dialog).getByText('Nothing matches')).toBeInTheDocument()
  })

  it('puts the cursor inside the quotes of a text', async () => {
    const onChange = vi.fn()
    const { user, dialog } = await openDialog(onChange)

    await user.click(within(dialog).getByRole('tab', { name: 'Operators' }))
    const dialogView = EditorView.findFromDOM(within(dialog).getByRole('textbox', { name: 'expression' }))!
    dialogView.dispatch({ selection: { anchor: dialogView.state.doc.length } })
    await user.click(within(dialog).getByRole('button', { name: "'text'" }))

    expect(dialogView.state.doc.toString()).toBe("amount > 0''")
    expect(dialogView.state.selection.main.head).toBe(11)
  })
})

describe('ExpressionEditor reading', () => {
  /** The labels of the purchase request's fields and values, as the names answer lists them. */
  const names = {
    locales: ['en', 'vi'],
    texts: [
      { key: 'purchaseRequest.status', texts: { en: 'Status', vi: 'Trạng thái' }, usedBy: 1 },
      { key: 'purchaseRequest.status.draft', texts: { en: 'Draft', vi: 'Nháp' }, usedBy: 1 },
      { key: 'purchaseRequest.amount', texts: { en: 'Amount', vi: 'Số tiền' }, usedBy: 1 },
      { key: 'purchaseRequest.urgent', texts: { en: 'Urgent', vi: 'Khẩn cấp' }, usedBy: 1 },
      { key: 'purchaseRequest.dueDate', texts: { en: 'Due date', vi: 'Hạn chót' }, usedBy: 1 },
    ],
  } as unknown as StudioNames

  /** The reading of `(status == 'draft' and amount >= 1000) or not (urgent or dueDate < date('2026-01-31'))`. */
  const reading: Reading = {
    kind: 'any',
    items: [
      {
        kind: 'all',
        items: [
          {
            kind: 'phrase',
            words: 'equal',
            slots: [
              [{ kind: 'name', name: 'status', labelKey: 'purchaseRequest.status' }],
              [{ kind: 'value', type: 'enum', value: 'draft', labelKey: 'purchaseRequest.status.draft' }],
            ],
          },
          {
            kind: 'phrase',
            words: 'greater-or-equal',
            slots: [
              [{ kind: 'name', name: 'amount', labelKey: 'purchaseRequest.amount' }],
              [{ kind: 'value', type: 'integer', value: 1000, labelKey: null }],
            ],
          },
        ],
      },
      {
        kind: 'none',
        items: [
          {
            kind: 'phrase',
            words: 'is-true',
            slots: [[{ kind: 'name', name: 'urgent', labelKey: 'purchaseRequest.urgent' }]],
          },
          {
            kind: 'phrase',
            words: 'less.date',
            slots: [
              [{ kind: 'name', name: 'dueDate', labelKey: 'purchaseRequest.dueDate' }],
              [{ kind: 'value', type: 'date', value: '2026-01-31', labelKey: null }],
            ],
          },
        ],
      },
    ],
  }

  /** Renders the editor in a locale, with the answers of the check route and the scope route. */
  function renderReading(locale: string, check: () => Response, scope?: unknown) {
    vi.stubGlobal('fetch', routes(check, scope))
    mountReading(locale)
  }

  function mountReading(locale: string) {
    render(
      <ThemeModeProvider>
        <PlatformContext.Provider value={{ current: { locale, texts: {} } } as Platform}>
          <TextProvider
            catalogs={[
              { texts: locale === 'vi' ? platformVietnamese : platformEnglish, fallbackTexts: platformEnglish },
            ]}
            development={false}
          >
            <ExpressionEditor
              application="PurchaseRequests"
              resourceId={resourceId}
              path={path}
              name="expression"
              value="status == 'draft'"
              required
              draft={() => ({})}
              options={() => []}
              names={names}
              onChange={vi.fn()}
            />
          </TextProvider>
        </PlatformContext.Provider>
      </ThemeModeProvider>,
    )
  }

  const date = (locale: string) =>
    formatValue({ name: '', type: 'date' }, '2026-01-31', {}, { locale, text: (key) => key })

  it.each([
    {
      locale: 'en',
      leadIn: 'In words:',
      bold: ['Status', 'Amount', 'Urgent', 'Due date'],
      andLine: 'Status is Draft and Amount is at least 1,000',
      urgentLine: 'Urgent is yes',
      dueLine: 'Due date is before',
    },
    {
      locale: 'vi',
      leadIn: 'Nghĩa là:',
      bold: ['Trạng thái', 'Số tiền', 'Khẩn cấp', 'Hạn chót'],
      andLine: 'Trạng thái bằng Nháp và Số tiền lớn hơn hoặc bằng 1.000',
      urgentLine: 'Khẩn cấp là có',
      dueLine: 'Hạn chót trước',
    },
  ])('reads the expression in $locale with its labels, values and numbers', async (copy) => {
    renderReading(copy.locale, () => json({ type: 'boolean', diagnostics: [], reading }))

    const line = await screen.findByTestId('studio-expression-reading')
    expect(line).toHaveTextContent(copy.leadIn)
    expect(Array.from(line.querySelectorAll('strong'), (label) => label.textContent)).toEqual(copy.bold)
    expect(line).toHaveTextContent(copy.andLine)
    expect(line).toHaveTextContent(copy.urgentLine)
    expect(line).toHaveTextContent(`${copy.dueLine} ${date(copy.locale)}`)
  })

  it('reads a group that holds a group as a list, with a nested list under "None of these:"', async () => {
    renderReading('en', () => json({ type: 'boolean', diagnostics: [], reading }))

    const line = await screen.findByTestId('studio-expression-reading')
    const group = within(line).getByText('At least one of these:').parentElement!
    const [first, second] = Array.from(group.querySelector(':scope > ul')!.children)
    expect(first).toHaveTextContent('Status is Draft and Amount is at least 1,000')
    expect(first.querySelector('ul')).toBeNull()
    expect(second).toHaveTextContent(/^None of these:/)
    expect(Array.from(second.querySelectorAll('li'), (item) => item.textContent)).toEqual([
      'Urgent is yes',
      `Due date is before ${date('en')}`,
    ])
  })

  it('shows no type line at a place that needs yes or no', async () => {
    renderReading('en', () => json({ type: 'boolean', diagnostics: [], reading }), {
      type: 'boolean',
      fields: [],
      functions: [],
    })

    await screen.findByTestId('studio-expression-reading')
    await waitFor(() => expect(screen.queryByTestId('studio-expression-type')).not.toBeInTheDocument())
  })

  it('shows the type under the reading at a place that takes any type', async () => {
    renderReading('en', () => json({ type: 'boolean', diagnostics: [], reading }), {
      type: null,
      fields: [],
      functions: [],
    })

    const line = await screen.findByTestId('studio-expression-reading')
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no'))
    expect(line.compareDocumentPosition(screen.getByTestId('studio-expression-type'))).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    )
  })

  it('shows the error, and no reading, while the check answers a diagnostic', async () => {
    let answer: unknown = { type: 'boolean', diagnostics: [], reading }
    renderReading('en', () => json(answer))
    await screen.findByTestId('studio-expression-reading')

    answer = { type: null, diagnostics: [{ code: 'AXC0030', message: 'Bad name.', start: 0, end: 5 }], reading: null }
    act(() => editTo('nme > 1'))

    await waitFor(() => expect(screen.getByTestId('studio-expression-error')).toHaveTextContent('Bad name. AXC0030'))
    expect(screen.queryByTestId('studio-expression-reading')).not.toBeInTheDocument()
  })

  it.each([
    ['en', 'In words: today plus 7 days'],
    ['vi', 'Nghĩa là: hôm nay cộng 7 ngày'],
  ])('reads the function call addDays(today(), 7) in words in %s', async (locale, line) => {
    renderReading(locale, () =>
      json({
        type: 'date',
        diagnostics: [],
        reading: {
          kind: 'phrase',
          words: 'fn.addDays',
          slots: [
            [{ kind: 'phrase', words: 'fn.today', slots: [] }],
            [{ kind: 'value', type: 'integer', value: 7, labelKey: null }],
          ],
        },
      }),
    )

    await waitFor(() => expect(screen.getByTestId('studio-expression-reading')).toHaveTextContent(line))
  })

  it('reads a field with no label as its name, and a text value between single quotes', async () => {
    renderReading('en', () =>
      json({
        type: 'boolean',
        diagnostics: [],
        reading: {
          kind: 'phrase',
          words: 'equal',
          slots: [
            [{ kind: 'name', name: 'reference', labelKey: null }],
            [{ kind: 'value', type: 'text', value: 'PR-', labelKey: null }],
          ],
        },
      }),
    )

    const line = await screen.findByTestId('studio-expression-reading')
    expect(line).toHaveTextContent("reference is 'PR-'")
    expect(within(line).getByText('reference').tagName).toBe('STRONG')
  })

  it('asks for the scope again with the next check answer when the first one had none', async () => {
    let scopeAnswer: () => Response = () => json({}, 404)
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) =>
        input === scopePath ? scopeAnswer() : json({ type: 'boolean', diagnostics: [], reading }),
      ),
    )
    mountReading('en')

    await screen.findByTestId('studio-expression-reading')
    await waitFor(() => expect(screen.getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no'))

    scopeAnswer = () => json({ type: 'boolean', fields: [], functions: [] })
    act(() => editTo('urgent'))

    await waitFor(() => expect(screen.queryByTestId('studio-expression-type')).not.toBeInTheDocument())
    expect(screen.getByTestId('studio-expression-reading')).toBeInTheDocument()
  })
})

describe('rankEntries', () => {
  const entry = (name: string, description: string): PanelEntry => ({
    kind: 'operator',
    name,
    insert: name,
    cursorInside: false,
    description,
  })

  it('finds an entry by the words of its description, after those whose name matches', () => {
    const entries = [entry("'text'", 'A text, between single quotes'), entry('quotes', ''), entry('x', 'no match')]
    expect(rankEntries(entries, ' Quotes ').map((item) => item.name)).toEqual(['quotes', "'text'"])
  })
})
