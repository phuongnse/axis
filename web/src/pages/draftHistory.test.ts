import { describe, expect, it } from 'vitest'
import { emptyHistory, endStep, historyLimit, recordStep, redoStep, undoStep, type DraftSnapshot } from './draftHistory'

function snapshot(n: number): DraftSnapshot {
  return { draft: { n }, jsonText: null }
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
    history = recordStep(history, { draft: before.draft, jsonText: null }, null)

    expect(history.past).toEqual([before])
  })
})
