// A file's line diff, see docs/reference/ai-authoring-in-studio.md#revisions and docs/reference/studio-packages.md#diffs.
import { Chunk, getChunks, getOriginalDoc, uncollapseUnchanged, unifiedMergeView } from '@codemirror/merge'
import { Prec, RangeSetBuilder, Text, type Extension } from '@codemirror/state'
import {
  Decoration,
  EditorView,
  gutter,
  GutterMarker,
  lineNumbers,
  ViewPlugin,
  type DecorationSet,
  type ViewUpdate,
} from '@codemirror/view'
import { changeLook } from '../platform/changeTint'
import type { DesignTokens } from '../platform/designTokens'

/**
 * How a file differs: in part, added whole, whose text before is empty, or removed whole, whose text after is empty. A
 * file added or removed whole is drawn as its own lines, each marked, with no comparison and one column of numbers.
 */
export type DiffShape = 'changed' | 'added' | 'removed'

/** The text a line diff shows: without its last line break, so a file's final empty line shows no row of its own. */
export function diffText(text: string): string {
  return text.endsWith('\n') ? text.slice(0, -1) : text
}

/** How the text after, `value`, differs from the text before, `original`. */
export function diffShape(original: string, value: string): DiffShape {
  if (diffText(original) === '') {
    return 'added'
  }
  return diffText(value) === '' ? 'removed' : 'changed'
}

/** The number of lines of a diff text, which is 0 for the empty text. */
function lineCountOf(text: string): number {
  return text === '' ? 0 : text.split('\n').length
}

/** The lines of `doc` that a chunk spans from `from` to `to`, which is 0 where the chunk has no text on that side. */
function chunkLines(doc: Text, from: number, to: number): number {
  if (from === to) {
    return 0
  }
  // A chunk ends with its last line's break, so the start of the next line is no line of its own.
  const text = doc.sliceString(from, to)
  const lines = text.split('\n').length
  return text.endsWith('\n') ? lines - 1 : lines
}

/**
 * The lines added and the lines removed by a change from the text before, `original`, to the text after, `value`: the
 * numbers in a file's header, counted on the same diff as {@link lineDiff} draws. A file added or removed whole counts
 * each of its lines, see docs/reference/ai-authoring-in-studio.md#revisions.
 */
export function lineCounts(original: string, value: string): { added: number; removed: number } {
  const before = diffText(original)
  const after = diffText(value)
  switch (diffShape(original, value)) {
    case 'added':
      return { added: lineCountOf(after), removed: 0 }
    case 'removed':
      return { added: 0, removed: lineCountOf(before) }
    case 'changed': {
      const textBefore = Text.of(before.split('\n'))
      const textAfter = Text.of(after.split('\n'))
      let added = 0
      let removed = 0
      for (const chunk of Chunk.build(textBefore, textAfter)) {
        removed += chunkLines(textBefore, chunk.fromA, chunk.toA)
        added += chunkLines(textAfter, chunk.fromB, chunk.toB)
      }
      return { added, removed }
    }
  }
}

/**
 * The line diff of a code editor: the editor's text is the text after a change, and `original` the text before. A
 * removed line sits on the `danger` tint after "−", in the text colour, and an added line on the `success` tint after
 * "+", in its language's colours, both from `changeLook`. The changed words of a line that changed in part are
 * underlined, and a line added or removed whole, or one that changed only in its spaces, is not. A file that changed in
 * part numbers its lines in two columns, the text before's and the text after's, and a run of unchanged lines folds to
 * one row that a click or the keyboard opens. It draws no accept or reject buttons. The editor leaves out its own
 * line numbers: this extension draws them.
 */
export function lineDiff(original: string, tokens: DesignTokens, shape: DiffShape): Extension {
  return [
    diffTheme(tokens),
    shape === 'changed'
      ? [
          unifiedMergeView({
            original: diffText(original),
            // A removed line is in the text colour: a language's colours on the `danger` tint fall below 4.5:1.
            syntaxHighlightDeletions: false,
            mergeControls: false,
            gutter: false,
            collapseUnchanged: { margin: 3, minSize: 4 },
          }),
          beforeLineNumbers(),
          lineNumbers(),
          wordMarks,
          collapsedRowKeys,
        ]
      : [lineNumbers(), wholeFile(shape === 'removed')],
  ]
}

const changedLine = Decoration.line({ class: 'cm-changedLine' })

/** Each line of the editor's text marked added, or removed, as the merge view marks a changed line, with no diff to compute. */
function wholeFile(removed: boolean): Extension {
  return [
    EditorView.editorAttributes.of({ class: removed ? 'cm-merge-b cm-removedFile' : 'cm-merge-b' }),
    EditorView.decorations.of((view) => {
      const builder = new RangeSetBuilder<Decoration>()
      if (view.state.doc.length > 0) {
        for (let number = 1; number <= view.state.doc.lines; number++) {
          const { from } = view.state.doc.line(number)
          builder.add(from, from, changedLine)
        }
      }
      return builder.finish()
    }),
  ]
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
  return Prec.high(
    gutter({
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
      lineMarkerChange: (update) => update.docChanged || chunksChanged(update),
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
    }),
  )
}

function chunksChanged(update: ViewUpdate) {
  return getChunks(update.state)?.chunks !== getChunks(update.startState)?.chunks
}

/**
 * The indexes of the lines of `text` whose changed words show no underline: a line whose changed text is only spaces,
 * such as the indent before a bracket the diff kept, and a line whose unchanged text is only spaces, as a line added
 * or removed whole. `ranges` are sorted, apart and relative to the text's start. An empty line has no words.
 */
function linesWithoutChangedWords(text: string, ranges: readonly { from: number; to: number }[]): number[] {
  const lines: number[] = []
  let start = 0
  text.split('\n').forEach((line, index) => {
    const end = start + line.length
    let changed = false
    let unchanged = false
    for (let at = start; at < end; at++) {
      if (/\S/.test(text[at])) {
        if (ranges.some((range) => at >= range.from && at < range.to)) {
          changed = true
        } else {
          unchanged = true
        }
      }
    }
    if (!changed || !unchanged) {
      lines.push(index)
    }
    start = end + 1
  })
  return lines
}

const noChangedWords = Decoration.line({ class: 'cm-noChangedWords' })

/**
 * Marks each line that shows no changed words with `cm-noChangedWords`, so nothing on it is underlined. An
 * added line takes a line decoration. A removed line is drawn by the merge view's own block, so the class goes on its
 * element once the block is drawn.
 */
const wordMarks = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet

    constructor(view: EditorView) {
      this.decorations = this.build(view)
      // The merge view draws the removed blocks and the folded rows after this plugin starts.
      view.requestMeasure({ read: () => null, write: () => markDrawnLines(view) })
    }

    update(update: ViewUpdate) {
      if (update.docChanged || chunksChanged(update)) {
        this.decorations = this.build(update.view)
      }
      // A block or a row drawn by this update, as one the viewport reaches, is marked once it is in the page.
      update.view.requestMeasure({ read: () => null, write: () => markDrawnLines(update.view) })
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
        for (const index of linesWithoutChangedWords(text, ranges)) {
          const line = doc.line(first + index)
          builder.add(line.from, line.from, noChangedWords)
        }
      }
      return builder.finish()
    }
  },
  { decorations: (plugin) => plugin.decorations },
)

/** Puts `cm-noChangedWords` on each drawn removed line that shows no changed words, and makes each folded row a button. */
function markDrawnLines(view: EditorView) {
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
      linesWithoutChangedWords(
        text,
        chunk.changes.map((change) => ({ from: change.fromA, to: change.toA })),
      ),
    )
    block.querySelectorAll(':scope > .cm-deletedLine').forEach((line, index) => {
      line.classList.toggle('cm-noChangedWords', whole.has(index))
    })
  }
  // The merge view opens a folded row on a click only, so the row is a button that the keyboard reaches.
  for (const row of view.contentDOM.querySelectorAll<HTMLElement>('.cm-collapsedLines')) {
    row.setAttribute('role', 'button')
    row.setAttribute('tabindex', '0')
  }
}

/** Opens a folded row on Enter and Space, as the merge view's own click handler does. */
const collapsedRowKeys = EditorView.domEventHandlers({
  keydown(event, view) {
    const row = event.target instanceof Element ? event.target.closest('.cm-collapsedLines') : null
    if (!row || (event.key !== 'Enter' && event.key !== ' ')) {
      return false
    }
    event.preventDefault()
    view.dispatch({ effects: uncollapseUnchanged.of(view.posAtDOM(row)) })
    view.focus()
    return true
  },
})

/** The look of a line diff: the tints, the marks, the underline of changed words and the folded row. */
function diffTheme(tokens: DesignTokens): Extension {
  const { colour, spacing } = tokens
  const removed = changeLook('removed', colour)
  const added = changeLook('added', colour)
  const underline = (markColour: string) => ({
    background: 'none',
    textDecorationLine: 'underline',
    textDecorationThickness: '2px',
    textDecorationColor: markColour,
  })
  return Prec.high(
    EditorView.theme({
      // The diff draws no focus ring of its own: its file's block draws one, see
      // docs/reference/ai-authoring-in-studio.md#revisions. The rule names `.cm-editor` too, so it wins over the code editor's.
      '&.cm-editor.cm-focused': { outline: 'none' },
      // The rules that set what the merge view's own theme sets name `.cm-editor` too, so they win whichever theme loads first.
      // Each line of a diff keeps room for its mark, so the code of every line starts at the same place.
      '&.cm-merge-b .cm-line, .cm-deletedLine': {
        position: 'relative',
        paddingInline: `calc(${spacing.sm}px + 2ch) ${spacing.sm}px`,
      },
      '&.cm-editor.cm-merge-b .cm-changedLine': { backgroundColor: added.tint },
      '&.cm-editor.cm-merge-b.cm-removedFile .cm-changedLine': { backgroundColor: removed.tint },
      '&.cm-merge-b .cm-changedLine::before, .cm-deletedLine::before': {
        position: 'absolute',
        insetInlineStart: `${spacing.sm}px`,
      },
      '&.cm-merge-b .cm-changedLine::before': { content: `"${added.mark}"`, color: added.markColour },
      '&.cm-merge-b.cm-removedFile .cm-changedLine::before, .cm-deletedLine::before': {
        content: `"${removed.mark}"`,
        color: removed.markColour,
      },
      '&.cm-editor .cm-deletedChunk': { backgroundColor: removed.tint, color: colour.text, paddingLeft: 0 },
      // The changed words of a changed line are underlined in the mark's colour, as a stronger tint under coloured code
      // falls below 4.5:1. A line that shows no changed words has no underline.
      '&.cm-editor.cm-merge-b .cm-changedText': underline(added.markColour),
      '&.cm-editor .cm-deletedChunk .cm-deletedText': underline(removed.markColour),
      '&.cm-editor .cm-noChangedWords .cm-changedText, &.cm-editor .cm-noChangedWords.cm-deletedLine .cm-deletedText': {
        textDecorationLine: 'none',
      },
      '.cm-beforeLineNumbers .cm-gutterElement': { paddingInline: `${spacing.sm}px`, textAlign: 'right' },
      // The row that opens a folded run of unchanged lines reads as an action.
      '&.cm-editor .cm-collapsedLines': {
        color: colour.primaryText,
        background: colour.canvas,
        padding: `0 ${spacing.sm}px 0 calc(${spacing.sm}px + 2ch)`,
        '&:hover': { textDecoration: 'underline' },
        '&:focus-visible': { outline: `2px solid ${colour.focusRing}`, outlineOffset: '-2px' },
        '&:before, &:after': { content: 'none' },
      },
    }),
  )
}
