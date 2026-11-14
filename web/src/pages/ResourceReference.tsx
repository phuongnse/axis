import { Flex, Typography } from 'antd'
import { useContext } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { resourceTitle, type StudioResourceItem } from '../platform/studio'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/**
 * Proposed: a pointer to a resource, see the review on #3263: the resource's title in the reader's language as the only
 * link, opening it at `path`, and its kind after it, muted, as the changes of a draft name a resource. A pointer to no
 * resource a person can open is its file, muted.
 */
export function ResourceReference({
  resourceId,
  file,
  path,
  resources,
  onSelect,
}: {
  resourceId: string | null
  file: string | null
  path: string
  resources: readonly StudioResourceItem[]
  onSelect: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  if (resourceId === null) {
    return file === null ? null : <Typography.Text style={{ color: colour.textMuted }}>{file}</Typography.Text>
  }
  const resource = resources.find((entry) => entry.id === resourceId)
  return (
    <Flex component="span" align="baseline" gap={spacing.xs} wrap style={{ display: 'inline-flex' }}>
      <AxisButton role="link" onClick={() => onSelect(resourceId, path)}>
        {resource === undefined ? (file ?? resourceId) : resourceTitle(resource, locale, t)}
      </AxisButton>
      {resource !== undefined && (
        <Typography.Text style={{ color: colour.textMuted }}>{kindSingular(resource.kind, t)}</Typography.Text>
      )}
    </Flex>
  )
}
