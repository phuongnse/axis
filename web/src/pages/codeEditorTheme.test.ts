import { highlightTree } from '@lezer/highlight'
import { tags } from '@lezer/highlight'
import { describe, expect, it } from 'vitest'
import { designTokensFor } from '../platform/designTokens'
import { codeHighlightStyle } from './codeEditorTheme'
import { languageSupport, type FileLanguage } from './fileLanguage'

const style = codeHighlightStyle(designTokensFor('light').colour)

/** The class that the highlight style gives the first token that covers `word` in `text`. */
function classOf(language: FileLanguage, text: string, word: string): string | null {
  const tree = languageSupport(language)!.language.parser.parse(text)
  const from = text.indexOf(word)
  let found: string | null = null
  highlightTree(tree, style, (start, end, classes) => {
    if (start <= from && from < end && found === null) {
      found = classes
    }
  })
  return found
}

describe('codeHighlightStyle', () => {
  const keyword = style.style([tags.keyword])
  const string = style.style([tags.string])
  const number = style.style([tags.number])

  it.each<[FileLanguage, string, string]>([
    ['csharp', 'class A { string s = "text"; int n = 42; }', 'class'],
    ['typescript', 'const s: string = "text"; const n = 42', 'const'],
    ['javascript', 'const s = "text"; const n = 42', 'const'],
    ['python', 's = "text"\nn = 42\nif n:\n    pass', 'if'],
    // JSON has no keywords, so `true` stands for them: it shares the keyword's colour.
    ['json', '{ "s": "text", "n": 42, "b": true }', 'true'],
  ])('colours a keyword, a string and a number in %s', (language, text, word) => {
    expect(classOf(language, text, word)).toBe(keyword)
    expect(classOf(language, text, '"text"')).toBe(string)
    expect(classOf(language, text, '42')).toBe(number)
  })

  it('gives comments and type names their own colours', () => {
    expect(style.style([tags.comment])).not.toBe(keyword)
    expect(classOf('typescript', '// note\nlet x: Date', '// note')).toBe(style.style([tags.comment]))
    expect(classOf('csharp', 'class A { Guid id; }', 'Guid')).toBe(style.style([tags.typeName]))
  })
})
