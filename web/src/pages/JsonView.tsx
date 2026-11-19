import { linter, lintGutter } from '@codemirror/lint'
import type { Extension } from '@codemirror/state'
import type { ValidateFunction } from 'ajv'
import { useMemo, type Ref, type RefObject } from 'react'
import { loadKindValidator } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import CodeEditor, { type CodeEditorHandle } from './CodeEditor'
import type { FindTexts } from './jsonFindPanel'
import { jsonLanguage } from './jsonLanguage'
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
  /** The texts of the find bar. */
  findTexts: FindTexts
  /** The resource kind whose schema the content is checked against. Without it, only the syntax is checked. */
  kind?: string
  /** Called with the number of problems that the checks mark, each time it changes. */
  onProblems?: (count: number) => void
  ref?: Ref<JsonViewHandle>
}

/** What the owner of a JSON view can ask it to do. */
export type JsonViewHandle = CodeEditorHandle

/** How long the checks wait after the last keystroke. */
const checkDelay = 250

/** A code editor for a resource's JSON, checked against the kind's schema as the author types. */
export default function JsonView({
  label,
  value,
  onChange,
  boundary,
  findTexts,
  kind,
  onProblems,
  ref,
}: JsonViewProps) {
  const t = useText()
  const checks = useMemo(() => kindChecks(kind, t), [kind, t])

  return (
    <CodeEditor
      ref={ref}
      label={label}
      value={value}
      onChange={onChange}
      language={jsonLanguage}
      checks={checks}
      boundary={boundary}
      findTexts={findTexts}
      onProblems={onProblems}
    />
  )
}

/** The checks of a JSON view: the syntax, and the schema of `kind` when there is one, loaded at the first check. */
function kindChecks(kind: string | undefined, t: (key: string) => string): Extension {
  let loaded: Promise<ValidateFunction | undefined> | null = null
  return [
    lintGutter(),
    linter(
      async (editor) => {
        loaded ??= kind === undefined ? Promise.resolve(undefined) : loadKindValidator(kind)
        return jsonProblems(editor.state.doc.toString(), await loaded, t)
      },
      { delay: checkDelay },
    ),
  ]
}
