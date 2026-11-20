import { foldable } from '@codemirror/language'
import { EditorState } from '@codemirror/state'
import { describe, expect, it } from 'vitest'
import { fileLanguage, foldByIndent, languageSupport } from './fileLanguage'

describe('fileLanguage', () => {
  it.each([
    ['Adapter.cs', 'csharp'],
    ['src/index.ts', 'typescript'],
    ['main.js', 'javascript'],
    ['main.mjs', 'javascript'],
    ['main.cjs', 'javascript'],
    ['tools/run.py', 'python'],
    ['axis-package.json', 'json'],
    ['X.CS', 'csharp'],
  ])('maps %s to %s', (path, language) => {
    expect(fileLanguage(path)).toBe(language)
  })

  it.each(['README.md', 'README', 'notes.txt', 'folder.cs/README', '.gitignore.'])('shows %s as plain text', (path) => {
    expect(fileLanguage(path)).toBe('plain')
  })

  it('has no language support for plain text', () => {
    expect(languageSupport('plain')).toBeNull()
  })
})

describe('folding by indentation', () => {
  const csharp = ['namespace A {', '    class B {', '        int x;', '', '        int y;', '    }', '}'].join('\n')
  const state = (doc: string, language: Parameters<typeof languageSupport>[0] = 'csharp') =>
    EditorState.create({ doc, extensions: languageSupport(language)! })

  it('folds a C# line over the lines after it that are indented deeper', () => {
    const editor = state(csharp)
    const line = editor.doc.line(2)

    expect(foldable(editor, line.from, line.to)).toEqual({ from: line.to, to: editor.doc.line(5).to })
  })

  it('folds nothing when no deeper line follows', () => {
    const editor = state(csharp)
    const line = editor.doc.line(3)

    expect(foldable(editor, line.from, line.to)).toBeNull()
  })

  it('folds a JSON object over its members', () => {
    const editor = state('{\n  "a": {\n    "b": 1\n  }\n}', 'json')
    const line = editor.doc.line(2)

    expect(foldable(editor, line.from, line.to)).toEqual({ from: line.to, to: editor.doc.line(3).to })
  })

  it('ignores a blank line when it measures the indent', () => {
    const editor = EditorState.create({ doc: 'a\n\n  b' })

    expect(foldByIndent(editor, 0, 1)).toEqual({ from: 1, to: 6 })
  })
})
