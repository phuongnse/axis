import { Flex, Typography } from 'antd'
import { useContext, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { EmptyDash } from '../platform/EmptyValue'
import { fillText } from '../platform/fillText'
import { keyedListsOf, layerMarks } from '../platform/layerMarks'
import {
  type AuthoringRequestLayerChange,
  changeRows,
  type StudioChangeRow,
  type StudioReleaseChange,
  type StudioResourceItem,
  type StudioRevisionChange,
} from '../platform/studio'
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
  const { colour, radius, spacing, type } = useDesignTokens()
  return (
    <Flex vertical gap={spacing.xs} style={{ minWidth: 0 }}>
      <Typography.Text style={{ color: colour.textMuted }}>{title}</Typography.Text>
      <pre
        data-testid={testId}
        style={{
          margin: 0,
          padding: spacing.sm,
          overflow: 'auto',
          fontFamily: type.codeFontFamily,
          background: colour.surface,
          border: `1px solid ${colour.borderSubtle}`,
          borderRadius: radius.base,
        }}
      >
        {text === null ? <EmptyDash /> : prettyJson(text)}
      </pre>
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

/** A changed file of a diff. `module` is the module resource a layer file changes, with the layer applied, on a layer file only. */
export type StudioChangeFile = StudioRevisionChange & { module?: AuthoringRequestLayerChange }

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
 * file is named by the module resource it changes and shows that resource's JSON and its marks.
 */
function ChangeFile({
  row,
  file,
  folded,
}: {
  row: StudioChangeRow
  file: StudioChangeFile | undefined
  folded: boolean
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
 * diff, and a layer file is named by its module resource, with the layer's marks under it. See
 * docs/reference/shared-widgets.md#which-widget and docs/reference/ai-authoring-in-studio.md#revisions.
 */
export function StudioChanges({
  files,
  resources,
  folded = false,
}: {
  files: StudioChangeFile[]
  resources: StudioResourceItem[]
  folded?: boolean
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const changes = files.map((file): StudioReleaseChange => ({
    path: file.file,
    change: file.change,
    kind: file.module?.kind ?? file.kind,
    name: file.module ? (file.module.name ?? file.module.id) : file.name,
    titles: null,
    locale: null,
  }))
  const byKey = new Map(files.map((file) => [`${file.change}:${file.file}`, file]))
  return (
    <Flex vertical gap="middle">
      {changeRows(changes, resources, locale, t).map((row) => (
        <ChangeFile key={row.key} row={row} file={byKey.get(row.key)} folded={folded} />
      ))}
    </Flex>
  )
}
