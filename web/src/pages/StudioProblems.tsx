import { Flex, Tag } from 'antd'
import { AxisButton } from '../platform/AxisButton'
import { EmptyState } from '../platform/EmptyState'
import { SidePanel } from '../platform/SidePanel'
import type { StudioDiagnostic, StudioResourceItem } from '../platform/studio'
import { useText } from '../platform/texts'
import { problemsPanelId } from './StudioBand'
import { ResourceReference } from './ResourceReference'

/**
 * The Problems panel of the open application, drawn in `SidePanel`: a column on the right, under the header, that
 * is not modal. It lists every error, then every warning, each with its code, message and resource. A problem's resource
 * is a link when it names one, and `onSelect` opens that resource at the problem's path while the panel stays open.
 * `onClose` runs from the close button. With no problem, it says so.
 */
export function StudioProblems({
  diagnostics,
  resources,
  onSelect,
  onClose,
}: {
  diagnostics: StudioDiagnostic[]
  resources?: readonly StudioResourceItem[]
  onSelect: (resourceId: string, path: string) => void
  onClose: () => void
}) {
  const t = useText()

  return (
    <SidePanel
      id={problemsPanelId}
      testId="studio-diagnostics"
      closeTestId="studio-problems-close"
      closeNameKey="shell.studio.problems.close"
      title={t('shell.studio.problems.title')}
      onClose={onClose}
    >
      {diagnostics.length === 0 ? (
        <EmptyState text={t('shell.studio.problems.none')} />
      ) : (
        <StudioProblemList diagnostics={diagnostics} resources={resources} onSelect={onSelect} />
      )}
    </SidePanel>
  )
}

/**
 * The problems of a draft or a check, errors first, then warnings, each with its severity, code, message and resource. A
 * problem's resource is a link when `onSelect` is given, see {@link ResourceReference}, and `onSelect` opens that resource
 * at the problem's path. Without `onSelect`, the resource is plain text. A problem that names no resource shows its file.
 * The list has no margin of its own, see docs/reference/shared-widgets.md#which-widget.
 */
export function StudioProblemList({
  diagnostics,
  resources,
  onSelect,
  onOpenFile,
}: {
  diagnostics: StudioDiagnostic[]
  resources?: readonly StudioResourceItem[]
  onSelect?: (resourceId: string, path: string) => void
  /**
   * Proposed, see #3978: opens the file of a problem that names no resource, such as a package's file, at its line.
   * Its file is then a link named by `fileName`, with its line in parentheses.
   */
  onOpenFile?: { name: (diagnostic: StudioDiagnostic) => string; open: (diagnostic: StudioDiagnostic) => void }
}) {
  const ordered = [
    ...diagnostics.filter((diagnostic) => diagnostic.severity === 'error'),
    ...diagnostics.filter((diagnostic) => diagnostic.severity !== 'error'),
  ]
  return (
    <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
      {ordered.map((diagnostic, index) => (
        <li key={index}>
          <DiagnosticRow diagnostic={diagnostic} resources={resources} onSelect={onSelect} onOpenFile={onOpenFile} />
        </li>
      ))}
    </ul>
  )
}

/** One problem: severity, code and message, then its resource, or its file when it names none. */
function DiagnosticRow({
  diagnostic,
  resources,
  onSelect,
  onOpenFile,
}: {
  diagnostic: StudioDiagnostic
  resources?: readonly StudioResourceItem[]
  onSelect?: (resourceId: string, path: string) => void
  onOpenFile?: { name: (diagnostic: StudioDiagnostic) => string; open: (diagnostic: StudioDiagnostic) => void }
}) {
  const t = useText()
  const resourceId = diagnostic.resourceId
  const resource = resourceId === null ? undefined : resources?.find((item) => item.id === resourceId)
  return (
    <div style={{ padding: '4px 15px' }}>
      <Flex vertical align="flex-start" style={{ minWidth: 0, whiteSpace: 'normal' }}>
        <span>
          {/* oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token. */}
          <Tag color={diagnostic.severity === 'error' ? 'error' : 'warning'}>{diagnostic.severity}</Tag>
          <strong>{diagnostic.code}</strong> {diagnostic.message}
        </span>
        {resourceId === null && onOpenFile ? (
          <AxisButton
            role="link"
            onClick={() => onOpenFile.open(diagnostic)}
            style={{ height: 'auto', whiteSpace: 'normal', textAlign: 'start' }}
          >
            {diagnostic.line === undefined
              ? onOpenFile.name(diagnostic)
              : t('shell.studio.package.problemAt')
                  .replace('{file}', onOpenFile.name(diagnostic))
                  .replace('{line}', String(diagnostic.line))}
          </AxisButton>
        ) : (
          <ResourceReference
            resource={resource}
            file={diagnostic.file}
            onOpen={resourceId !== null && onSelect ? () => onSelect(resourceId, diagnostic.path) : undefined}
          />
        )}
      </Flex>
    </div>
  )
}
