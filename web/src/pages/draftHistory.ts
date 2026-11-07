import { getAt } from '../platform/jsonPatch'

/** The draft and the text of the JSON view at one moment. `jsonText` is `null` while the view shows the draft. */
export interface DraftSnapshot {
  draft: Record<string, unknown>
  jsonText: string | null
}

/**
 * The steps that can be undone and redone, oldest first in `past`. `step` names the typing step that is still open,
 * so the next edit of the same key joins it, and it is `null` when the next edit starts a new step.
 */
export interface DraftHistory {
  past: DraftSnapshot[]
  future: DraftSnapshot[]
  step: string | null
}

/** The most steps the history keeps. The oldest step is dropped past this. */
export const historyLimit = 100

/** A history with nothing to undo or redo. */
export const emptyHistory: DraftHistory = { past: [], future: [], step: null }

/**
 * Records `before` as a new step, unless `step` is not `null` and equals the open step, which the edit then joins.
 * A new edit drops the steps that could have been redone. A `before` that is the last step's draft and JSON text is not
 * saved, because the same event edited the draft twice. The oldest step is dropped past `historyLimit`.
 */
export function recordStep(history: DraftHistory, before: DraftSnapshot, step: string | null): DraftHistory {
  const joins = step !== null && step === history.step
  const last = history.past.at(-1)
  const repeats = before.draft === last?.draft && before.jsonText === last.jsonText
  const past = joins || repeats ? history.past : [...history.past, before].slice(-historyLimit)
  return { past, future: [], step }
}

/** Closes the open typing step, so the next edit starts a new step. */
export function endStep(history: DraftHistory): DraftHistory {
  return history.step === null ? history : { ...history, step: null }
}

/** Steps back from `current`. It returns the snapshot to restore, or `null` when there is nothing to undo. */
export function undoStep(
  history: DraftHistory,
  current: DraftSnapshot,
): { history: DraftHistory; snapshot: DraftSnapshot } | null {
  const snapshot = history.past.at(-1)
  if (!snapshot) {
    return null
  }
  return {
    history: { past: history.past.slice(0, -1), future: [...history.future, current], step: null },
    snapshot,
  }
}

/** Steps forward from `current`. It returns the snapshot to restore, or `null` when there is nothing to redo. */
export function redoStep(
  history: DraftHistory,
  current: DraftSnapshot,
): { history: DraftHistory; snapshot: DraftSnapshot } | null {
  const snapshot = history.future.at(-1)
  if (!snapshot) {
    return null
  }
  return {
    history: { past: [...history.past, current], future: history.future.slice(0, -1), step: null },
    snapshot,
  }
}

/**
 * The item that setting `pointer` to `value` removes from `before`, or `null` when it removes none. An item is removed
 * when `value` is `undefined` and the pointer names a held index of an array, or when the list at the pointer
 * becomes one item shorter. In the shorter list it is the first item that differs by reference, or else the last.
 * A shorter list whose items all live on, such as a widget dragged into a section, is a move and removes nothing: the
 * new value holds as many values, at any depth, as the old one.
 */
export function removedItem(before: unknown, pointer: string, value: unknown): { item: unknown } | null {
  const old = getAt(before, pointer)
  if (value === undefined) {
    const parent = getAt(before, pointer.slice(0, pointer.lastIndexOf('/')))
    const index = Number(pointer.slice(pointer.lastIndexOf('/') + 1))
    return Array.isArray(parent) && Number.isInteger(index) && index >= 0 && index < parent.length
      ? { item: parent[index] }
      : null
  }
  if (Array.isArray(old) && Array.isArray(value) && value.length === old.length - 1) {
    const at = old.findIndex((item, index) => index >= value.length || item !== value[index])
    return nodeCount(value) < nodeCount(old) ? { item: old[at === -1 ? old.length - 1 : at] } : null
  }
  return null
}

/** The number of values in `value`, itself and every value nested in it. */
function nodeCount(value: unknown): number {
  const children = Array.isArray(value)
    ? value
    : value !== null && typeof value === 'object'
      ? Object.values(value)
      : []
  return children.reduce<number>((count, child) => count + nodeCount(child), 1)
}
