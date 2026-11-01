import { Drawer, Flex, Tag, Typography } from 'antd'
import { Link } from 'react-router'
import type { StudioDiagnostic } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed: the folder's problems beside the page, opened from the band's error and warning counts. It does not block
 * the page: opening a problem opens its resource at the place it names, and the panel stays open with the list, which
 * follows the diagnostics as the author fixes them. Its close button and Escape close it.
 */
export function StudioProblemsPanel({
  open,
  diagnostics,
  addressOf,
  onClose,
}: {
  open: boolean
  diagnostics: readonly StudioDiagnostic[]
  addressOf: (diagnostic: StudioDiagnostic) => string | null
  onClose: () => void
}) {
  const t = useText()
  return (
    <Drawer
      id="studio-problems"
      data-testid="studio-problems"
      open={open}
      mask={false}
      size={420}
      // Proposed: it opens under the shell's header and the band, 48 px each, so Validate and Publish… stay in reach.
      rootStyle={{ top: 96 }}
      title={t('shell.studio.problems.title')}
      onClose={onClose}
    >
      <ProblemList diagnostics={diagnostics} addressOf={addressOf} />
    </Drawer>
  )
}

/** The folder's diagnostics, errors first, under one line that says whether they stop Publish. */
export function ProblemList({
  diagnostics,
  addressOf,
}: {
  diagnostics: readonly StudioDiagnostic[]
  addressOf: (diagnostic: StudioDiagnostic) => string | null
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const errors = diagnostics.filter((diagnostic) => diagnostic.severity === 'error')
  const ordered = [...errors, ...diagnostics.filter((diagnostic) => diagnostic.severity !== 'error')]
  let summary = 'shell.studio.overview.attention.none'
  if (errors.length > 0) {
    summary = 'shell.studio.overview.attention.errors'
  } else if (diagnostics.length > 0) {
    summary = 'shell.studio.overview.attention.warnings'
  }
  return (
    <Flex vertical gap={spacing.sm}>
      <Typography.Text style={{ color: errors.length > 0 ? colour.dangerText : colour.textMuted }}>
        {t(summary)}
      </Typography.Text>
      {ordered.length > 0 && (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {ordered.map((diagnostic, index) => (
            <li
              key={index}
              style={{
                paddingBlock: spacing.xs,
                borderTop: index > 0 ? `1px solid ${colour.divider}` : undefined,
              }}
            >
              <DiagnosticRow diagnostic={diagnostic} to={addressOf(diagnostic)} />
            </li>
          ))}
        </ul>
      )}
    </Flex>
  )
}

/** One diagnostic: its severity and message, then its code and where it is, a link to that place when it is in a resource. */
function DiagnosticRow({ diagnostic, to }: { diagnostic: StudioDiagnostic; to: string | null }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const where = `${diagnostic.file}${diagnostic.path !== '' ? ` · ${t('shell.studio.path')}: ${diagnostic.path}` : ''}`
  return (
    <Flex gap={spacing.xs} align="baseline">
      <Tag color={diagnostic.severity === 'error' ? 'error' : 'warning'} style={{ marginInlineEnd: 0 }}>
        {diagnostic.severity}
      </Tag>
      <Flex vertical style={{ minWidth: 0 }}>
        <Typography.Text>{diagnostic.message}</Typography.Text>
        <span style={{ color: colour.textMuted }}>
          {diagnostic.code} ·{' '}
          {to ? (
            <Link to={to} style={{ color: colour.primaryText }}>
              {where}
            </Link>
          ) : (
            where
          )}
        </span>
      </Flex>
    </Flex>
  )
}
