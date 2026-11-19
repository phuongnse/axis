import { Flex, Typography } from 'antd'
import { useContext, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { CodeBlock } from '../platform/CodeBlock'
import { EmptyDash } from '../platform/EmptyValue'
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
import { studioRequestAddress } from '../platform/studioAddress'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
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

/** One side of a diff: its title over the JSON text, or over the em dash where the file does not exist. */
function Side({ testId, title, text }: { testId: string; title: string; text: string | null }) {
  const { colour, spacing } = useDesignTokens()
  return (
    <Flex vertical gap={spacing.xs} style={{ minWidth: 0 }}>
      <Typography.Text style={{ color: colour.textMuted }}>{title}</Typography.Text>
      <CodeBlock testId={testId} text={text === null ? null : prettyJson(text)} />
    </Flex>
  )
}

/**
 * The JSON of one file before and after a change, side by side, each indented. A side where the file does not exist is
 * null and shows the em dash. It does not mark the changed lines. Shared widgets are in
 * docs/reference/shared-widgets.md#which-widget.
 */
export function JsonDiff({ before, after }: { before: string | null; after: string | null }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: spacing.md }}>
      <Side testId="json-diff-before" title={t('shell.studio.history.before')} text={before} />
      <Side testId="json-diff-after" title={t('shell.studio.history.after')} text={after} />
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
 * One changed file: its name, its kind, its change and, when `folded`, a Show the diff link that opens its JSON. A layer
 * file is named by the module resource it changes and shows that resource's JSON and its marks. `changedBy` is the line
 * of who changed the file, or null.
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
  const { colour } = useDesignTokens()
  const [open, setOpen] = useState(false)
  const module = file?.module
  return (
    <section data-testid="studio-changes-file" data-file={file?.file}>
      <Flex vertical gap="small">
        <Flex wrap gap="small" align="baseline">
          <Typography.Text strong>{row.name}</Typography.Text>
          <Typography.Text style={{ color: colour.textMuted }}>
            {row.kind === null ? <EmptyDash /> : kindSingular(row.kind, t)}
          </Typography.Text>
          <Typography.Text>{t(`shell.studio.overview.change.${row.change}`)}</Typography.Text>
          {folded && (
            <AxisButton
              role="link"
              aria-expanded={open}
              aria-label={fillText(
                t(open ? 'shell.studio.changes.hideDiffName' : 'shell.studio.changes.showDiffName'),
                {
                  name: row.name,
                },
              )}
              onClick={() => setOpen((current) => !current)}
            >
              {t(open ? 'shell.studio.changes.hideDiff' : 'shell.studio.changes.showDiff')}
            </AxisButton>
          )}
        </Flex>
        {changedBy}
        {module !== undefined && <LayerMarks module={module} />}
        {(!folded || open) && (
          <JsonDiff
            before={module ? module.before : (file?.before ?? null)}
            after={module ? module.after : (file?.after ?? null)}
          />
        )}
      </Flex>
    </section>
  )
}

/**
 * The files a revision or a draft changed, sorted by name: each shows its name, its kind (the em dash when it has none)
 * and Added, Changed or Removed, and under them its JSON before and after as a {@link JsonDiff}. `resources` are the
 * application's resources, which name a file's kind and title. With `folded`, each file starts closed behind Show the
 * diff, and a layer file is named by its module resource, with the layer's marks under it. With `changedBy`, the publish
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
