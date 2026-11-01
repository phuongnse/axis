import { Button, Typography } from 'antd'
import { folderDiffers, releaseRows, type StudioDiagnostic, type StudioReleases } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed: the open application's state in Studio's band, beside Validate and Publish…, in words alone. Its errors,
 * which stop Publish, and its warnings are one text button that opens the problems panel beside the page. The rest is
 * text in textMuted, as Publish… beside it is what to do: changes the live release lacks, no live release yet, the live
 * release when nothing needs attention, or a release history that did not load. While the history loads, only the
 * problems show.
 */
export function StudioStatus({
  diagnostics,
  releases,
  problemsOpen,
  onProblems,
}: {
  diagnostics: readonly StudioDiagnostic[]
  releases: StudioReleases | 'failed' | undefined
  problemsOpen: boolean
  onProblems: () => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const errors = diagnostics.filter((diagnostic) => diagnostic.severity === 'error').length
  const warnings = diagnostics.length - errors
  const count = (key: string, n: number) => t(`${key}.${n === 1 ? 'one' : 'other'}`).replace('{count}', String(n))

  let state: string | undefined
  if (releases === 'failed') {
    state = t('shell.studio.status.releasesFailed')
  } else if (releases !== undefined) {
    const live = releaseRows(releases).find((row) => row.active)
    if (!live) {
      state = t('shell.studio.status.unpublished')
    } else if (folderDiffers(releases)) {
      state = t('shell.studio.status.changes')
    } else if (diagnostics.length === 0) {
      state = t('shell.studio.status.live').replace('{number}', String(live.number))
    }
  }

  if (diagnostics.length === 0 && state === undefined) {
    return null
  }
  return (
    <div
      role="group"
      aria-label={t('shell.studio.status.label')}
      data-testid="studio-status"
      style={{ display: 'flex', alignItems: 'center', gap: spacing.md, flex: 'none' }}
    >
      {diagnostics.length > 0 && (
        <Button
          type="text"
          aria-expanded={problemsOpen}
          aria-controls="studio-problems"
          onClick={onProblems}
          style={{ paddingInline: spacing.xs }}
        >
          {errors > 0 && (
            <span style={{ color: colour.dangerText }}>{count('shell.studio.status.errors', errors)}</span>
          )}
          {errors > 0 && warnings > 0 && <span style={{ color: colour.textMuted }}> · </span>}
          {warnings > 0 && (
            <span style={{ color: colour.warningText }}>{count('shell.studio.status.warnings', warnings)}</span>
          )}
        </Button>
      )}
      {state !== undefined && (
        <Typography.Text style={{ color: releases === 'failed' ? colour.warningText : colour.textMuted }}>
          {state}
        </Typography.Text>
      )}
    </div>
  )
}
