import { EditorView } from '@codemirror/view'
import { act, screen } from '@testing-library/react'

/** The CodeMirror view of the JSON tab. It loads lazily, so the call waits for it. */
export async function jsonView(): Promise<EditorView> {
  return EditorView.findFromDOM(await screen.findByRole('textbox', { name: 'JSON' }, { timeout: 5000 }))!
}

/** The text in the JSON tab. */
export async function jsonText(): Promise<string> {
  return (await jsonView()).state.doc.toString()
}

/** Replaces the text in the JSON tab as an author's edit would. */
export async function editJson(text: string): Promise<void> {
  const view = await jsonView()
  view.focus()
  act(() => {
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } })
  })
}
