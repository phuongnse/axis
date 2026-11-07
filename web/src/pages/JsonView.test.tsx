import { undo } from '@codemirror/commands'
import { EditorView } from '@codemirror/view'
import { act, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import JsonView from './JsonView'
import { jsonViewHeight, minHeight } from './jsonViewHeight'

function renderView(value: string, onChange = vi.fn()) {
  const view = (text: string) => (
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <JsonView label="JSON" value={text} onChange={onChange} />
      </TextProvider>
    </ThemeModeProvider>
  )
  const { rerender } = render(view(value))
  return { onChange, setValue: (text: string) => rerender(view(text)) }
}

function editorView(): EditorView {
  return EditorView.findFromDOM(screen.getByRole('textbox', { name: 'JSON' }))!
}

describe('JsonView', () => {
  it('names its content and shows line numbers', () => {
    renderView('{\n  "a": 1\n}')

    expect(screen.getByRole('textbox', { name: 'JSON' })).toBeInTheDocument()
    expect(document.querySelector('.cm-lineNumbers')).not.toBeNull()
  })

  it('reports an edit', () => {
    const { onChange } = renderView('{}')
    const view = editorView()

    act(() => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: '{ "a": 1 }' } }))

    expect(onChange).toHaveBeenCalledExactlyOnceWith('{ "a": 1 }')
  })

  it('replaces the text with a new value without reporting it', () => {
    const { onChange, setValue } = renderView('{}')

    setValue('{ "b": 2 }')

    expect(editorView().state.doc.toString()).toBe('{ "b": 2 }')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('keeps a value that came from outside out of its own undo history', () => {
    const { setValue } = renderView('{}')
    setValue('{ "b": 2 }')

    const view = editorView()
    expect(undo(view)).toBe(false)
    expect(view.state.doc.toString()).toBe('{ "b": 2 }')
  })
})

describe('jsonViewHeight', () => {
  it('is the window height minus the footer, the gap, the top and the space below', () => {
    expect(jsonViewHeight({ windowHeight: 1000, top: 200, footerHeight: 48, below: 90, gap: 16 })).toBe(646)
  })

  it('does not go under the least height in a tiny window', () => {
    expect(jsonViewHeight({ windowHeight: 300, top: 200, footerHeight: 48, below: 90, gap: 16 })).toBe(minHeight)
  })
})
