import { describe, expect, it } from 'vitest'
import {
  emptyHistory,
  endStep,
  historyLimit,
  recordStep,
  redoStep,
  removedItem,
  undoStep,
  type DraftSnapshot,
  type LabelTextDrafts,
} from './draftHistory'

const noTexts: LabelTextDrafts = {}

function snapshot(n: number): DraftSnapshot {
  return { draft: { n }, jsonText: null, texts: noTexts }
}

describe('draftHistory', () => {
  it('records one step for edits that share a step key', () => {
    let history = recordStep(emptyHistory, snapshot(0), '/name')
    history = recordStep(history, snapshot(1), '/name')
    history = recordStep(history, snapshot(2), '/name')

    expect(history.past).toEqual([snapshot(0)])
  })

  it('records a step for every edit with no step key', () => {
    let history = recordStep(emptyHistory, snapshot(0), null)
    history = recordStep(history, snapshot(1), null)

    expect(history.past).toEqual([snapshot(0), snapshot(1)])
  })

  it('records separate steps for different keys', () => {
    let history = recordStep(emptyHistory, snapshot(0), '/name')
    history = recordStep(history, snapshot(1), '/title')

    expect(history.past).toHaveLength(2)
  })

  it('splits steps when the open step ends', () => {
    let history = recordStep(emptyHistory, snapshot(0), '/name')
    history = endStep(history)
    history = recordStep(history, snapshot(1), '/name')

    expect(history.past).toEqual([snapshot(0), snapshot(1)])
  })

  it('undoes and redoes a step', () => {
    const history = recordStep(emptyHistory, snapshot(0), null)

    const undone = undoStep(history, snapshot(1))!
    expect(undone.snapshot).toEqual(snapshot(0))
    expect(undone.history.past).toEqual([])
    expect(undone.history.step).toBeNull()

    const redone = redoStep(undone.history, snapshot(0))!
    expect(redone.snapshot).toEqual(snapshot(1))
    expect(redone.history.past).toEqual([snapshot(0)])
    expect(redone.history.future).toEqual([])
  })

  it('has nothing to undo or redo when the history is empty', () => {
    expect(undoStep(emptyHistory, snapshot(0))).toBeNull()
    expect(redoStep(emptyHistory, snapshot(0))).toBeNull()
  })

  it('drops the steps that could be redone when a new edit comes', () => {
    const history = recordStep(emptyHistory, snapshot(0), null)
    const undone = undoStep(history, snapshot(1))!

    const edited = recordStep(undone.history, snapshot(0), null)

    expect(edited.future).toEqual([])
    expect(redoStep(edited, snapshot(2))).toBeNull()
  })

  it('keeps the last 100 steps', () => {
    let history = emptyHistory
    for (let n = 0; n <= historyLimit; n++) {
      history = recordStep(history, snapshot(n), null)
    }
    expect(history.past).toHaveLength(historyLimit)

    let current = snapshot(historyLimit + 1)
    for (let undone = 0; undone < historyLimit; undone++) {
      const result = undoStep(history, current)!
      history = result.history
      current = result.snapshot
    }

    expect(current).toEqual(snapshot(1))
    expect(undoStep(history, current)).toBeNull()
  })

  it('skips a step whose draft is the draft of the last step', () => {
    const before = snapshot(0)
    let history = recordStep(emptyHistory, before, null)
    history = recordStep(history, { draft: before.draft, jsonText: null, texts: noTexts }, null)

    expect(history.past).toEqual([before])
  })

  it('keeps a step whose draft is unchanged but whose label texts changed', () => {
    const before = snapshot(0)
    let history = recordStep(emptyHistory, before, null)
    const typed = { draft: before.draft, jsonText: null, texts: { 'pages.a.title': { en: 'Title' } } }
    history = recordStep(history, typed, null)

    expect(history.past).toEqual([before, typed])
  })
})

describe('removedItem', () => {
  const a = { name: 'a' }
  const b = { name: 'b' }
  const c = { name: 'c' }
  const before = { rows: [a, b, c], title: 'T' }

  it('returns the item of a list that is one item shorter, the first that differs', () => {
    expect(removedItem(before, '/rows', [a, c])).toEqual({ item: b })
    expect(removedItem(before, '/rows', [b, c])).toEqual({ item: a })
  })

  it('returns the last item when the shorter list keeps the others', () => {
    expect(removedItem(before, '/rows', [a, b])).toEqual({ item: c })
  })

  it('returns the item of an array index that is cleared', () => {
    expect(removedItem(before, '/rows/1', undefined)).toEqual({ item: b })
  })

  it('returns null when the item moves into another item of the list', () => {
    const section = { name: 's', widgets: [] as unknown[] }
    const rows = { rows: [a, section] }
    const moved = [{ ...section, widgets: [structuredClone(a)] }]
    expect(removedItem(rows, '/rows', moved)).toBeNull()
  })

  it('returns the item when an equal copy of it remains elsewhere', () => {
    const copy = { name: 'a' }
    const rows = { rows: [a, copy] }
    expect(removedItem(rows, '/rows', [copy])).toEqual({ item: a })
  })

  it('returns null for an edit that removes nothing', () => {
    expect(removedItem(before, '/rows', [a, b, c, a])).toBeNull()
    expect(removedItem(before, '/rows', [a])).toBeNull()
    expect(removedItem(before, '/rows/5', undefined)).toBeNull()
    expect(removedItem(before, '/title', undefined)).toBeNull()
    expect(removedItem(before, '/title', 'U')).toBeNull()
  })
})
