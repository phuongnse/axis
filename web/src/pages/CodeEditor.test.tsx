import { foldedRanges } from '@codemirror/language'
import { EditorView } from '@codemirror/view'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { ConfigProvider, theme } from 'antd'
import { createRef, type ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { antThemeFor } from '../platform/antTheme'
import { changeLook } from '../platform/changeTint'
import { designTokensFor } from '../platform/designTokens'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import type { ThemeMode } from '../platform/themeMode'
import { withAntdStyles } from '../test/antdStyles'
import CodeEditor, { type CodeEditorHandle, type CodeEditorProps } from './CodeEditor'
import { jsonViewHeight } from './jsonViewHeight'

interface RenderOptions {
  /** The mode the provider starts in, so a test does not depend on the stored or system choice. */
  initialMode?: ThemeMode
  /** Wraps the editor, for example in a nested ConfigProvider with page values. */
  wrap?: (editor: ReactNode) => ReactNode
  /** The platform texts the editor reads, with English as the fallback. English when absent. */
  texts?: Record<string, string>
}

function renderEditor(
  props: Partial<CodeEditorProps> = {},
  { initialMode, wrap, texts: initialTexts = platformEnglish }: RenderOptions = {},
) {
  const handle = createRef<CodeEditorHandle>()
  let texts = initialTexts
  const view = (extra: Partial<CodeEditorProps>) => {
    const editor = (
      <CodeEditor label="Code" value={'one\ntwo\nthree'} language="plain" ref={handle} {...props} {...extra} />
    )
    return (
      <ThemeModeProvider initialMode={initialMode}>
        <TextProvider catalogs={[{ texts, fallbackTexts: platformEnglish }]} development={false}>
          <footer />
          {wrap ? wrap(editor) : editor}
        </TextProvider>
      </ThemeModeProvider>
    )
  }
  const { rerender } = render(view({}))
  return {
    handle,
    rerender: (extra: Partial<CodeEditorProps>) => rerender(view(extra)),
    /** Shows the editor in other texts, as a change of locale does. */
    setTexts: (next: Record<string, string>) => {
      texts = next
      rerender(view({}))
    },
  }
}

const editorView = () => EditorView.findFromDOM(screen.getByRole('textbox', { name: 'Code' }))!
const typeAtStart = (view: EditorView) => view.dispatch({ changes: { from: 0, insert: 'x' }, userEvent: 'input.type' })

/** A colour as the browser writes it, so a hex value and the rgb value jsdom reads back compare equal. */
function asStyled(colour: string) {
  const probe = document.createElement('div')
  probe.style.backgroundColor = colour
  return probe.style.backgroundColor
}

/**
 * The background of a selected search match. jsdom's computed style drops the editor's rules, so the test adds a match
 * to the content and reads the rules that match it, as JsonView.test.tsx does for the scroller.
 */
function selectedMatchBackground() {
  const match = document.createElement('span')
  match.className = 'cm-searchMatch cm-searchMatch-selected'
  const content = document.querySelector('.cm-content')!
  content.append(match)
  const rules = Array.from(document.styleSheets).flatMap((sheet) => Array.from(sheet.cssRules))
  const styleRules = rules.filter((rule): rule is CSSStyleRule => 'selectorText' in rule)
  const backgrounds = styleRules
    .filter((rule) => match.matches(rule.selectorText))
    .map((rule) => rule.style.getPropertyValue('background-color'))
    .filter(Boolean)
  match.remove()
  return backgrounds.at(-1)
}

/**
 * The value that wins in the cascade for a property of an element of the editor: the last rule that matches and sets it.
 * jsdom's computed style drops the editor's rules, so the test reads the rules, as `selectedMatchBackground` does.
 */
function winningRule(element: Element, property: string) {
  const rules = Array.from(document.styleSheets).flatMap((sheet) => Array.from(sheet.cssRules))
  const styleRules = rules.filter((rule): rule is CSSStyleRule => 'selectorText' in rule)
  return styleRules
    .filter((rule) => element.matches(rule.selectorText))
    .map((rule) => rule.style.getPropertyValue(property))
    .filter(Boolean)
    .at(-1)
}

describe('CodeEditor', () => {
  it('accepts typing and reports it', () => {
    const onChange = vi.fn()
    renderEditor({ onChange })
    const view = editorView()

    act(() => typeAtStart(view))

    expect(view.state.doc.toString()).toBe('xone\ntwo\nthree')
    expect(view.state.readOnly).toBe(false)
    expect(onChange).toHaveBeenCalledExactlyOnceWith('xone\ntwo\nthree')
  })

  it('refuses typing when it is read-only, but still takes a new value', () => {
    const onChange = vi.fn()
    const { rerender } = renderEditor({ readOnly: true, onChange })
    const view = editorView()

    act(() => typeAtStart(view))

    expect(view.state.readOnly).toBe(true)
    expect(view.state.doc.toString()).toBe('one\ntwo\nthree')
    expect(onChange).not.toHaveBeenCalled()

    rerender({ readOnly: true, onChange, value: 'other' })

    expect(view.state.doc.toString()).toBe('other')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('accepts typing again once it is no longer read-only', () => {
    const { rerender } = renderEditor({ readOnly: true })
    rerender({ readOnly: false })
    const view = editorView()

    act(() => typeAtStart(view))

    expect(view.state.doc.toString()).toBe('xone\ntwo\nthree')
  })

  it('reports the cursor once it is built and each time it moves', () => {
    const onCursor = vi.fn()
    renderEditor({ onCursor })
    expect(onCursor).toHaveBeenLastCalledWith({ line: 1, column: 1 })

    const view = editorView()
    act(() => view.dispatch({ selection: { anchor: 'one\ntw'.length } }))

    expect(onCursor).toHaveBeenLastCalledWith({ line: 2, column: 3 })
  })

  it('moves the cursor to the start of a line, held within the text', () => {
    const onCursor = vi.fn()
    const { handle } = renderEditor({ onCursor, readOnly: true })

    act(() => handle.current!.goTo(2))
    expect(onCursor).toHaveBeenLastCalledWith({ line: 2, column: 1 })

    act(() => handle.current!.goTo(99))
    expect(onCursor).toHaveBeenLastCalledWith({ line: 3, column: 1 })

    act(() => handle.current!.goTo(0))
    expect(onCursor).toHaveBeenLastCalledWith({ line: 1, column: 1 })
  })

  it('shows the fold gutter with Axis texts', () => {
    renderEditor({ language: 'csharp', value: 'class A {\n    int x;\n}' })

    expect(document.querySelector('.cm-foldGutter')).not.toBeNull()
    expect(editorView().state.phrase('Fold line')).toBe('Fold lines')
  })

  describe('the find bar', () => {
    // The bar takes its texts from the editor's own texts, so an editor given no texts of its own shows the locale's.
    describe.each([
      { locale: 'English', texts: platformEnglish, field: 'Find in JSON', count: '1 of 2', none: 'No matches' },
      {
        locale: 'Vietnamese',
        texts: platformVietnamese,
        field: 'Tìm trong JSON',
        count: '1/2',
        none: 'Không tìm thấy',
      },
    ])('in $locale', ({ texts, field, count, none }) => {
      function openFind() {
        renderEditor({}, { texts })
        fireEvent.keyDown(editorView().contentDOM, { key: 'f', ctrlKey: true })
        return screen.getByRole('textbox', { name: field })
      }

      it('finds the field by its name and counts the matches', () => {
        const input = openFind()

        fireEvent.input(input, { target: { value: 'o' } })

        expect(screen.getByRole('status')).toHaveTextContent(count)
      })

      it('says when nothing matches', () => {
        const input = openFind()

        fireEvent.input(input, { target: { value: 'zzz' } })

        expect(screen.getByRole('status')).toHaveTextContent(none)
      })
    })

    it('says a result in a new locale while the bar is open', () => {
      const { setTexts } = renderEditor({}, { texts: platformEnglish })
      fireEvent.keyDown(editorView().contentDOM, { key: 'f', ctrlKey: true })
      fireEvent.input(screen.getByRole('textbox', { name: 'Find in JSON' }), { target: { value: 'zzz' } })
      expect(screen.getByRole('status')).toHaveTextContent('No matches')

      setTexts(platformVietnamese)

      expect(screen.getByRole('status')).toHaveTextContent('Không tìm thấy')
    })
  })

  describe('the fold marks', () => {
    // The first line folds, so a click on the gutter, which jsdom maps to the first line, reaches the part.
    const foldable = { language: 'csharp', value: 'class A {\n    int x;\n}' } as const
    const gutter = () => document.querySelector<HTMLElement>('.cm-foldGutter')!
    // CodeMirror's hidden spacer, which sizes the gutter, carries a mark's name too, so the visible mark is the one found.
    const foldMark = (name: string) =>
      within(gutter())
        .getAllByTitle(name)
        .find((mark) => mark.closest<HTMLElement>('.cm-gutterElement')?.style.visibility !== 'hidden')!

    describe.each(['light', 'dark'] as const)('in %s mode', (mode) => {
      it('are the chevron at 12 px in the muted text colour, down when open and right when folded', () => {
        renderEditor(foldable, { initialMode: mode })

        const open = foldMark('Fold lines')
        expect(open.querySelector('.anticon-down')).toHaveStyle({
          fontSize: '12px',
          color: designTokensFor(mode).colour.textMuted,
        })
        expect(open.querySelector<SVGElement>('svg')?.style.transform).toBe('')

        fireEvent.click(open)

        const folded = foldMark('Unfold lines')
        expect(folded.querySelector('.anticon-down')).toHaveStyle({
          fontSize: '12px',
          color: designTokensFor(mode).colour.textMuted,
        })
        expect(folded.querySelector<SVGElement>('svg')?.style.transform).toBe('rotate(270deg)')
      })
    })

    it('folds the part on a click and unfolds it on a second click', () => {
      renderEditor(foldable)
      const view = editorView()

      fireEvent.click(foldMark('Fold lines'))

      expect(foldedRanges(view.state).size).toBe(1)

      fireEvent.click(foldMark('Unfold lines'))

      expect(foldedRanges(view.state).size).toBe(0)
      expect(foldMark('Fold lines')).toBeInTheDocument()
    })
  })

  it('marks the current line', () => {
    renderEditor()

    expect(document.querySelector('.cm-activeLine')).not.toBeNull()
  })

  describe('with fill', () => {
    const restores: (() => void)[] = []
    afterEach(() => {
      restores.splice(0).forEach((restore) => restore())
    })

    function stub<T extends object, K extends keyof T>(target: T, key: K, value: T[K]) {
      const original = Object.getOwnPropertyDescriptor(target, key)
      Object.defineProperty(target, key, { configurable: true, value })
      restores.push(() => {
        if (original) {
          Object.defineProperty(target, key, original)
        } else {
          delete target[key]
        }
      })
    }

    const rectOf = (top: number, height: number) => () => ({ top, bottom: top + height, height }) as DOMRect

    it('ends above the shell footer, found as the footer element', () => {
      stub(window, 'innerHeight', 1000)
      stub(HTMLElement.prototype, 'offsetParent', document.body)
      stub(HTMLElement.prototype, 'getBoundingClientRect', function (this: HTMLElement) {
        return this.tagName === 'FOOTER' ? rectOf(952, 48)() : rectOf(100, 0)()
      })

      renderEditor({ fill: true })

      const host = screen.getByRole('textbox', { name: 'Code' }).closest('.cm-editor')!.parentElement!
      expect(host.style.height).toBe(
        `${jsonViewHeight({ windowHeight: 1000, top: 100, footerHeight: 48, below: 0, gap: 16 })}px`,
      )
      expect(host.style.height).toBe('836px')
    })

    it('has no fixed height without fill', () => {
      renderEditor()

      const host = screen.getByRole('textbox', { name: 'Code' }).closest('.cm-editor')!.parentElement!
      expect(host.style.height).toBe('')
    })
  })

  describe('with antd styles', () => {
    withAntdStyles()

    describe.each(['light', 'dark'] as const)('the selected search match in %s mode', (mode) => {
      it('takes the selected tint of the theme in use, with no page values', () => {
        renderEditor({}, { initialMode: mode })

        const tint = theme.getDesignToken(antThemeFor(mode)).controlItemBgActive
        expect(selectedMatchBackground()).toBe(asStyled(tint))
      })
    })

    describe('with grow', () => {
      const parts = () => ({
        editor: document.querySelector('.cm-editor')!,
        scroller: document.querySelector('.cm-scroller')!,
        content: document.querySelector('.cm-content')!,
        host: screen.getByRole('textbox', { name: 'Code' }).closest('.cm-editor')!.parentElement!,
      })

      it('is as tall as its text, with no border and no up-and-down scroll of its own', () => {
        renderEditor({ grow: true })

        const { editor, scroller } = parts()
        expect(winningRule(editor, 'height')).toBe('auto')
        expect(winningRule(editor, 'border-width')).toBe('0px')
        expect(winningRule(scroller, 'overflow-y')).toBe('hidden')
        expect(winningRule(scroller, 'overflow')).toBeUndefined()
        expect(winningRule(scroller, 'overscroll-behavior')).not.toBe('contain')
      })

      it('keeps its height, border and scroll without grow', () => {
        renderEditor()

        const { editor, scroller } = parts()
        expect(winningRule(editor, 'height')).toBe('100%')
        expect(winningRule(editor, 'border-width')).toBe('1px')
        expect(winningRule(scroller, 'overflow')).toBe('auto')
      })

      it('has a minimum height when editable, and none when read-only', () => {
        const { rerender } = renderEditor({ grow: true })
        expect(winningRule(parts().content, 'min-height')).toBe('5lh')

        rerender({ grow: true, readOnly: true })
        expect(winningRule(parts().content, 'min-height')).not.toBe('5lh')

        rerender({ grow: true, readOnly: false })
        expect(winningRule(parts().content, 'min-height')).toBe('5lh')
      })

      it('leaves no fixed height on the host when fill is set too', () => {
        renderEditor({ grow: true, fill: true })

        expect(parts().host.style.height).toBe('')
      })
    })

    it('takes the selected tint of a page whose primary differs from the token file', () => {
      const primary = '#c41d7f'
      renderEditor(
        {},
        {
          initialMode: 'light',
          wrap: (editor) => <ConfigProvider theme={{ token: { colorPrimary: primary } }}>{editor}</ConfigProvider>,
        },
      )

      const base = antThemeFor('light')
      const pageTint = theme.getDesignToken({
        ...base,
        token: { ...base.token, colorPrimary: primary },
      }).controlItemBgActive
      const defaultTint = theme.getDesignToken(base).controlItemBgActive
      expect(asStyled(pageTint)).not.toBe(asStyled(defaultTint))
      expect(selectedMatchBackground()).toBe(asStyled(pageTint))
    })
  })

  describe('with original', () => {
    withAntdStyles()

    const { colour } = designTokensFor('light')
    const removed = changeLook('removed', colour)
    const added = changeLook('added', colour)

    /** A colour or a tint as the style sheet reads it back: jsdom writes each hex colour as rgb. */
    const asRule = (value: string) => value.replace(/#[0-9a-f]{6}/gi, asStyled)

    /** The value of a property in the last style rule whose selector ends in `ending`, which reads pseudo-elements too. */
    function ruleEndingIn(ending: string, property: string, without = /^$/) {
      const rules = Array.from(document.styleSheets).flatMap((sheet) => Array.from(sheet.cssRules))
      return rules
        .filter((rule): rule is CSSStyleRule => 'selectorText' in rule)
        .filter((rule) =>
          rule.selectorText.split(',').some((selector) => selector.trim().endsWith(ending) && !without.test(selector)),
        )
        .map((rule) => rule.style.getPropertyValue(property))
        .filter(Boolean)
        .at(-1)
    }

    const diff = (original: string, value: string, extra: Partial<CodeEditorProps> = {}, options?: RenderOptions) =>
      renderEditor({ original, value, language: 'plain', ...extra }, { initialMode: 'light', ...options })

    const changedLines = () => Array.from(document.querySelectorAll<HTMLElement>('.cm-changedLine'))
    const deletedLines = () => Array.from(document.querySelectorAll<HTMLElement>('.cm-deletedLine'))
    /** Waits for the marks that the editor puts on the removed lines and folded rows once they are drawn. */
    const drawn = () =>
      waitFor(() => expect(document.querySelector('.cm-deletedLine.cm-noChangedWords, .cm-deletedLine')).not.toBeNull())
    const numbers = (gutterClass: string) =>
      Array.from(document.querySelectorAll<HTMLElement>(`.cm-gutter.${gutterClass} .cm-gutterElement`))
        .filter((element) => element.style.visibility !== 'hidden')
        .map((element) => element.textContent)
        .filter(Boolean)

    const partly = { original: 'alpha\nprice = 10\nomega', value: 'alpha\nprice = 20\nbonus\nomega' }

    it('draws a removed line after "−" on the danger tint and an added line after "+" on the success tint', async () => {
      diff(partly.original, partly.value)
      await drawn()

      const [pricing, bonus] = changedLines()
      expect(pricing).toHaveTextContent('price = 20')
      expect(bonus).toHaveTextContent('bonus')
      expect(removed.tint).toContain('color-mix')
      expect(winningRule(pricing, 'background-color')).toBe(asRule(added.tint))
      expect(winningRule(bonus, 'background-color')).toBe(asRule(added.tint))
      expect(ruleEndingIn('.cm-changedLine::before', 'content', /removedFile/)).toBe(`"${added.mark}"`)
      expect(ruleEndingIn('.cm-changedLine::before', 'color', /removedFile/)).toBe(asStyled(added.markColour))

      const [gone] = deletedLines()
      expect(gone).toHaveTextContent('price = 10')
      expect(winningRule(gone.closest('.cm-deletedChunk')!, 'background-color')).toBe(asRule(removed.tint))
      expect(winningRule(gone.closest('.cm-deletedChunk')!, 'color')).toBe(asStyled(colour.text))
      expect(ruleEndingIn('.cm-deletedLine::before', 'content')).toBe(`"${removed.mark}"`)
      expect(ruleEndingIn('.cm-deletedLine::before', 'color')).toBe(asStyled(removed.markColour))
    })

    it('draws no accept or reject buttons and marks no current line', async () => {
      diff(partly.original, partly.value)
      await drawn()

      expect(screen.queryByRole('button', { name: /accept|reject/i })).not.toBeInTheDocument()
      expect(document.querySelector('.cm-activeLine')).toBeNull()
      expect(document.querySelector('.cm-activeLineGutter')).toBeNull()
      expect(editorView().state.readOnly).toBe(true)
    })

    it("refuses the author's edits and has no fold gutter, whatever readOnly says", async () => {
      const onChange = vi.fn()
      diff(partly.original, partly.value, { readOnly: false, onChange })
      await drawn()

      act(() => typeAtStart(editorView()))

      expect(onChange).not.toHaveBeenCalled()
      expect(document.querySelector('.cm-foldGutter')).toBeNull()
    })

    it('is as tall as its text, whatever grow says', () => {
      diff(partly.original, partly.value, { grow: false })

      const editor = document.querySelector('.cm-editor')!
      expect(winningRule(editor, 'height')).toBe('auto')
      expect(winningRule(document.querySelector('.cm-scroller')!, 'overflow-y')).toBe('hidden')
    })

    describe('the changed words', () => {
      const underline = (element: Element) => winningRule(element, 'text-decoration-line')

      it('are underlined 2 px in the mark colour in a line that changed in part, in the removed and the added line', async () => {
        diff(partly.original, partly.value)
        await drawn()

        const addedWord = document.querySelector('.cm-changedLine .cm-changedText')!
        expect(addedWord).toHaveTextContent('20')
        expect(underline(addedWord)).toBe('underline')
        expect(winningRule(addedWord, 'text-decoration-thickness')).toBe('2px')
        expect(winningRule(addedWord, 'text-decoration-color')).toBe(asStyled(added.markColour))
        const removedWord = document.querySelector('.cm-deletedLine .cm-deletedText')!
        expect(removedWord).toHaveTextContent('10')
        expect(removedWord.closest('.cm-deletedLine')).not.toHaveClass('cm-noChangedWords')
        expect(underline(removedWord)).toBe('underline')
        expect(winningRule(removedWord, 'text-decoration-color')).toBe(asStyled(removed.markColour))
      })

      it('are not underlined in a line added whole', async () => {
        diff(partly.original, partly.value)
        await drawn()

        const bonus = changedLines()[1]
        expect(bonus).toHaveClass('cm-noChangedWords')
        bonus.querySelectorAll('.cm-changedText').forEach((word) => expect(underline(word)).toBe('none'))
      })

      it('are not underlined in a line removed whole', async () => {
        diff('alpha\ngone\nomega', 'alpha\nomega')
        await drawn()

        const [gone] = deletedLines()
        expect(gone).toHaveTextContent('gone')
        expect(gone).toHaveClass('cm-noChangedWords')
        gone.querySelectorAll('.cm-deletedText').forEach((word) => expect(underline(word)).toBe('none'))
      })

      it('are not underlined in a line whose only change is its spaces', async () => {
        diff('if (x) {\nrun()\n}', 'if (x) {\n  run()\n}')
        await drawn()

        const run = changedLines().find((line) => line.textContent?.includes('run()'))!
        expect(run).toHaveClass('cm-noChangedWords')
        const spaces = run.querySelectorAll('.cm-changedText')
        expect(spaces.length).toBeGreaterThan(0)
        spaces.forEach((word) => expect(underline(word)).toBe('none'))
      })
    })

    describe('the line numbers', () => {
      it("are two columns for a changed file: the text before's, then the text after's", async () => {
        diff(partly.original, partly.value)
        await drawn()

        expect(document.querySelectorAll('.cm-gutter')).toHaveLength(2)
        expect(Array.from(document.querySelectorAll('.cm-gutter')).map((column) => column.className)).toEqual([
          expect.stringContaining('cm-beforeLineNumbers'),
          expect.stringContaining('cm-lineNumbers'),
        ])
        expect(numbers('cm-beforeLineNumbers')).toEqual(['1', '2', '3'])
        expect(numbers('cm-lineNumbers')).toEqual(['1', '2', '3', '4'])
      })

      it('are one column for a file added whole', () => {
        diff('', 'one\ntwo')

        expect(document.querySelectorAll('.cm-gutter')).toHaveLength(1)
        expect(numbers('cm-lineNumbers')).toEqual(['1', '2'])
        expect(changedLines()).toHaveLength(2)
        expect(ruleEndingIn('.cm-changedLine::before', 'content', /removedFile/)).toBe(`"${added.mark}"`)
      })

      it('are one column for a file removed whole, each line removed', () => {
        diff('one\ntwo', '')

        expect(document.querySelectorAll('.cm-gutter')).toHaveLength(1)
        expect(numbers('cm-lineNumbers')).toEqual(['1', '2'])
        const lines = changedLines()
        expect(lines).toHaveLength(2)
        expect(lines[0].closest('.cm-editor')).toHaveClass('cm-removedFile')
        expect(ruleEndingIn('.cm-removedFile .cm-changedLine', 'background-color')).toBe(asRule(removed.tint))
        expect(ruleEndingIn('.cm-removedFile .cm-changedLine::before', 'content')).toBe(`"${removed.mark}"`)
        expect(editorView().state.doc.toString()).toBe('one\ntwo')
      })

      it('make no row for the last line break of either text', async () => {
        diff('a\nb\n', 'a\nc\n')
        await drawn()

        expect(editorView().state.doc.toString()).toBe('a\nc')
        expect(numbers('cm-lineNumbers')).toEqual(['1', '2'])
        expect(numbers('cm-beforeLineNumbers')).toEqual(['1', '2'])
      })

      it('make no row for the last line break of a file added whole', () => {
        diff('', 'one\ntwo\n')

        expect(numbers('cm-lineNumbers')).toEqual(['1', '2'])
      })
    })

    describe('the unchanged lines', () => {
      const lines = (from: number, to: number) =>
        Array.from({ length: to - from + 1 }, (_, index) => `line ${from + index}`).join('\n')
      // Ten unchanged lines between two changes: 3 stay on each side and the 4 between them fold.
      const original = `first\n${lines(1, 10)}\nlast`
      const value = `FIRST\n${lines(1, 10)}\nLAST`

      it('fold to a button that shows how many, keeping 3 lines on each side of a change', async () => {
        diff(original, value)

        const button = await screen.findByRole('button', { name: 'Show 4 unchanged lines' })
        expect(button).toHaveAttribute('tabindex', '0')
        expect(screen.getByText('line 3')).toBeInTheDocument()
        expect(screen.getByText('line 8')).toBeInTheDocument()
        expect(screen.queryByText('line 4')).not.toBeInTheDocument()
        expect(screen.queryByText('line 7')).not.toBeInTheDocument()
      })

      it('show their lines after a click', async () => {
        diff(original, value)

        fireEvent.click(await screen.findByRole('button', { name: 'Show 4 unchanged lines' }))

        expect(screen.getByText('line 4')).toBeInTheDocument()
        expect(screen.getByText('line 7')).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /unchanged lines/ })).not.toBeInTheDocument()
      })

      it('show their lines after Enter', async () => {
        diff(original, value)

        fireEvent.keyDown(await screen.findByRole('button', { name: 'Show 4 unchanged lines' }), { key: 'Enter' })

        expect(screen.getByText('line 4')).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: /unchanged lines/ })).not.toBeInTheDocument()
      })

      it('show their lines after Space', async () => {
        diff(original, value)

        fireEvent.keyDown(await screen.findByRole('button', { name: 'Show 4 unchanged lines' }), { key: ' ' })

        expect(screen.getByText('line 7')).toBeInTheDocument()
      })

      it('stay folded after another key', async () => {
        diff(original, value)

        fireEvent.keyDown(await screen.findByRole('button', { name: 'Show 4 unchanged lines' }), { key: 'a' })

        expect(screen.queryByText('line 4')).not.toBeInTheDocument()
      })

      it('do not fold a run of 3 or fewer lines', async () => {
        diff(`first\n${lines(1, 6)}\nlast`, `FIRST\n${lines(1, 6)}\nLAST`)
        await drawn()

        expect(screen.queryByRole('button', { name: /unchanged lines/ })).not.toBeInTheDocument()
        expect(screen.getByText('line 4')).toBeInTheDocument()
      })

      it('read in Vietnamese in the Vietnamese texts', async () => {
        diff(original, value, {}, { texts: platformVietnamese })

        expect(await screen.findByRole('button', { name: 'Xem 4 dòng không đổi' })).toBeInTheDocument()
      })

      it('are a button in the primary text colour on the canvas, with a focus ring from the keyboard', async () => {
        diff(original, value)

        const row = await screen.findByRole('button', { name: 'Show 4 unchanged lines' })
        expect(winningRule(row, 'color')).toBe(asStyled(colour.primaryText))
        expect(winningRule(row, 'background')).toBe(asStyled(colour.canvas))
        expect(ruleEndingIn('.cm-collapsedLines:focus-visible', 'outline')).toBe(`2px solid ${colour.focusRing}`)
      })
    })

    it("shows a new text before as the merge view's own change", async () => {
      const { rerender } = diff(partly.original, partly.value)
      await drawn()

      rerender({ original: 'alpha\nprice = 15\nomega' })

      await waitFor(() => expect(deletedLines()[0]).toHaveTextContent('price = 15'))
    })

    it('shows a file that turns from changed to added whole as one column', async () => {
      const { rerender } = diff(partly.original, partly.value)
      await drawn()

      rerender({ original: '' })

      await waitFor(() => expect(document.querySelectorAll('.cm-gutter')).toHaveLength(1))
      expect(deletedLines()).toHaveLength(0)
    })
  })
})
