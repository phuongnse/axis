import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { folderDiffers, type StudioDiagnostic, type StudioReleases } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/** The id of the Problems panel, which the band's problems button controls. */
export const problemsPanelId = 'studio-problems-panel'

/**
 * The band of an open application: one row as high as the shell's header, which stays under the header while the page
 * scrolls. On its left is the application's state in words. Errors and warnings are one text button, such as "2 errors,
 * 1 warning", that calls `onToggleProblems` and shows only when there is one. After it come the release state, in
 * `textMuted`: "Changes not live" when the folder differs from the live release, "Not published yet" when there is no
 * live release, and "Release 7 is live" otherwise, or "Release 7 is live, off-standard" when the live release is
 * off-standard. "Releases did not load" is in `warningText` when `history` is `'failed'`, and nothing shows while
 * `history` is `undefined`. No state has an icon or a link. `children` are the actions on the right.
 */
export function StudioBand({
  diagnostics,
  history,
  problemsOpen,
  onToggleProblems,
  children,
}: {
  diagnostics: StudioDiagnostic[]
  history: StudioReleases | 'failed' | undefined
  problemsOpen: boolean
  onToggleProblems: () => void
  children?: ReactNode
}) {
  const t = useText()
  const { colour, size, spacing } = useDesignTokens()
  const errors = diagnostics.filter((diagnostic) => diagnostic.severity === 'error').length
  const warnings = diagnostics.length - errors
  const counts = (count: number, one: string, many: string) =>
    count === 1 ? t(one) : t(many).replace('{count}', String(count))

  return (
    <Flex
      align="center"
      justify="space-between"
      gap="middle"
      data-testid="studio-band"
      style={{
        position: 'sticky',
        top: size.bar,
        zIndex: 5,
        height: size.bar,
        margin: `-${spacing.md}px -${spacing.md}px 0`,
        paddingInline: spacing.md,
        background: colour.surface,
        borderBottom: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex align="center" gap="middle" style={{ minWidth: 0 }}>
        {errors + warnings > 0 && (
          <AxisButton
            role="plain"
            data-testid="studio-problems"
            aria-expanded={problemsOpen}
            aria-controls={problemsPanelId}
            onClick={onToggleProblems}
          >
            {errors > 0 && (
              <span style={{ color: colour.dangerText }}>
                {counts(errors, 'shell.studio.problems.error', 'shell.studio.problems.errors')}
              </span>
            )}
            {errors > 0 && warnings > 0 && ', '}
            {warnings > 0 && (
              <span style={{ color: colour.warningText }}>
                {counts(warnings, 'shell.studio.problems.warning', 'shell.studio.problems.warnings')}
              </span>
            )}
          </AxisButton>
        )}
        {history === 'failed' ? (
          <Typography.Text data-testid="studio-releases-error" style={{ color: colour.warningText }}>
            {t('shell.studio.band.releasesFailed')}
          </Typography.Text>
        ) : (
          history !== undefined && (
            <Typography.Text
              data-testid={folderDiffers(history) ? 'studio-folder-differs' : undefined}
              style={{ color: colour.textMuted }}
            >
              {history.active === null
                ? t('shell.studio.band.notPublished')
                : folderDiffers(history)
                  ? t('shell.studio.band.changesNotLive')
                  : t(
                      history.items.find((item) => item.releaseId === history.active?.releaseId)?.offStandard
                        ? 'shell.studio.band.liveOffStandard'
                        : 'shell.studio.band.live',
                    ).replace('{release}', String(history.active.number))}
            </Typography.Text>
          )
        )}
      </Flex>
      {children}
    </Flex>
  )
}
