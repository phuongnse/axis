import { Typography } from 'antd'
import { useContext } from 'react'
import { Link } from 'react-router'
import type { StudioChangeRow, StudioReleasePartChange } from '../platform/studio'
import { sortByName } from '../platform/listOrder'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/**
 * The change list of Studio: one row for each file that differs, with its name, its kind and what changed. A name opens
 * its resource through `address`, except a removed file's, which the folder lacks and shows as plain text. With
 * `plainChanged`, which a release page sets, a changed resource is plain text too, because the folder's version is not
 * the one the release changed. With `parts`, which a release page sets, each module and package that differs follows
 * the files, modules first and each group by label, and an empty `parts` says that none differs.
 */
export function StudioChangeList({
  rows,
  parts,
  address,
  plainChanged = false,
}: {
  rows: StudioChangeRow[]
  parts?: StudioReleasePartChange[]
  address: (extra: Record<string, string>) => string
  plainChanged?: boolean
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, spacing } = useDesignTokens()
  const sortedParts =
    parts &&
    sortByName(
      parts,
      (part) => part.label,
      locale,
      (part) => (part.type === 'module' ? 0 : 1),
    )
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'minmax(0, 1fr) max-content max-content',
        columnGap: spacing.lg,
        rowGap: spacing.xs,
      }}
    >
      {rows.map((row) => (
        <div key={row.key} data-testid="studio-change-row" style={{ display: 'contents' }}>
          {row.resourceId === null || (plainChanged && row.change === 'changed') ? (
            <Typography.Text>{row.name}</Typography.Text>
          ) : (
            <Link to={address({ resource: row.resourceId })}>{row.name}</Link>
          )}
          <Typography.Text style={{ color: colour.textMuted }}>
            {row.kind === null ? '—' : kindSingular(row.kind, t)}
          </Typography.Text>
          <Typography.Text>{t(`shell.studio.overview.change.${row.change}`)}</Typography.Text>
        </div>
      ))}
      {sortedParts?.map((part) => (
        <div key={`${part.type}:${part.id}`} data-testid="studio-change-part" style={{ display: 'contents' }}>
          <Typography.Text>{part.label}</Typography.Text>
          <Typography.Text style={{ color: colour.textMuted }}>
            {t(`shell.studio.release.type.${part.type}`)}
          </Typography.Text>
          <Typography.Text>{partChangeText(part, t)}</Typography.Text>
        </div>
      ))}
      {parts?.length === 0 && (
        <Typography.Text data-testid="studio-change-parts-none" style={{ gridColumn: '1 / -1' }}>
          {t('shell.studio.overview.change.parts.none')}
        </Typography.Text>
      )}
    </div>
  )
}

function partChangeText(part: StudioReleasePartChange, t: (key: string) => string): string {
  return t(`shell.studio.overview.change.part.${part.change}`)
    .replace('{version}', part.version ?? '')
    .replace('{previous}', part.previousVersion ?? '')
}
