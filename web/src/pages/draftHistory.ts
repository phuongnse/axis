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
