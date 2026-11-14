import { Flex, Tag, Typography } from 'antd'
import { useContext, type ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { resourceTitle, type StudioDiagnostic, type StudioResourceItem } from '../platform/studio'
import { kindSingular } from '../platform/studioKinds'
import { PlatformContext } from './context'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { problemsPanelId } from './StudioBand'
import { StudioSidePanel } from './StudioSidePanel'

/**
 * The Problems panel of the open application, in the shared `StudioSidePanel`. It lists every error, then every warning, each with its code, its message and what it is about. A problem that names a
 * resource links it, and `onSelect` opens that resource at the problem's path while the panel stays open.
 * `onClose` runs from the close button. With no problem, it says so.
 */
export function StudioProblems({
  diagnostics,
  resources,
  onSelect,
  onClose,
}: {
  diagnostics: StudioDiagnostic[]
  /** The application's resources, which name each problem's reference. */
  resources?: readonly StudioResourceItem[]
  onSelect: (resourceId: string, path: string) => void
  onClose: () => void
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const ordered = [
    ...diagnostics.filter((diagnostic) => diagnostic.severity === 'error'),
    ...diagnostics.filter((diagnostic) => diagnostic.severity !== 'error'),
  ]

  return (
    <StudioSidePanel
      id={problemsPanelId}
      title={t('shell.studio.problems.title')}
      closeKey="shell.studio.problems.close"
      onClose={onClose}
      testId="studio-diagnostics"
      closeTestId="studio-problems-close"
    >
      {ordered.length === 0 ? (
        <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.problems.none')}</Typography.Text>
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {ordered.map((diagnostic, index) => (
            <li key={index}>
              <StudioDiagnosticRow diagnostic={diagnostic} resources={resources} onSelect={onSelect} />
            </li>
          ))}
        </ul>
      )}
    </StudioSidePanel>
  )
}

/**
 * One problem: its severity and code, or a `lead` in their place, its message as text, and under it what it is about.
 * Proposed: the reference alone is a link, named as people read it, the resource's title and kind, such as
 * "Requests (page)", and it opens that resource at the problem's path; one that names no resource is its file, as
 * text, see the review on #3263. A request's page shows its checks' problems and its review's findings with it too.
 */
export function StudioDiagnosticRow({
  diagnostic,
  onSelect,
  resources = [],
  lead,
}: {
  diagnostic: StudioDiagnostic
  onSelect: (resourceId: string, path: string) => void
  /** The application's resources, which name the reference. */
  resources?: readonly StudioResourceItem[]
  /** Proposed: what leads the message in place of its severity and code, such as a finding's "Must change". */
  lead?: ReactNode
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const resourceId = diagnostic.resourceId
  const resource = resourceId === null ? undefined : resources.find((item) => item.id === resourceId)
  const reference =
    resource === undefined
      ? diagnostic.file
      : t('shell.studio.reference')
          .replace('{name}', resourceTitle(resource, locale, t))
          .replace('{kind}', kindSingular(resource.kind, t).toLowerCase())
  return (
    <Flex vertical align="flex-start" style={{ minWidth: 0, paddingBlock: spacing.xs / 2 }}>
      <span>
        {lead !== undefined ? (
          <>{lead} </>
        ) : (
          <>
            {/* oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token. */}
            <Tag color={diagnostic.severity === 'error' ? 'error' : 'warning'}>{diagnostic.severity}</Tag>
            <strong>{diagnostic.code}</strong>{' '}
          </>
        )}
        {diagnostic.message}
      </span>
      {resourceId === null ? (
        <Typography.Text style={{ color: colour.textMuted }}>{reference}</Typography.Text>
      ) : (
        <AxisButton role="link" onClick={() => onSelect(resourceId, diagnostic.path)}>
          {reference}
        </AxisButton>
      )}
    </Flex>
  )
}
