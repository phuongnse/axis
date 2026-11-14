import { Flex, Typography } from 'antd'
import { useContext, type ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { resourceTitle, type StudioDiagnostic, type StudioResourceItem } from '../platform/studio'
import { kindSingular } from '../platform/studioKinds'
import { PlatformContext } from './context'
import { useDiagnosticItems } from './useDiagnosticItems'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { problemsPanelId } from './StudioBand'
import { StudioSidePanel } from './StudioSidePanel'

/**
 * The Problems panel of the open application, in the shared `StudioSidePanel`. It lists every error, then every warning,
 * grouped by the resource they are about in a `StudioProblemList`, and `onSelect` opens that resource at a problem's
 * path while the panel stays open.
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
  const diagnosticItems = useDiagnosticItems()
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
        <StudioProblemList items={diagnosticItems(ordered)} resources={resources} onSelect={onSelect} />
      )}
    </StudioSidePanel>
  )
}

/** Proposed: one problem of a `StudioProblemList`: what leads it, its words and the place it is about. */
export interface StudioProblemItem {
  /** What leads its words, such as "Error AX0412" or "Must change". */
  lead: ReactNode
  message: string
  resourceId: string | null
  file: string
  path: string
}

/**
 * Proposed: problems grouped by the resource they are about, as an editor's problems panel groups them by file, see the
 * review on #3263. Each resource shows once, its name as the only link, opening it at its first problem's path, and its
 * kind muted after it, as the changes of a draft name a resource; one that names no resource is its file, muted. Its
 * problems follow, indented, each led by its `lead`, its words as text, and, after the first, a Show link to its own
 * place. The Problems panel, a request's checks and its review all list their problems with it.
 */
export function StudioProblemList({
  items,
  resources = [],
  onSelect,
  testId,
}: {
  items: readonly StudioProblemItem[]
  /** The application's resources, which name each group. */
  resources?: readonly StudioResourceItem[]
  onSelect: (resourceId: string, path: string) => void
  testId?: string
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const groups: { key: string; resourceId: string | null; file: string; items: StudioProblemItem[] }[] = []
  for (const item of items) {
    const key = item.resourceId ?? `file:${item.file}`
    const group = groups.find((entry) => entry.key === key)
    if (group === undefined) {
      groups.push({ key, resourceId: item.resourceId, file: item.file, items: [item] })
    } else {
      group.items.push(item)
    }
  }
  return (
    <ul style={{ listStyle: 'none', margin: 0, padding: 0 }} data-testid={testId}>
      {groups.map((group) => {
        const resource =
          group.resourceId === null ? undefined : resources.find((entry) => entry.id === group.resourceId)
        const first = group.items[0]
        return (
          <li key={group.key} style={{ paddingBlock: spacing.xs / 2 }} data-testid="studio-problem-group">
            <Flex align="baseline" gap={spacing.xs} wrap>
              {group.resourceId === null ? (
                <Typography.Text style={{ color: colour.textMuted }}>{group.file}</Typography.Text>
              ) : (
                <>
                  <AxisButton role="link" onClick={() => onSelect(group.resourceId!, first.path)}>
                    {resource === undefined ? group.file : resourceTitle(resource, locale, t)}
                  </AxisButton>
                  {resource !== undefined && (
                    <Typography.Text style={{ color: colour.textMuted }}>
                      {kindSingular(resource.kind, t)}
                    </Typography.Text>
                  )}
                </>
              )}
            </Flex>
            <ul style={{ listStyle: 'none', margin: 0, paddingInlineStart: spacing.md }}>
              {group.items.map((item, index) => (
                <li key={index} data-testid="studio-problem">
                  {item.lead} <Typography.Text>{item.message}</Typography.Text>
                  {index > 0 && group.resourceId !== null && (
                    <>
                      {' '}
                      <AxisButton role="link" onClick={() => onSelect(group.resourceId!, item.path)}>
                        {t('shell.studio.problems.show')}
                      </AxisButton>
                    </>
                  )}
                </li>
              ))}
            </ul>
          </li>
        )
      })}
    </ul>
  )
}
