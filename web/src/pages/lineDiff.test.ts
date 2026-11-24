import { describe, expect, it } from 'vitest'
import { lineCounts } from './lineDiff'

describe('lineCounts', () => {
  it('counts the lines removed and added in the middle of a text', () => {
    const original = 'keep\nold1\nold2\nold3\nend\n'
    const value = 'keep\nnew1\nnew2\nnew3\nnew4\nnew5\nnew6\nnew7\nnew8\nend\n'

    expect(lineCounts(original, value)).toEqual({ added: 8, removed: 3 })
  })

  it('counts a change on the last line the same with and without a final line break', () => {
    expect(lineCounts('one\ntwo\n', 'one\nthree\n')).toEqual({ added: 1, removed: 1 })
    expect(lineCounts('one\ntwo', 'one\nthree')).toEqual({ added: 1, removed: 1 })
    expect(lineCounts('one\ntwo\n', 'one\nthree')).toEqual({ added: 1, removed: 1 })
    expect(lineCounts('one\ntwo', 'one\nthree\nfour\n')).toEqual({ added: 2, removed: 1 })
  })

  it('counts nothing for a text that did not change', () => {
    expect(lineCounts('one\ntwo\n', 'one\ntwo\n')).toEqual({ added: 0, removed: 0 })
  })

  it('counts each line of a file added whole, and none removed', () => {
    expect(lineCounts('', 'a\nb\nc\n')).toEqual({ added: 3, removed: 0 })
    expect(lineCounts('', 'a\nb\nc')).toEqual({ added: 3, removed: 0 })
  })

  it('counts each line of a file removed whole, and none added', () => {
    expect(lineCounts('a\nb\n', '')).toEqual({ added: 0, removed: 2 })
    expect(lineCounts('a\nb', '')).toEqual({ added: 0, removed: 2 })
  })
})
