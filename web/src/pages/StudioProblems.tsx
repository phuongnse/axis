import { Flex } from 'antd'
import { AxisButton } from '../platform/AxisButton'
import { EmptyState } from '../platform/EmptyState'
import { SidePanel } from '../platform/SidePanel'
import { StatusTag } from '../platform/StatusTag'
import type { StudioDiagnostic, StudioResourceItem } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
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

/**
 * One problem: severity as a `StatusTag`, code and message, then its resource, or its file when it names none, on the
 * same row when it fits.
 */
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
  const { spacing } = useDesignTokens()
  const resourceId = diagnostic.resourceId
  const resource = resourceId === null ? undefined : resources?.find((item) => item.id === resourceId)
  return (
    <div style={{ padding: '4px 15px' }}>
      {/* Proposed, see #3978: the place follows the message on its row when the row has room, and the row under it when not. */}
      <Flex wrap align="baseline" style={{ minWidth: 0, whiteSpace: 'normal', columnGap: spacing.md }}>
        <span>
          <span style={{ marginInlineEnd: spacing.sm }}>
            <StatusTag tone={diagnostic.severity === 'error' ? 'danger' : 'attention'}>{diagnostic.severity}</StatusTag>
          </span>
          <strong>{diagnostic.code}</strong> {diagnostic.message}
        </span>
        {resourceId === null && onOpenFile ? (
          <FileAtLine
            file={onOpenFile.name(diagnostic)}
            line={diagnostic.line}
            onOpen={() => onOpenFile.open(diagnostic)}
          />
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

/**
 * Proposed, see #3978: a link to a file that no resource holds, such as a package's file, named by the file and, when
 * known, its line in parentheses. A problem's file and a failed test's place are drawn by it.
 */
export function FileAtLine({ file, line, onOpen }: { file: string; line?: number; onOpen: () => void }) {
  const t = useText()
  return (
    <AxisButton role="link" onClick={onOpen} style={{ height: 'auto', whiteSpace: 'normal', textAlign: 'start' }}>
      {line === undefined
        ? file
        : t('shell.studio.package.problemAt').replace('{file}', file).replace('{line}', String(line))}
    </AxisButton>
  )
}
