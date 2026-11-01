import { CloseCircleOutlined, CloudUploadOutlined, ExclamationCircleOutlined } from '@ant-design/icons'
import { Flex } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { folderDiffers, type StudioDiagnostic, type StudioReleases } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed: the open application's state in Studio's band, on every page but the Overview, beside Validate and
 * Publish…: its errors, which stop Publish, its warnings, and whether the folder has changes the live release lacks,
 * or that it has no live release yet.
 * Each opens the Overview, which lists them. Nothing shows when nothing needs attention.
 */
export function StudioStatus({
  diagnostics,
  releases,
  overview,
}: {
  diagnostics: readonly StudioDiagnostic[]
  releases: StudioReleases | 'failed' | undefined
  /** The Overview's address. */
  overview: string
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const errors = diagnostics.filter((diagnostic) => diagnostic.severity === 'error').length
  const warnings = diagnostics.length - errors
  const changes = releases !== undefined && releases !== 'failed' && folderDiffers(releases)
  // Proposed: an application with no live release says so, as no site runs it yet.
  const unpublished = releases !== undefined && releases !== 'failed' && releases.active === null
  const count = (key: string, n: number) => t(`${key}.${n === 1 ? 'one' : 'other'}`).replace('{count}', String(n))
  const entries: { key: string; icon: ReactNode; text: string; colour: string }[] = []
  if (errors > 0) {
    entries.push({
      key: 'errors',
      icon: <CloseCircleOutlined aria-hidden />,
      text: count('shell.studio.status.errors', errors),
      colour: colour.dangerText,
    })
  }
  if (warnings > 0) {
    entries.push({
      key: 'warnings',
      icon: <ExclamationCircleOutlined aria-hidden />,
      text: count('shell.studio.status.warnings', warnings),
      colour: colour.warningText,
    })
  }
  if (changes) {
    entries.push({
      key: 'changes',
      icon: <CloudUploadOutlined aria-hidden />,
      text: t('shell.studio.status.changes'),
      colour: colour.textMuted,
    })
  }
  if (unpublished) {
    entries.push({
      key: 'unpublished',
      icon: <CloudUploadOutlined aria-hidden />,
      text: t('shell.studio.status.unpublished'),
      colour: colour.textMuted,
    })
  }
  if (entries.length === 0) {
    return null
  }
  return (
    <Flex
      component="ul"
      aria-label={t('shell.studio.status.label')}
      data-testid="studio-status"
      gap={spacing.md}
      style={{ listStyle: 'none', margin: 0, padding: 0, flex: 'none' }}
    >
      {entries.map((entry) => (
        <li key={entry.key}>
          <Link to={overview} style={{ color: entry.colour, display: 'inline-flex', gap: spacing.xs }}>
            {entry.icon}
            {entry.text}
          </Link>
        </li>
      ))}
    </Flex>
  )
}
