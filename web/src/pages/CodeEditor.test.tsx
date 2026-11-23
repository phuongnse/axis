import { foldedRanges } from '@codemirror/language'
import { EditorView } from '@codemirror/view'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { ConfigProvider, theme } from 'antd'
import { createRef, type ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { antThemeFor } from '../platform/antTheme'
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
}

function renderEditor(props: Partial<CodeEditorProps> = {}, { initialMode, wrap }: RenderOptions = {}) {
  const handle = createRef<CodeEditorHandle>()
  const view = (extra: Partial<CodeEditorProps>) => {
    const editor = (
      <CodeEditor label="Code" value={'one\ntwo\nthree'} language="plain" ref={handle} {...props} {...extra} />
    )
    return (
      <ThemeModeProvider initialMode={initialMode}>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <footer />
          {wrap ? wrap(editor) : editor}
        </TextProvider>
      </ThemeModeProvider>
    )
  }
  const { rerender } = render(view({}))
  return { handle, rerender: (extra: Partial<CodeEditorProps>) => rerender(view(extra)) }
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
})
