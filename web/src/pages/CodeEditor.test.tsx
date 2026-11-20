import { EditorView } from '@codemirror/view'
import { act, render, screen } from '@testing-library/react'
import { createRef } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import CodeEditor, { type CodeEditorHandle, type CodeEditorProps } from './CodeEditor'
import { jsonViewHeight } from './jsonViewHeight'

const findTexts = { field: 'Find in code', count: '{current} of {count}', none: 'No matches' }

function renderEditor(props: Partial<CodeEditorProps> = {}) {
  const handle = createRef<CodeEditorHandle>()
  const view = (extra: Partial<CodeEditorProps>) => (
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <footer />
        <CodeEditor
          label="Code"
          value={'one\ntwo\nthree'}
          language="plain"
          findTexts={findTexts}
          ref={handle}
          {...props}
          {...extra}
        />
      </TextProvider>
    </ThemeModeProvider>
  )
  const { rerender } = render(view({}))
  return { handle, rerender: (extra: Partial<CodeEditorProps>) => rerender(view(extra)) }
}

const editorView = () => EditorView.findFromDOM(screen.getByRole('textbox', { name: 'Code' }))!
const typeAtStart = (view: EditorView) => view.dispatch({ changes: { from: 0, insert: 'x' }, userEvent: 'input.type' })

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
})
