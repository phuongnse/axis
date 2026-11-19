import { javascript } from '@codemirror/lang-javascript'
import { python } from '@codemirror/lang-python'
import { StreamLanguage } from '@codemirror/language'
import { csharp } from '@codemirror/legacy-modes/mode/clike'
import type { Extension } from '@codemirror/state'
import { jsonLanguage } from './jsonLanguage'

// Proposed: each language's highlighting in a package file, see #3978 and docs/reference/studio-packages.md#a-packages-files.
const languages: Record<string, Extension> = {
  csharp: StreamLanguage.define(csharp),
  typescript: javascript({ typescript: true }),
  tsx: javascript({ typescript: true, jsx: true }),
  javascript: javascript(),
  python: python(),
  json: jsonLanguage,
}

const byExtension: Record<string, string> = {
  cs: 'csharp',
  ts: 'typescript',
  mts: 'typescript',
  cts: 'typescript',
  tsx: 'tsx',
  js: 'javascript',
  mjs: 'javascript',
  cjs: 'javascript',
  py: 'python',
  json: 'json',
}

const plainText: Extension = []

/**
 * The highlighting of a file by its path's extension: C#, TypeScript, JavaScript, Python or JSON. Any other file, such
 * as `requirements.txt` or a project file, is plain text. The same path always gives the same extension, so the editor
 * keeps it.
 */
export function codeLanguage(path: string): Extension {
  const extension = path.slice(path.lastIndexOf('.') + 1).toLowerCase()
  return languages[byExtension[extension] ?? ''] ?? plainText
}
