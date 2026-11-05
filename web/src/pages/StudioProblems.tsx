import { CloseOutlined } from '@ant-design/icons'
import { Button, Flex, Tag, Typography } from 'antd'
import { AxisButton } from '../platform/AxisButton'
import type { StudioDiagnostic } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { problemsPanelId } from './StudioBand'

/** The width of the Problems panel in px. */
const panelWidth = 320

/**
 * The Problems panel of the open application: a column beside the page, under the header and the band, that is not
 * modal. It lists every error, then every warning, each with its code, message, file and path. A problem that names a
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
  const { colour, size, spacing } = useDesignTokens()
  const ordered = [
    ...diagnostics.filter((diagnostic) => diagnostic.severity === 'error'),
    ...diagnostics.filter((diagnostic) => diagnostic.severity !== 'error'),
  ]
  const top = size.bar * 2 + spacing.md

  return (
    <aside
      id={problemsPanelId}
      data-testid="studio-diagnostics"
      aria-label={t('shell.studio.problems.title')}
      style={{
        position: 'sticky',
        top,
        alignSelf: 'flex-start',
        flex: 'none',
        width: panelWidth,
        maxHeight: `calc(100vh - ${top + spacing.md}px)`,
        overflowY: 'auto',
        padding: spacing.sm,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex vertical gap="small">
        <Flex justify="space-between" align="center">
          <Typography.Title level={5} style={{ margin: 0 }}>
            {t('shell.studio.problems.title')}
          </Typography.Title>
          <AxisButton
            role="icon"
            nameKey="shell.studio.problems.close"
            icon={<CloseOutlined />}
            data-testid="studio-problems-close"
            onClick={onClose}
          />
        </Flex>
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
      </Flex>
    </aside>
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
        <Tag color={diagnostic.severity === 'error' ? 'error' : 'warning'}>{diagnostic.severity}</Tag>
        <strong>{diagnostic.code}</strong> {diagnostic.message}
      </span>
      <Typography.Text type="secondary">
        {diagnostic.file}
        {diagnostic.path !== '' && ` · ${t('shell.studio.path')}: ${diagnostic.path}`}
      </Typography.Text>
    </Flex>
  )
  const resourceId = diagnostic.resourceId
  return resourceId === null ? (
    <div style={{ padding: '4px 15px' }}>{content}</div>
  ) : (
    <Button
      type="text"
      style={{ height: 'auto', textAlign: 'left', width: '100%' }}
      onClick={() => onSelect(resourceId, diagnostic.path)}
    >
      {content}
    </Button>
  )
}
