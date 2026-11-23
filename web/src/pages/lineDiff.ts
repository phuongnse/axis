import { getChunks, getOriginalDoc, unifiedMergeView, type Chunk } from '@codemirror/merge'
import { Prec, RangeSetBuilder, type EditorState, type Extension } from '@codemirror/state'
import {
  Decoration,
  EditorView,
  gutter,
  GutterMarker,
  ViewPlugin,
  type DecorationSet,
  type ViewUpdate,
} from '@codemirror/view'

/**
 * Proposed: a code editor's line diff, see #4623 and docs/reference/studio-packages.md#diffs. The editor's text is the
 * text after the change, and `original` the text before. It draws no accept or reject buttons and no change gutter, as
 * the "−" and "+" marks and the tints of the editor's theme show each change. Two columns number the lines: the text
 * before's numbers, then the text after's, and a removed line has only the first, an added line only the second. The
 * changed words are underlined only in a line that changed in part: a line added or removed whole is not. A file added
 * or removed whole, whose `original` is empty, is drawn as its lines, each marked, with no diff and one column of
 * numbers: a removed file is its own text, each line drawn removed.
 */
export function lineDiff(original: string, removed: boolean): Extension {
  return [flush, original === '' ? wholeFile(removed) : changedFile(original)]
}

/**
 * Proposed: a diff fills its file's block flush, as #3263 agreed, with no frame or corners of its own, and keyboard
 * focus stays visible inside the block's edge, see #4623.
 */
const flush = Prec.high(
  EditorView.theme({ '&': { border: 'none', borderRadius: 'unset' }, '&.cm-focused': { outlineOffset: '-2px' } }),
)

/** The lines a diff adds and removes, for its file's header. */
export interface DiffCounts {
  added: number
  removed: number
}

/** The lines the line diff of an editor's state adds and removes. A file added or removed whole counts every line. */
export function diffCounts(state: EditorState, whole: 'added' | 'removed' | null): DiffCounts {
  if (whole !== null) {
    return whole === 'added' ? { added: state.doc.lines, removed: 0 } : { added: 0, removed: state.doc.lines }
  }
  const before = getOriginalDoc(state)
  const lines = (doc: EditorState['doc'], from: number, to: number) =>
    from >= to ? 0 : doc.lineAt(Math.min(to - 1, doc.length)).number - doc.lineAt(from).number + 1
  return (getChunks(state)?.chunks ?? []).reduce(
    (counts, chunk) => ({
      added: counts.added + lines(state.doc, chunk.fromB, chunk.toB),
      removed: counts.removed + lines(before, chunk.fromA, chunk.toA),
    }),
    { added: 0, removed: 0 },
  )
}

/** A file that changed in part: the merge view against `original`, its column of the text before's numbers and its marks. */
function changedFile(original: string): Extension {
  return [
    unifiedMergeView({
      original,
      // A removed line is in the text colour: a language's colours on the `danger` tint fall below 4.5:1.
      syntaxHighlightDeletions: false,
      mergeControls: false,
      gutter: false,
      collapseUnchanged: { margin: 3, minSize: 4 },
    }),
    // The text before's numbers come before the line numbers of the editor.
    Prec.high(beforeLineNumbers()),
    wholeLineChanges,
  ]
}

const changedLine = Decoration.line({ class: 'cm-changedLine' })

/**
 * A file added or removed whole: each line of the editor's text marked added, or removed, as the merge view marks a
 * changed line, with no diff to compute. Its lines are numbered in the editor's one column, as only one text has them.
 */
function wholeFile(removed: boolean): Extension {
  return [
    EditorView.editorAttributes.of({ class: removed ? 'cm-merge-b cm-removedFile' : 'cm-merge-b' }),
    EditorView.decorations.of((view) => {
      const builder = new RangeSetBuilder<Decoration>()
      for (let number = 1; number <= view.state.doc.lines; number++) {
        const { from } = view.state.doc.line(number)
        builder.add(from, from, changedLine)
      }
      return builder.finish()
    }),
  ]
}

/** The text a line diff shows: without its last line break, so a file's final empty line shows no row of its own. */
export function diffText(text: string): string {
  return text.endsWith('\n') ? text.slice(0, -1) : text
}

/** One number, or a run of numbers one under another, as a block of removed lines needs. */
class LineNumbers extends GutterMarker {
  readonly first: number
  readonly last: number

  constructor(first: number, last = first) {
    super()
    this.first = first
    this.last = last
  }

  eq(other: LineNumbers) {
    return other.first === this.first && other.last === this.last
  }

  toDOM() {
    if (this.first === this.last) {
      return document.createTextNode(String(this.first))
    }
    const numbers = document.createElement('div')
    for (let line = this.first; line <= this.last; line++) {
      numbers.appendChild(document.createElement('div')).textContent = String(line)
    }
    return numbers
  }
}

/** The chunks of the diff, empty before they are computed. */
function chunksOf(view: EditorView): readonly Chunk[] {
  return getChunks(view.state)?.chunks ?? []
}

/**
 * The column of the text before's numbers: an unchanged line's number in the text before, nothing on an added line,
 * and a removed block's numbers beside its lines.
 */
function beforeLineNumbers(): Extension {
  return gutter({
    class: 'cm-beforeLineNumbers',
    lineMarker(view, line) {
      let position = line.from
      for (const chunk of chunksOf(view)) {
        if (line.from >= chunk.fromB && line.from < chunk.toB) {
          return null
        }
        if (chunk.toB <= line.from) {
          position = line.from - chunk.toB + chunk.toA
        }
      }
      const before = getOriginalDoc(view.state)
      return new LineNumbers(before.lineAt(Math.min(position, before.length)).number)
    },
    lineMarkerChange: (update) => update.docChanged,
    widgetMarker(view, _widget, block) {
      // The removed lines of a chunk are one block at the chunk's start in the editor's text.
      const chunk = chunksOf(view).find((candidate) => candidate.fromB === block.from)
      if (!chunk || chunk.fromA >= chunk.toA) {
        return null
      }
      const before = getOriginalDoc(view.state)
      return new LineNumbers(
        before.lineAt(chunk.fromA).number,
        before.lineAt(Math.max(chunk.fromA, Math.min(chunk.endA, before.length))).number,
      )
    },
    initialSpacer: (view) => new LineNumbers(view.state.doc.lines + getOriginalDoc(view.state).lines),
  })
}

/**
 * The indexes of the lines of `text` whose whole text lies inside `ranges`, which are sorted and relative to its start.
 * An empty line has no words, so it is never one of them.
 */
function wholeLines(text: string, ranges: readonly { from: number; to: number }[]): number[] {
  const whole: number[] = []
  let start = 0
  text.split('\n').forEach((line, index) => {
    const end = start + line.length
    let covered = start
    for (const range of ranges) {
      if (range.from <= covered && range.to > covered) {
        covered = range.to
      }
    }
    if (line.length > 0 && covered >= end) {
      whole.push(index)
    }
    start = end + 1
  })
  return whole
}

const wholeLineChange = Decoration.line({ class: 'cm-wholeLineChange' })

/**
 * Marks each line that a change added or removed whole with `cm-wholeLineChange`, so its words are not underlined. An
 * added line takes a line decoration. A removed line is drawn by the merge view's own block, so the class goes on its
 * element once the block is drawn.
 */
const wholeLineChanges = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet

    constructor(view: EditorView) {
      this.decorations = this.build(view)
      // The merge view draws the removed blocks after this plugin starts.
      view.requestMeasure({ read: () => null, write: () => markRemovedLines(view) })
    }

    update(update: ViewUpdate) {
      if (update.docChanged || getChunks(update.state)?.chunks !== getChunks(update.startState)?.chunks) {
        this.decorations = this.build(update.view)
      }
      // A block drawn by this update, as one the viewport reaches, is marked once it is in the page.
      update.view.requestMeasure({ read: () => null, write: () => markRemovedLines(update.view) })
    }

    build(view: EditorView): DecorationSet {
      const doc = view.state.doc
      const builder = new RangeSetBuilder<Decoration>()
      for (const chunk of chunksOf(view)) {
        if (chunk.fromB >= chunk.toB) {
          continue
        }
        const text = doc.sliceString(chunk.fromB, Math.min(chunk.endB, doc.length))
        const ranges = chunk.changes.map((change) => ({ from: change.fromB, to: change.toB }))
        const first = doc.lineAt(chunk.fromB).number
        for (const index of wholeLines(text, ranges)) {
          const line = doc.line(first + index)
          builder.add(line.from, line.from, wholeLineChange)
        }
      }
      return builder.finish()
    }
  },
  { decorations: (plugin) => plugin.decorations },
)

/** Puts `cm-wholeLineChange` on each drawn removed line that a change removed whole. */
function markRemovedLines(view: EditorView) {
  const before = getOriginalDoc(view.state)
  const chunks = chunksOf(view)
  for (const block of view.contentDOM.querySelectorAll<HTMLElement>('.cm-deletedChunk')) {
    const position = view.posAtDOM(block)
    const chunk = chunks.find((candidate) => candidate.fromB === position)
    if (!chunk) {
      continue
    }
    const text = before.sliceString(chunk.fromA, Math.min(chunk.endA, before.length))
    const whole = new Set(
      wholeLines(
        text,
        chunk.changes.map((change) => ({ from: change.fromA, to: change.toA })),
      ),
    )
    block.querySelectorAll(':scope > .cm-deletedLine').forEach((line, index) => {
      line.classList.toggle('cm-wholeLineChange', whole.has(index))
    })
  }
}
