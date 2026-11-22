import { Flex } from 'antd'
import { EmptyState } from '../platform/EmptyState'
import { SidePanel } from '../platform/SidePanel'
import { StatusTag } from '../platform/StatusTag'
import { severityText, severityTone, type StudioDiagnostic, type StudioResourceItem } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { problemsPanelId } from './StudioBand'
import { FileAtLine } from './FileAtLine'
import { ResourceReference } from './ResourceReference'

/**
 * The Problems panel of the open application, drawn in `SidePanel`: a column on the right, under the header, that
 * is not modal. It lists every error, then every warning, each with its code, message and resource. A problem's resource
 * is a real link to the resource at the problem's path when `addressOf` is given, and it opens in a new tab with Ctrl+click
 * while the panel stays open. `fileAddressOf` links a package file's place to the file, see {@link StudioProblemList}.
 * `onClose` runs from the close button. With no problem, it says so.
 */
export function StudioProblems({
  diagnostics,
  resources,
  addressOf,
  fileAddressOf,
  onClose,
}: {
  diagnostics: StudioDiagnostic[]
  resources?: readonly StudioResourceItem[]
  addressOf?: (resourceId: string, path: string) => string
  fileAddressOf?: (file: string, line?: number) => string | undefined
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
        <StudioProblemList
          diagnostics={diagnostics}
          resources={resources}
          addressOf={addressOf}
          fileAddressOf={fileAddressOf}
        />
      )}
    </SidePanel>
  )
}

/**
 * The problems of a draft or a check, errors first, then warnings, each with its severity, code, message and resource. The
 * severity tag stands apart from the text, which is a column of its own, so a long message wraps under itself. The resource
 * follows the message on its line. A problem's resource is a link when `addressOf` is given, see {@link ResourceReference},
 * and `addressOf` gives the address of that resource at the problem's path. Without `addressOf`, the resource is plain text.
 * A problem that names no resource shows its file, see {@link FileAtLine}. With `fileAddressOf` it also shows the line, and
 * the file is a link when `fileAddressOf` gives an address for it, which opens the file at the line, as for a package's
 * file. Any other file is plain text. The list has no margin of its own, see
 * docs/reference/shared-widgets.md#which-widget and docs/reference/actions-and-controls.md#opening-things.
 */
export function StudioProblemList({
  diagnostics,
  resources,
  addressOf,
  fileAddressOf,
}: {
  diagnostics: StudioDiagnostic[]
  resources?: readonly StudioResourceItem[]
  addressOf?: (resourceId: string, path: string) => string
  fileAddressOf?: (file: string, line?: number) => string | undefined
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
  addressOf?: (resourceId: string, path: string) => string
  fileAddressOf?: (file: string, line?: number) => string | undefined
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const resourceId = diagnostic.resourceId
  const resource = resourceId === null ? undefined : resources?.find((item) => item.id === resourceId)
  return (
    <div style={{ padding: '4px 15px' }}>
      <Flex gap={spacing.xs} align="baseline">
        <StatusTag tone={severityTone(diagnostic.severity)}>{severityText(t, diagnostic.severity)}</StatusTag>
        <div data-testid="studio-problem-text" style={{ flex: 1, minWidth: 0, whiteSpace: 'normal' }}>
          <strong>{diagnostic.code}</strong> {diagnostic.message}{' '}
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
        </div>
      </Flex>
    </div>
  )
}
