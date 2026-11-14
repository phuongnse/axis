import { Chunk, unifiedMergeView } from '@codemirror/merge'
import { EditorState, Prec, Text } from '@codemirror/state'
import { EditorView, lineNumbers } from '@codemirror/view'
import { useEffect, useRef } from 'react'
import { diffTints } from '../platform/antTheme'
import { useText } from '../platform/texts'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { codeEditorTheme, sharedTheme } from './codeEditorTheme'
import { jsonLanguage } from './jsonLanguage'

/** The lines a diff adds and removes. */
export interface DiffCounts {
  added: number
  removed: number
}

/** The lines that each chunk of the line diff from `before` to `after` adds and removes. */
function diffCounts(before: string, after: string): DiffCounts {
  if (before === '' || after === '') {
    return {
      added: after === '' ? 0 : after.split('\n').length,
      removed: before === '' ? 0 : before.split('\n').length,
    }
  }
  const a = Text.of(before.split('\n'))
  const b = Text.of(after.split('\n'))
  const lines = (doc: Text, from: number, to: number) =>
    from === to ? 0 : doc.lineAt(Math.max(from, to - 1)).number - doc.lineAt(from).number + 1
  return Chunk.build(a, b).reduce(
    (counts, chunk) => ({
      added: counts.added + lines(b, chunk.fromB, chunk.toB),
      removed: counts.removed + lines(a, chunk.fromA, chunk.toA),
    }),
    { added: 0, removed: 0 },
  )
}

/**
 * Proposed: a read-only diff of two JSON texts, unified as GitHub shows one: the text after with its line numbers, each
 * added line on the success tint and each removed line under it on the danger tint, the changed words of a line marked
 * deeper, the JSON coloured as the code editor colours it, and each run of unchanged lines folded to "N unchanged lines"
 * past three lines of context, which opens on a click. An added file is all added and a removed file all removed. See
 * the review on #3263. It tells `onCounts` the lines it adds and removes.
 */
export default function JsonDiff({
  before,
  after,
  label,
  onCounts,
}: {
  before: string
  after: string
  label: string
  /** Gets the lines the diff adds and removes, for the file's header. */
  onCounts?: (counts: DiffCounts) => void
}) {
  const t = useText()
  const tokens = useDesignTokens()
  const { mode } = useThemeMode()
  const hostRef = useRef<HTMLDivElement>(null)
  const unchanged = t('shell.studio.diff.unchanged')

  useEffect(() => {
    onCounts?.(diffCounts(before, after))
  }, [before, after, onCounts])

  useEffect(() => {
    const whole = before === '' ? { text: after, added: true } : after === '' ? { text: before, added: false } : null
    const view = new EditorView({
      parent: hostRef.current!,
      state: EditorState.create({
        // A whole file added or removed has no line to compare: its text is all added, or all removed.
        doc: whole === null ? after : whole.text,
        extensions: [
          EditorState.readOnly.of(true),
          EditorView.editable.of(false),
          EditorView.contentAttributes.of({ 'aria-label': label }),
          EditorState.phrases.of({ '$ unchanged lines': unchanged }),
          lineNumbers(),
          jsonLanguage,
          whole === null
            ? unifiedMergeView({
                original: before,
                mergeControls: false,
                highlightChanges: true,
                gutter: true,
                syntaxHighlightDeletions: true,
                collapseUnchanged: { margin: 3, minSize: 4 },
              })
            : EditorView.editorAttributes.of({ class: whole.added ? 'cm-wholeAdded' : 'cm-wholeRemoved' }),
          diffTheme(tokens, mode),
        ],
      }),
    })
    return () => view.destroy()
  }, [before, after, label, unchanged, tokens, mode])

  return <div ref={hostRef} data-testid="json-diff" />
}

const diffTheme = sharedTheme((tokens, mode) => {
  const { colour, spacing } = tokens
  const tint = diffTints(mode)
  return [
    codeEditorTheme(tokens, mode),
    // The diff's own look comes first, so it wins over the shared editor's frame, which an earlier extension sets.
    Prec.high(
      EditorView.theme({
        // Proposed: the diff fills the block of its file flush, as GitHub draws a diff, with no frame or corners of its
        // own, see the review on #3263. Keyboard focus stays visible inside the edge of the block.
        '&': { border: 'none', borderRadius: 'unset' },
        '&.cm-focused': { outlineOffset: '-2px' },
        '.cm-content': { padding: `${spacing.xs}px 0` },
        '.cm-line': { paddingInline: `${spacing.sm}px` },
        '.cm-gutters': {
          backgroundColor: colour.surface,
          color: colour.textMuted,
          border: 'none',
          borderInlineEnd: `1px solid ${colour.borderSubtle}`,
        },
        '.cm-lineNumbers .cm-gutterElement': { paddingInline: `${spacing.xs}px` },
        '&.cm-merge-b .cm-changedLine, .cm-inlineChangedLine': { backgroundColor: tint.added },
        '&.cm-merge-b .cm-changedText': { background: tint.addedWord },
        '.cm-deletedChunk': { backgroundColor: tint.removed, paddingLeft: 0 },
        '.cm-deletedChunk .cm-deletedLine': { paddingInline: `${spacing.sm}px` },
        '&.cm-merge-b .cm-deletedText, .cm-deletedChunk .cm-deletedText': { background: tint.removedWord },
        '&.cm-merge-b .cm-changedLineGutter': { background: colour.success },
        '.cm-deletedLineGutter': { background: colour.danger },
        '.cm-changeGutter': { width: '3px', paddingLeft: 0 },
        '&.cm-wholeAdded .cm-line': { backgroundColor: tint.added },
        '&.cm-wholeRemoved .cm-line': { backgroundColor: tint.removed },
        '.cm-collapsedLines': {
          color: colour.primaryText,
          background: colour.canvas,
          paddingInline: `${spacing.sm}px`,
          '&:before, &:after': { content: '""', margin: 0 },
        },
      }),
    ),
  ]
})
