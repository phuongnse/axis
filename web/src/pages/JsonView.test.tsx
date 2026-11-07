import { undo } from '@codemirror/commands'
import { EditorView } from '@codemirror/view'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { createRef } from 'react'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import JsonView, { type JsonViewHandle } from './JsonView'
import { jsonViewHeight, minHeight } from './jsonViewHeight'

const findTexts = { field: 'Find in JSON', count: '{current} of {count}', none: 'No matches' }

function renderView(value: string, onChange = vi.fn()) {
  const handle = createRef<JsonViewHandle>()
  const view = (text: string) => (
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <JsonView label="JSON" value={text} onChange={onChange} findTexts={findTexts} ref={handle} />
      </TextProvider>
    </ThemeModeProvider>
  )
  const { rerender } = render(view(value))
  return { onChange, handle, setValue: (text: string) => rerender(view(text)) }
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

describe('JsonView find bar', () => {
  const text = '{\n  "a": "item",\n  "b": "item",\n  "c": "item"\n}'

  function openFind(cursor = 0) {
    const rendered = renderView(text)
    const view = editorView()
    act(() => view.dispatch({ selection: { anchor: cursor } }))
    fireEvent.keyDown(view.contentDOM, { key: 'f', ctrlKey: true })
    return { ...rendered, view }
  }

  const field = () => screen.getByRole('textbox', { name: 'Find in JSON' })
  const count = () => screen.getByRole('status')

  function search(query: string) {
    fireEvent.input(field(), { target: { value: query } })
  }

  it('counts matches and moves between them with Enter and Shift+Enter', () => {
    openFind()

    search('item')
    expect(count()).toHaveTextContent('1 of 3')

    fireEvent.keyDown(field(), { key: 'Enter' })
    expect(count()).toHaveTextContent('2 of 3')

    fireEvent.keyDown(field(), { key: 'Enter', shiftKey: true })
    expect(count()).toHaveTextContent('1 of 3')
  })

  it('wraps from the last match to the first', () => {
    openFind()
    search('item')

    fireEvent.keyDown(field(), { key: 'Enter' })
    fireEvent.keyDown(field(), { key: 'Enter' })
    fireEvent.keyDown(field(), { key: 'Enter' })

    expect(count()).toHaveTextContent('1 of 3')
  })

  it('selects the first match after the cursor where it opened', () => {
    const { view } = openFind(text.indexOf('item') + 1)

    search('item')

    const second = text.indexOf('item', text.indexOf('item') + 1)
    expect(view.state.selection.main.from).toBe(second)
    expect(count()).toHaveTextContent('1 of 3')
    fireEvent.keyDown(field(), { key: 'Enter' })
    expect(count()).toHaveTextContent('2 of 3')
  })

  it('says when nothing matches and shows nothing for an empty query', () => {
    openFind()

    search('zzz')
    expect(count()).toHaveTextContent('No matches')

    search('')
    expect(count()).toHaveTextContent('')
    expect(count()).not.toHaveTextContent('No matches')
  })

  it('closes on Escape and puts focus back in the text', () => {
    const { view } = openFind()
    field().focus()

    fireEvent.keyDown(field(), { key: 'Escape' })

    expect(screen.queryByRole('textbox', { name: 'Find in JSON' })).not.toBeInTheDocument()
    expect(view.contentDOM.contains(document.activeElement)).toBe(true)
  })

  it('opens from the handle', () => {
    const { handle } = renderView(text)

    act(() => handle.current!.openFind())

    expect(field()).toBeInTheDocument()
  })
})

describe('JsonView handle', () => {
  it('replaces the text as an edit that is reported once', () => {
    const { onChange, handle } = renderView('{}')

    act(() => handle.current!.replace('{\n  "a": 1\n}'))

    expect(editorView().state.doc.toString()).toBe('{\n  "a": 1\n}')
    expect(onChange).toHaveBeenCalledExactlyOnceWith('{\n  "a": 1\n}')
  })

  it('reports nothing when the text is the same', () => {
    const { onChange, handle } = renderView('{}')

    act(() => handle.current!.replace('{}'))

    expect(onChange).not.toHaveBeenCalled()
  })

  it('lets the author undo a replacement', () => {
    const { handle } = renderView('{}')
    act(() => handle.current!.replace('{ "a": 1 }'))

    const view = editorView()
    expect(undo(view)).toBe(true)
    expect(view.state.doc.toString()).toBe('{}')
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
