import { Flex, Typography } from 'antd'
import { lazy, Suspense, useContext, useId, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { changeLook } from '../platform/changeTint'
import { EmptyDash } from '../platform/EmptyValue'
import { FilledText } from '../platform/FilledText'
import { FocusRingStyle } from '../platform/FocusRingStyle'
import { FoldButton } from '../platform/FoldButton'
import { fillText } from '../platform/fillText'
import { keyedListsOf, layerMarks } from '../platform/layerMarks'
import { PersonName } from '../platform/PersonName'
import {
  type AuthoringRequestLayerChange,
  changeRows,
  type StudioChangeAuthor,
  type StudioChangeRow,
  type StudioReleaseChange,
  type StudioResourceItem,
  type StudioRevisionChange,
} from '../platform/studio'
import { studioRequestAddress } from '../platform/studioAddress'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { visuallyHidden } from '../platform/visuallyHidden'
import { PlatformContext } from './context'
import { fileLanguage } from './fileLanguage'
import { LayerMarkLabel } from './LayerMarkViews'
import { lineCounts } from './lineDiff'

const CodeEditor = lazy(() => import('./CodeEditor'))

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
 * {@link StudioChangeAuthor}. A person is drawn by `PersonName`, so the reader is followed by "(you)", a server revision
 * is "System", and an AI request is a link to its page.
 */
function ChangedBy({ application, authors }: { application: string; authors: StudioChangeAuthor[] }) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const nodes: ReactNode[] = authors.map((author) => {
    switch (author.type) {
      case 'system':
        return t('shell.studio.releases.system')
      case 'person':
        return <PersonName key={author.id} id={author.id} name={author.name} />
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
 * The lines a file added and removed, as "+8" in the added colour and "−3" in the removed colour, each left out when it
 * is zero. The marks and colours are those of the line diff, from `changeLook`. Assistive technology reads them in
 * full, such as "8 lines added, 3 lines removed", joined in the locale's list format.
 */
function LineCounts({ added, removed }: { added: number; removed: number }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const spoken = [
    added > 0 &&
      fillText(t(added === 1 ? 'shell.studio.changes.linesAdded.one' : 'shell.studio.changes.linesAdded.many'), {
        count: `${added}`,
      }),
    removed > 0 &&
      fillText(t(removed === 1 ? 'shell.studio.changes.linesRemoved.one' : 'shell.studio.changes.linesRemoved.many'), {
        count: `${removed}`,
      }),
  ].filter((part): part is string => part !== false)
  const addedLook = changeLook('added', colour)
  const removedLook = changeLook('removed', colour)
  return (
    <Flex gap="small" style={{ marginInlineStart: 'auto' }}>
      {added > 0 && (
        <Typography.Text aria-hidden style={{ color: addedLook.markColour }}>
          {addedLook.mark}
          {added}
        </Typography.Text>
      )}
      {removed > 0 && (
        <Typography.Text aria-hidden style={{ color: removedLook.markColour }}>
          {removedLook.mark}
          {removed}
        </Typography.Text>
      )}
      <span style={visuallyHidden}>{new Intl.ListFormat(locale, { type: 'unit', style: 'long' }).format(spoken)}</span>
    </Flex>
  )
}

/**
 * One changed file as a block, see docs/reference/ai-authoring-in-studio.md#revisions: a border and a header with the
 * file's {@link FoldButton}, its name, its kind, its change and its line counts, then who changed it and the marks of a
 * layer file, then the file's line diff of `CodeEditor`. The diff is drawn while the file is folded, so the counts stay
 * true, and it is hidden. The block is open at first unless `folded`. A layer file is named by the module resource it
 * changes, and a resource shows its indented JSON as a package file shows its text. `changedBy` is the line of who
 * changed the file, or null. Shared widgets are in docs/reference/shared-widgets.md#which-widget.
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
  const bodyId = useId()
  const module = file?.module
  const packageText = row.packageFile && module === undefined
  const side = (text: string | null) => (text === null ? '' : packageText ? text : prettyJson(text))
  const original = side(module ? module.before : (file?.before ?? null))
  const value = side(module ? module.after : (file?.after ?? null))
  const language = packageText ? fileLanguage(row.name) : 'json'
  const { added, removed } = lineCounts(original, value)
  return (
    <section
      data-testid="studio-changes-file"
      data-file={file?.file}
      data-diff-file
      style={{ border: `1px solid ${colour.borderSubtle}`, borderRadius: radius.base, overflow: 'hidden' }}
    >
      <Flex vertical gap="small" style={{ padding: `${spacing.sm}px ${spacing.md}px` }}>
        <Flex wrap gap="small" align="center">
          <FoldButton
            open={open}
            onToggle={() => setOpen((current) => !current)}
            names={{ show: 'shell.studio.changes.showDiffName', hide: 'shell.studio.changes.hideDiffName' }}
            values={{ name: row.name }}
            controls={bodyId}
          />
          <Typography.Text strong>{row.name}</Typography.Text>
          <Typography.Text style={{ color: colour.textMuted }}>
            {row.kind !== null ? (
              kindSingular(row.kind, t)
            ) : row.packageFile ? (
              t('shell.studio.changes.packageFile')
            ) : (
              <EmptyDash />
            )}
          </Typography.Text>
          <Typography.Text>{t(`shell.studio.overview.change.${row.change}`)}</Typography.Text>
          <LineCounts added={added} removed={removed} />
        </Flex>
        {changedBy}
        {module !== undefined && <LayerMarks module={module} />}
      </Flex>
      <div id={bodyId} hidden={!open}>
        <Suspense fallback={null}>
          <CodeEditor label={row.name} value={value} original={original} language={language} readOnly grow />
        </Suspense>
      </div>
    </section>
  )
}

/**
 * The files a revision, a request's draft or the publish page changed, sorted by name, a package file last by its path.
 * Each is a block of {@link ChangeFile}: a header with a fold, its name, its kind (the em dash when it has none), its
 * change and its line counts over the line diff of `CodeEditor`, a resource's JSON included, open at first unless
 * `folded`, as the publish page folds them. `resources` are the application's resources, which name a file's kind and
 * title. A layer file is named by its module resource, with the layer's marks under its header. With `changedBy`, the
 * publish page's files also show "Changed by" under their header, from `authors` of each file. A block draws one focus
 * ring while its diff has focus. See docs/reference/shared-widgets.md#which-widget,
 * docs/reference/ai-authoring-in-studio.md#revisions and docs/reference/studio.md#publishing.
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
      <FocusRingStyle selector="[data-diff-file]:has(.cm-editor:focus-within)" />
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
