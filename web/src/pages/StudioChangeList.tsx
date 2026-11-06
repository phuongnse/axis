import { Typography } from 'antd'
import { Link } from 'react-router'
import type { StudioChangeRow } from '../platform/studio'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * The change list of Studio: one row for each file that differs, with its name, its kind and what changed. A name opens
 * its resource through `address`, except a removed file's, which the folder lacks and shows as plain text. With
 * `plainChanged`, which a release page sets, a changed resource is plain text too, because the folder's version is not
 * the one the release changed.
 */
export function StudioChangeList({
  rows,
  address,
  plainChanged = false,
}: {
  rows: StudioChangeRow[]
  address: (extra: Record<string, string>) => string
  plainChanged?: boolean
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
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
    </div>
  )
}
