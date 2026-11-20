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

/** The name of each language the editor highlights, as the status line says it. */
const languageNames: Record<string, string> = {
  csharp: 'C#',
  typescript: 'TypeScript',
  tsx: 'TypeScript',
  javascript: 'JavaScript',
  python: 'Python',
  json: 'JSON',
}

/**
 * Proposed, see #3978: the name of the language a file's path says, from the same table that picks its highlighting,
 * or `null` for plain text.
 */
export function fileLanguageName(path: string): string | null {
  const language = byExtension[path.slice(path.lastIndexOf('.') + 1).toLowerCase()]
  return language === undefined ? null : (languageNames[language] ?? null)
}
