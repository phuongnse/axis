import { Flex } from 'antd'
import { Link } from 'react-router'
import { EmptyState } from '../platform/EmptyState'
import { SidePanel } from '../platform/SidePanel'
import { StatusTag } from '../platform/StatusTag'
import { severityText, severityTone, type StudioDiagnostic, type StudioResourceItem } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useBulletList } from '../platform/useBulletList'
import { problemsPanelId } from './StudioBand'
import { FileAtLine } from './FileAtLine'
import { ResourceReference } from './ResourceReference'

/**
 * The Problems panel of the open application, drawn in `SidePanel`: a column on the right, under the header, that
 * is not modal. It lists every problem once under its resource, or its file, see {@link StudioProblemList}. A resource is
 * a real link to the resource at its first problem's path when `addressOf` is given, and it opens in a new tab with Ctrl+click
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

/** The problems that share one resource, or one file when they name no resource, in the order they are listed. */
type ProblemGroup = { diagnostics: StudioDiagnostic[]; resource: StudioResourceItem | undefined }

/**
 * Groups the problems, see {@link StudioProblemList}. A problem falls in the group of its resource when `resources` holds
 * it, and in the group of its file otherwise. Within a group, errors come first, then the rest, each in the order given.
 * Groups with an error come first, then the others, each in the order its first problem came.
 */
function problemGroups(diagnostics: StudioDiagnostic[], resources?: readonly StudioResourceItem[]): ProblemGroup[] {
  const groups = new Map<string, ProblemGroup>()
  for (const diagnostic of diagnostics) {
    const resource =
      diagnostic.resourceId === null ? undefined : resources?.find((item) => item.id === diagnostic.resourceId)
    const key = resource ? `resource:${resource.id}` : `file:${diagnostic.file}`
    const group = groups.get(key) ?? { resource, diagnostics: [] }
    group.diagnostics.push(diagnostic)
    groups.set(key, group)
  }
  const sorted = [...groups.values()].map((group) => ({
    diagnostics: [
      ...group.diagnostics.filter((diagnostic) => diagnostic.severity === 'error'),
      ...group.diagnostics.filter((diagnostic) => diagnostic.severity !== 'error'),
    ],
    resource: group.resource,
  }))
  return [
    ...sorted.filter((group) => group.diagnostics[0].severity === 'error'),
    ...sorted.filter((group) => group.diagnostics[0].severity !== 'error'),
  ]
}

/**
 * The problems of a draft or a check, grouped: each resource, or each file that names none, shows once, as a heading, with its
 * problems as bullets of `useBulletList` under it. Within a group errors come first, and groups with an error come first, see
 * {@link problemGroups}. Each problem has its severity, code and message, and no resource or file of its own. The heading is a
 * link to the group's first problem's place, and every later problem has a "Show" link to its own place, when the heading is a
 * link. A resource's place is `addressOf` at the problem's path. A package file's place is `fileAddressOf`, which the heading
 * shows without a line. Any other file is plain text, see {@link FileAtLine} and {@link ResourceReference}. The list has no
 * margin of its own, see docs/reference/shared-widgets.md#which-widget and docs/reference/actions-and-controls.md#opening-things.
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
  const bullets = useBulletList()
  const placeOf = (group: ProblemGroup, diagnostic: StudioDiagnostic) =>
    group.resource ? addressOf?.(group.resource.id, diagnostic.path) : fileAddressOf?.(diagnostic.file, diagnostic.line)
  return (
    <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
      {problemGroups(diagnostics, resources).map((group, groupIndex) => {
        const [first] = group.diagnostics
        const firstPlace = placeOf(group, first)
        return (
          <li key={groupIndex} data-testid="studio-problem-group">
            <div style={{ paddingBlock: 4 }}>
              {group.resource === undefined && fileAddressOf ? (
                <FileAtLine file={first.file} href={firstPlace} />
              ) : (
                <ResourceReference resource={group.resource} file={first.file} href={firstPlace} />
              )}
            </div>
            <ul style={bullets.list}>
              {group.diagnostics.map((diagnostic, index) => (
                <li key={index} data-testid="studio-problem" style={bullets.item}>
                  <DiagnosticRow diagnostic={diagnostic} show={index === 0 ? undefined : placeOf(group, diagnostic)} />
                </li>
              ))}
            </ul>
          </li>
        )
      })}
    </ul>
  )
}

/** One problem: its severity, code and message. `show` is the place of a later problem in its group, which its "Show" link opens. */
function DiagnosticRow({ diagnostic, show }: { diagnostic: StudioDiagnostic; show?: string }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  return (
    <div style={{ paddingBlock: 4 }}>
      <Flex gap={spacing.xs} align="baseline">
        <StatusTag tone={severityTone(diagnostic.severity)}>{severityText(t, diagnostic.severity)}</StatusTag>
        <div data-testid="studio-problem-text" style={{ flex: 1, minWidth: 0, whiteSpace: 'normal' }}>
          <strong>{diagnostic.code}</strong> {diagnostic.message}
          {show && (
            <>
              {' '}
              <Link to={show}>{t('shell.studio.problems.show')}</Link>
            </>
          )}
        </div>
      </Flex>
    </div>
  )
}
