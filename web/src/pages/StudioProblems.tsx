import { Button, Flex, Tag, Typography } from 'antd'
import type { StudioDiagnostic } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { problemsPanelId } from './StudioBand'
import { StudioSidePanel } from './StudioSidePanel'

/**
 * The Problems panel of the open application, in the shared `StudioSidePanel`. It lists every error, then every warning, each with its code, message, file and path. A problem that names a
 * resource is a button, and `onSelect` opens that resource at the problem's path while the panel stays open.
 * `onClose` runs from the close button. With no problem, it says so.
 */
export function StudioProblems({
  diagnostics,
  onSelect,
  onClose,
}: {
  diagnostics: StudioDiagnostic[]
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
              <DiagnosticRow diagnostic={diagnostic} onSelect={onSelect} />
            </li>
          ))}
        </ul>
      )}
    </StudioSidePanel>
  )
}

/** One problem: severity, code, message, file and path. It is a button when it names a resource. */
function DiagnosticRow({
  diagnostic,
  onSelect,
}: {
  diagnostic: StudioDiagnostic
  onSelect: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const content = (
    <Flex vertical align="flex-start" style={{ minWidth: 0, whiteSpace: 'normal' }}>
      <span>
        {/* oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token. */}
        <Tag color={diagnostic.severity === 'error' ? 'error' : 'warning'}>{diagnostic.severity}</Tag>
        <strong>{diagnostic.code}</strong> {diagnostic.message}
      </span>
      <Typography.Text type="secondary">
        {diagnostic.file}
        {diagnostic.path !== '' && `, ${t('shell.studio.path')}: ${diagnostic.path}`}
      </Typography.Text>
    </Flex>
  )
  const resourceId = diagnostic.resourceId
  return resourceId === null ? (
    <div style={{ padding: '4px 15px' }}>{content}</div>
  ) : (
    // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
    <Button
      type="text"
      style={{ height: 'auto', textAlign: 'left', width: '100%' }}
      onClick={() => onSelect(resourceId, diagnostic.path)}
    >
      {content}
    </Button>
  )
}
