import type { ValidateFunction } from 'ajv'
import { useEffect, useRef, type Ref, type RefObject } from 'react'
import { loadKindValidator } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import CodeEditor, { type CodeEditorHandle } from './CodeEditor'
import { jsonProblems } from './jsonProblems'

export interface JsonViewProps {
  /** The accessible name of the editor. */
  label: string
  value: string
  onChange: (text: string) => void
  /**
   * The element whose bottom edge ends the content that follows the view, such as the resource editor's root. When
   * absent, nothing follows the view.
   */
  boundary?: RefObject<HTMLElement | null>
  /** The resource kind whose schema the content is checked against. Without it, only the syntax is checked. */
  kind?: string
  /** Called with the number of problems that the checks mark, each time it changes. */
  onProblems?: (count: number) => void
  ref?: Ref<JsonViewHandle>
}

/** What the owner of a JSON view can ask it to do. */
export type JsonViewHandle = CodeEditorHandle

/** A code editor for a resource's JSON that fills the window down to the shell footer and checks the text as it is typed. */
export default function JsonView({ label, value, onChange, boundary, kind, onProblems, ref }: JsonViewProps) {
  const t = useText()
  const latest = useRef({ kind, t })
  useEffect(() => {
    latest.current = { kind, t }
  })
  const validator = useRef<{ kind: string | undefined; loaded: Promise<ValidateFunction | undefined> }>(null)

  return (
    <CodeEditor
      label={label}
      value={value}
      onChange={onChange}
      language="json"
      fill
      boundary={boundary}
      onProblems={onProblems}
      lint={async (editor) => {
        const current = latest.current
        const cached =
          validator.current && validator.current.kind === current.kind
            ? validator.current
            : {
                kind: current.kind,
                loaded: current.kind === undefined ? Promise.resolve(undefined) : loadKindValidator(current.kind),
              }
        validator.current = cached
        return jsonProblems(editor.state.doc.toString(), await cached.loaded, current.t)
      }}
      ref={ref}
    />
  )
}
