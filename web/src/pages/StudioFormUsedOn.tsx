import { Flex, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { EmptyState } from '../platform/EmptyState'
import {
  relationTextKey,
  type StudioReference,
  type StudioReferenceNode,
  type StudioResourceItem,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { ResourceReference } from './ResourceReference'

/** Opens a resource. `path` is set for a reference entry, where the reference sits in that resource. */
export type OpenResource = (id: string, path: string | null) => void

/** A `widget form` or `task form` reference whose holder is a resource. */
export type FormUse = StudioReference & { node: Extract<StudioReferenceNode, { type: 'resource' }> }

/**
 * The pages and process steps that use a form. An entry is its resource as a link, with its kind muted, and then its
 * relation in words, as the Used by tab says it. The link opens the resource at the reference's path.
 */
export function StudioFormUsedOn({
  entries,
  onOpen,
  resources,
}: {
  entries: FormUse[] | 'failed' | undefined
  onOpen: OpenResource
  resources: readonly StudioResourceItem[]
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  if (entries === undefined) {
    return null
  }
  return (
    <section data-testid="studio-form-used-on">
      <Typography.Title level={5}>{t('shell.studio.design.usedOn')}</Typography.Title>
      {entries === 'failed' ? (
        <AxisAlert kind="error" message={t('shell.studio.referencesFailed')} />
      ) : entries.length === 0 ? (
        <EmptyState text={t('shell.studio.design.usedOnNone')} />
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {entries.map((entry, index) => (
            <li key={index} data-testid="studio-form-used-on-entry">
              <Flex align="center" gap={spacing.sm} wrap>
                <ResourceReference
                  resource={resources.find((item) => item.id === entry.node.id) ?? entry.node}
                  file={entry.file}
                  onOpen={() => onOpen(entry.node.id, entry.path)}
                />
                <Typography.Text>{t(relationTextKey(entry.relation))}</Typography.Text>
              </Flex>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
