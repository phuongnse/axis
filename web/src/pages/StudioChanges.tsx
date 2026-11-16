import { Flex, Typography } from 'antd'
import { useContext } from 'react'
import { EmptyDash } from '../platform/EmptyValue'
import {
  changeRows,
  type StudioReleaseChange,
  type StudioResourceItem,
  type StudioRevisionChange,
} from '../platform/studio'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/** The JSON text of one side of a change, indented when it parses, and as it is when it does not. */
function prettyJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2)
  } catch {
    return text
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

/**
 * The files a revision or a draft changed, sorted by name: each shows its name, its kind (the em dash when it has none)
 * and Added, Changed or Removed, and under them its JSON before and after as a {@link JsonDiff}. `resources` are the
 * application's resources, which name a file's kind and title. See
 * docs/reference/shared-widgets.md#which-widget and docs/reference/ai-authoring-in-studio.md#revisions.
 */
export function StudioChanges({
  files,
  resources,
}: {
  files: StudioRevisionChange[]
  resources: StudioResourceItem[]
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour } = useDesignTokens()
  const changes = files.map((file): StudioReleaseChange => ({
    path: file.file,
    change: file.change,
    kind: file.kind,
    name: file.name,
    titles: null,
    locale: null,
  }))
  const byKey = new Map(files.map((file) => [`${file.change}:${file.file}`, file]))
  return (
    <Flex vertical gap="middle">
      {changeRows(changes, resources, locale, t).map((row) => {
        const file = byKey.get(row.key)
        return (
          <section key={row.key} data-testid="studio-changes-file" data-file={file?.file}>
            <Flex vertical gap="small">
              <Flex wrap gap="small" align="baseline">
                <Typography.Text strong>{row.name}</Typography.Text>
                <Typography.Text style={{ color: colour.textMuted }}>
                  {row.kind === null ? <EmptyDash /> : kindSingular(row.kind, t)}
                </Typography.Text>
                <Typography.Text>{t(`shell.studio.overview.change.${row.change}`)}</Typography.Text>
              </Flex>
              <JsonDiff before={file?.before ?? null} after={file?.after ?? null} />
            </Flex>
          </section>
        )
      })}
    </Flex>
  )
}
