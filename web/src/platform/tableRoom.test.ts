import { describe, expect, it } from 'vitest'
import { makeRoom, type RoomColumn } from './tableRoom'

const column = (key: string, priority?: RoomColumn['priority'], width = 100): RoomColumn => ({ key, width, priority })

describe('makeRoom', () => {
  it('moves low columns first, then the unmarked ones from the right, then high ones, until the rest fit', () => {
    const columns = [
      column('id'),
      column('a', 'high'),
      column('b'),
      column('c', 'low'),
      column('d'),
      column('e', 'low'),
    ]

    expect(makeRoom(300, 0, columns)).toEqual({ moved: ['e', 'c', 'd'], scrolls: false })
    expect(makeRoom(250, 0, columns)).toEqual({ moved: ['e', 'c', 'd', 'b'], scrolls: false })
    expect(makeRoom(150, 0, columns)).toEqual({ moved: ['e', 'c', 'd', 'b', 'a'], scrolls: false })
  })

  it('counts the width of what is not a data column', () => {
    const columns = [column('id'), column('a'), column('b')]

    expect(makeRoom(250, 40, columns)).toEqual({ moved: ['b'], scrolls: false })
  })

  it('never moves the first column and reports that the table scrolls when the rest still do not fit', () => {
    const columns = [column('id', 'low', 400), column('a'), column('b')]

    expect(makeRoom(300, 0, columns)).toEqual({ moved: ['b', 'a'], scrolls: true })
  })

  it('moves none when the columns fit', () => {
    const columns = [column('id'), column('a'), column('b')]

    expect(makeRoom(300, 0, columns)).toEqual({ moved: [], scrolls: false })
  })

  it('moves none and does not scroll before the table has a width', () => {
    const columns = [column('id'), column('a'), column('b')]

    expect(makeRoom(0, 0, columns)).toEqual({ moved: [], scrolls: false })
  })
})
