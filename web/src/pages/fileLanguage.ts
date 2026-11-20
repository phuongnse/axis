import { javascript } from '@codemirror/lang-javascript'
import { python } from '@codemirror/lang-python'
import { foldService, getIndentUnit, LanguageSupport, StreamLanguage } from '@codemirror/language'
import { csharp } from '@codemirror/legacy-modes/mode/clike'
import { countColumn, type EditorState } from '@codemirror/state'
import { jsonLanguage } from './jsonLanguage'

/** The languages a code editor highlights. Any other file is `plain`. */
export type FileLanguage = 'csharp' | 'typescript' | 'javascript' | 'python' | 'json' | 'plain'

const byExtension: Record<string, FileLanguage> = {
  cs: 'csharp',
  ts: 'typescript',
  js: 'javascript',
  mjs: 'javascript',
  cjs: 'javascript',
  py: 'python',
  json: 'json',
}

/** The language of a file, from the extension of the last segment of its path, see docs/reference/studio-packages.md#a-packages-files. */
export function fileLanguage(path: string): FileLanguage {
  const name = path.slice(path.lastIndexOf('/') + 1)
  const dot = name.lastIndexOf('.')
  return dot < 0 ? 'plain' : (byExtension[name.slice(dot + 1).toLowerCase()] ?? 'plain')
}

/**
 * Folds a line over the lines after it that are indented deeper, skipping blank lines. It serves a language without a
 * syntax tree, see docs/reference/studio-packages.md#a-packages-files.
 */
export function foldByIndent(
  state: EditorState,
  lineStart: number,
  lineEnd: number,
): { from: number; to: number } | null {
  const doc = state.doc
  const unit = getIndentUnit(state)
  const indentOf = (text: string) => countColumn(text.slice(0, text.length - text.trimStart().length), unit)
  const start = doc.lineAt(lineStart)
  if (start.text.trim() === '') {
    return null
  }
  const indent = indentOf(start.text)
  let last = -1
  for (let number = start.number + 1; number <= doc.lines; number++) {
    const line = doc.line(number)
    if (line.text.trim() === '') {
      continue
    }
    if (indentOf(line.text) <= indent) {
      break
    }
    last = line.to
  }
  return last < 0 ? null : { from: lineEnd, to: last }
}

/** The fold service of a language without a syntax tree, and of plain text. */
export const indentFolding = foldService.of(foldByIndent)
const supports = new Map<FileLanguage, LanguageSupport | null>()

function build(language: FileLanguage): LanguageSupport | null {
  switch (language) {
    case 'typescript':
      return javascript({ typescript: true })
    case 'javascript':
      return javascript()
    case 'python':
      return python()
    case 'csharp':
      return new LanguageSupport(StreamLanguage.define(csharp), [indentFolding])
    case 'json':
      return new LanguageSupport(jsonLanguage, [indentFolding])
    case 'plain':
      return null
  }
}

/** The highlighting, folding and indentation of a language, or `null` for plain text. It is built once per language. */
export function languageSupport(language: FileLanguage): LanguageSupport | null {
  if (!supports.has(language)) {
    supports.set(language, build(language))
  }
  return supports.get(language)!
}
