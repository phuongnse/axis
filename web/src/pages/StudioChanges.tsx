import { Flex, Typography } from 'antd'
import { lazy, Suspense, useContext, useId, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { FilledText } from '../platform/FilledText'
import { fillText } from '../platform/fillText'
import { keyedListsOf, layerMarks } from '../platform/layerMarks'
import {
  type AuthoringRequestLayerChange,
  changeRows,
  type StudioChangeAuthor,
  type StudioChangeRow,
  type StudioReleaseChange,
  type StudioResourceItem,
  type StudioRevisionChange,
} from '../platform/studio'
import { packageFileOf, studioRequestAddress } from '../platform/studioAddress'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { addedMark, removedMark } from '../platform/changeTint'
import { FoldButton } from '../platform/FoldButton'
import { visuallyHidden } from '../platform/visuallyHidden'
import { PlatformContext } from './context'
import { fileLanguage } from './fileLanguage'
import type { DiffCounts } from './lineDiff'
import { LayerMarkLabel } from './LayerMarkViews'

/** The JSON text of one side of a change, indented when it parses, and as it is when it does not. */
function prettyJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2)
  } catch {
    return text
  }
}

/** The JSON value of a text, or null when it is no text or does not parse. */
function parsedJson(text: string | null): unknown {
  if (text === null) {
    return null
  }
  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}

const CodeEditor = lazy(() => import('./CodeEditor'))

/**
 * Proposed: a package file's change as a line diff, in the one code editor of Studio, read-only, coloured by the file's
 * language, see #4623 and docs/reference/studio-packages.md#diffs. An added file shows each line added and a removed
 * file each line removed. A resource's JSON shows the same way, indented, as #3263's rule a file's diff agreed.
 */
function LineDiff({
  path,
  name,
  before,
  after,
  json,
  onCounts,
}: {
  path: string
  name: string
  before: string | null
  after: string | null
  /** A resource's JSON, coloured as JSON whatever its file is named. */
  json: boolean
  onCounts: (counts: DiffCounts) => void
}) {
  const t = useText()
  return (
    <div data-testid="line-diff">
      <Suspense fallback={null}>
        <CodeEditor
          label={fillText(t('shell.studio.changes.diffName'), { name })}
          value={after ?? ''}
          original={before ?? ''}
          language={json ? 'json' : fileLanguage(path)}
          onDiffCounts={onCounts}
          readOnly
          // As tall as its text, so a wheel over the diff scrolls the page, see
          // docs/reference/actions-and-controls.md#scroll-areas.
          grow
        />
      </Suspense>
    </div>
  )
}

/**
 * A file of {@link StudioChanges}: a revision's file, or a live diff's with its titles by locale, or null. `module` is the
 * module resource a layer file changes, with the layer applied, on a layer file only.
 */
export type StudioChangedFile = StudioRevisionChange & {
  titles?: Record<string, string> | null
  module?: AuthoringRequestLayerChange
}

/**
 * The line "Changed by" with each person who changed a file, in the order of their first change, each as a
 * {@link StudioChangeAuthor}. The reader is "you", a server revision is "System", and an AI request is a link to its page.
 */
function ChangedBy({ application, authors }: { application: string; authors: StudioChangeAuthor[] }) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const nodes: ReactNode[] = authors.map((author) => {
    switch (author.type) {
      case 'you':
        return t('shell.studio.changes.you')
      case 'system':
        return t('shell.studio.releases.system')
      case 'person':
        return author.name
      case 'request':
        return (
          <Link key={author.id} to={studioRequestAddress(application, author.id)}>
            {fillText(t('shell.studio.changes.aiRequest'), { name: author.name })}
          </Link>
        )
    }
  })
  // The authors joined as one list in the locale's words, each author's node in place of its index.
  const list = new Intl.ListFormat(locale, { type: 'conjunction' })
    .formatToParts(authors.map((_, index) => `${index}`))
    .map((part) => (part.type === 'element' ? nodes[Number(part.value)] : part.value))
  return (
    <Typography.Text data-testid="studio-changes-by">
      <FilledText text={t('shell.studio.changes.changedBy')} parts={{ authors: list }} />
    </Typography.Text>
  )
}

/**
 * The marks of a module resource that a layer changed, each element as its key and a tag with its accessible name, such as
 * "supplier, added by this application". Removed elements show muted, as the layer editor shows them.
 */
function LayerMarks({ module }: { module: AuthoringRequestLayerChange }) {
  const { colour, spacing } = useDesignTokens()
  const content = parsedJson(module.after)
  const { keyedLists, maps } = keyedListsOf(module.kind, content)
  const elements = layerMarks(content, module.marks, module.removed, keyedLists, maps)
  if (elements.length === 0) {
    return null
  }
  return (
    <Flex vertical gap={spacing.xs}>
      {elements.map((element, index) => (
        <Flex key={index} gap="small" align="center" wrap data-testid="studio-changes-mark">
          <Typography.Text style={element.mark === 'removed' ? { color: colour.textMuted } : undefined}>
            {element.key}
          </Typography.Text>
          <LayerMarkLabel mark={element.mark} name={element.key} />
        </Flex>
      ))}
    </Flex>
  )
}

/**
 * Proposed: the focus ring of a file's block, round the whole file while its diff has focus, as a CSS rule since inline
 * styles cannot reach focus, as `TileFocusStyle` draws a tile's; the diff draws no ring of its own, see #4623.
 */
function DiffFocusStyle() {
  const { colour } = useDesignTokens()
  return (
    <style>{`[data-diff-file]:has(.cm-focused) { outline: 2px solid ${colour.focusRing}; outline-offset: -1px; }`}</style>
  )
}

/**
 * Proposed: one changed file as #3263 agreed it, see #4623 and docs/reference/ai-authoring-in-studio.md#revisions: a
 * block with its own border, whose header holds the fold's chevron, the file's name in bold, its kind muted, its change
 * and the lines it adds and removes, then who changed it, then its line diff flush in the block. A package file's kind
 * reads Package file. A layer file is named by the module resource it changes and shows that resource's JSON and its
 * marks. Every file starts open, or folded when `folded`.
 */
function ChangeFile({
  row,
  file,
  folded,
  changedBy,
}: {
  row: StudioChangeRow
  file: StudioChangedFile | undefined
  folded: boolean
  changedBy: ReactNode
}) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const [open, setOpen] = useState(!folded)
  const [counts, setCounts] = useState<DiffCounts>()
  const diffId = useId()
  const module = file?.module
  const packageFile = module === undefined && file !== undefined && packageFileOf(file.file) !== undefined
  const kind =
    row.kind !== null ? kindSingular(row.kind, t) : packageFile ? t('shell.studio.changes.packageFile') : null
  const before = module ? module.before : (file?.before ?? null)
  const after = module ? module.after : (file?.after ?? null)
  const rule = `1px solid ${colour.borderSubtle}`
  return (
    <section
      data-testid="studio-changes-file"
      data-file={file?.file}
      data-diff-file
      // The block clips its diff to its rounded corners.
      style={{
        border: rule,
        borderRadius: radius.base,
        background: colour.surface,
        overflow: 'hidden',
      }}
    >
      <Flex
        wrap
        gap="small"
        align="center"
        style={{
          padding: `${spacing.xs / 2}px ${spacing.sm}px ${spacing.xs / 2}px ${spacing.xs / 2}px`,
          borderBottom: open || changedBy !== null || module !== undefined ? rule : undefined,
        }}
      >
        <FoldButton
          open={open}
          showKey="shell.studio.changes.showDiffName"
          hideKey="shell.studio.changes.hideDiffName"
          nameValues={{ name: row.name }}
          controls={diffId}
          onToggle={() => setOpen((current) => !current)}
        />
        <Typography.Text strong>{row.name}</Typography.Text>
        {kind !== null && <Typography.Text style={{ color: colour.textMuted }}>{kind}</Typography.Text>}
        <Typography.Text>{t(`shell.studio.overview.change.${row.change}`)}</Typography.Text>
        {counts !== undefined && (
          <span style={{ marginInlineStart: 'auto' }}>
            <span style={visuallyHidden}>
              {fillText(t('shell.studio.changes.counts'), {
                added: String(counts.added),
                removed: String(counts.removed),
              })}
            </span>
            <span aria-hidden style={{ color: colour.successText }}>{`${addedMark}${counts.added}`}</span>{' '}
            <span aria-hidden style={{ color: colour.dangerText }}>{`${removedMark}${counts.removed}`}</span>
          </span>
        )}
      </Flex>
      {(changedBy !== null || module !== undefined) && (
        <Flex
          vertical
          gap="small"
          style={{ padding: `${spacing.xs}px ${spacing.sm}px`, borderBottom: open ? rule : undefined }}
        >
          {changedBy}
          {module !== undefined && <LayerMarks module={module} />}
        </Flex>
      )}
      {/* The diff stays drawn while folded, so the header counts its lines. */}
      <div id={diffId} hidden={!open}>
        <LineDiff
          path={file?.file ?? ''}
          name={row.name}
          before={before === null ? null : packageFile ? before : prettyJson(before)}
          after={after === null ? null : packageFile ? after : prettyJson(after)}
          json={!packageFile}
          onCounts={setCounts}
        />
      </div>
    </section>
  )
}

/**
 * The files a revision or a draft changed, sorted by name, each a block as {@link ChangeFile} draws it. `resources` are the
 * application's resources, which name a file's kind and title. With `folded`, each file starts folded, and a layer file is named by its module resource, with the layer's marks under it. With `changedBy`, the publish
 * page's files also show "Changed by" under their header, from `authors` of each file. See
 * docs/reference/shared-widgets.md#which-widget, docs/reference/ai-authoring-in-studio.md#revisions and
 * docs/reference/studio.md#publishing.
 */
export function StudioChanges<File extends StudioChangedFile>({
  files,
  resources,
  folded = false,
  changedBy,
}: {
  files: File[]
  resources: StudioResourceItem[]
  folded?: boolean
  changedBy?: { application: string; authors: (file: File) => StudioChangeAuthor[] }
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const changes = files.map((file): StudioReleaseChange => ({
    path: file.file,
    change: file.change,
    kind: file.module?.kind ?? file.kind,
    name: file.module ? (file.module.name ?? file.module.id) : file.name,
    titles: file.titles ?? null,
    locale: null,
  }))
  const byKey = new Map(files.map((file) => [`${file.change}:${file.file}`, file]))
  return (
    <Flex vertical gap="middle">
      <DiffFocusStyle />
      {changeRows(changes, resources, locale, t).map((row) => {
        const file = byKey.get(row.key)
        const authors = file && changedBy ? changedBy.authors(file) : []
        const by =
          changedBy && authors.length > 0 ? <ChangedBy application={changedBy.application} authors={authors} /> : null
        return <ChangeFile key={row.key} row={row} file={file} folded={folded} changedBy={by} />
      })}
    </Flex>
  )
}
