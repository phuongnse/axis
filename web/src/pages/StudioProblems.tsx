import { Flex } from 'antd'
import { EmptyState } from '../platform/EmptyState'
import { SidePanel } from '../platform/SidePanel'
import { StatusTag } from '../platform/StatusTag'
import { severityText, severityTone, type StudioDiagnostic, type StudioResourceItem } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { problemsPanelId } from './StudioBand'
import type { FileAddressOf, ResourceAddressOf } from '../platform/studioAddress'
import { FileAtLine } from './FileAtLine'
import { ResourceReference } from './ResourceReference'

/**
 * The Problems panel of the open application, drawn in `SidePanel`: a column on the right, under the header, that
 * is not modal. It lists every error, then every warning, each with its code, message and resource. A problem's resource
 * is a link when it names one, to the address `addressOf` gives for that resource at the problem's path.
 * `onClose` runs from the close button. With no problem, it says so.
 */
export function StudioProblems({
  diagnostics,
  resources,
  addressOf,
  onClose,
}: {
  diagnostics: StudioDiagnostic[]
  resources?: readonly StudioResourceItem[]
  addressOf: ResourceAddressOf
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
        <StudioProblemList diagnostics={diagnostics} resources={resources} addressOf={addressOf} />
      )}
    </SidePanel>
  )
}

/**
 * The problems of a draft or a check, errors first, then warnings, each with its severity, code, message and resource. A
 * problem's resource is a link when `addressOf` is given, see {@link ResourceReference}, to the address of that resource
 * at the problem's path. Without it, the resource is plain text. A problem that names no resource shows its file, as a
 * link when `fileAddressOf` is given, see {@link FileAtLine}, to the address of that file at the problem's line.
 * The list has no margin of its own, see docs/reference/shared-widgets.md#which-widget.
 */
export function StudioProblemList({
  diagnostics,
  resources,
  addressOf,
  fileAddressOf,
}: {
  diagnostics: StudioDiagnostic[]
  resources?: readonly StudioResourceItem[]
  addressOf?: ResourceAddressOf
  fileAddressOf?: FileAddressOf
}) {
  const ordered = [
    ...diagnostics.filter((diagnostic) => diagnostic.severity === 'error'),
    ...diagnostics.filter((diagnostic) => diagnostic.severity !== 'error'),
  ]
  return (
    <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
      {ordered.map((diagnostic, index) => (
        <li key={index}>
          <DiagnosticRow
            diagnostic={diagnostic}
            resources={resources}
            addressOf={addressOf}
            fileAddressOf={fileAddressOf}
          />
        </li>
      ))}
    </ul>
  )
}

/** One problem: severity, code and message, then its resource, or its file, with its line, when it names none. */
function DiagnosticRow({
  diagnostic,
  resources,
  addressOf,
  fileAddressOf,
}: {
  diagnostic: StudioDiagnostic
  resources?: readonly StudioResourceItem[]
  addressOf?: ResourceAddressOf
  fileAddressOf?: FileAddressOf
}) {
  const t = useText()
  const resourceId = diagnostic.resourceId
  const resource = resourceId === null ? undefined : resources?.find((item) => item.id === resourceId)
  const { spacing } = useDesignTokens()
  return (
    // Proposed, #4902: the severity stands apart from what it says, `spacing-xs` away, with the text in its own column so
    // a long message wraps under itself, and the resource or file follows the message on its line rather than below it.
    <div style={{ padding: '4px 15px' }}>
      <Flex align="baseline" gap={spacing.xs} style={{ minWidth: 0, whiteSpace: 'normal' }}>
        <StatusTag tone={severityTone(diagnostic.severity)}>{severityText(t, diagnostic.severity)}</StatusTag>
        <span style={{ flex: 1, minWidth: 0 }}>
          <strong>{diagnostic.code}</strong> {diagnostic.message}{' '}
          <span>
            {resource === undefined && fileAddressOf ? (
              <FileAtLine
                file={diagnostic.file}
                line={diagnostic.line}
                href={fileAddressOf(diagnostic.file, diagnostic.line)}
              />
            ) : (
              <ResourceReference
                resource={resource}
                file={diagnostic.file}
                href={resourceId !== null && addressOf ? addressOf(resourceId, diagnostic.path) : undefined}
              />
            )}
          </span>
        </span>
      </Flex>
    </div>
  )
}
