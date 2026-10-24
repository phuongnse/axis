import { Alert, Button, Flex, Tag, Typography } from 'antd'
import type { StudioReference, StudioReferenceNode } from '../platform/studio'
import { useText } from '../platform/texts'
import type { OpenResource } from './StudioReferences'

/** A `widget form` or `task form` reference whose holder is a resource. */
export type FormUse = StudioReference & { node: Extract<StudioReferenceNode, { type: 'resource' }> }

/** The pages and process steps that use a form. An entry opens its resource at the reference's path. */
export function StudioFormUsedOn({
  entries,
  onOpen,
}: {
  entries: FormUse[] | 'failed' | undefined
  onOpen: OpenResource
}) {
  const t = useText()
  if (entries === undefined) {
    return null
  }
  return (
    <section data-testid="studio-form-used-on">
      <Typography.Title level={5}>{t('shell.studio.design.usedOn')}</Typography.Title>
      {entries === 'failed' ? (
        <Alert type="error" title={t('shell.studio.referencesFailed')} />
      ) : entries.length === 0 ? (
        <Typography.Text type="secondary">{t('shell.studio.design.usedOnNone')}</Typography.Text>
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {entries.map((entry, index) => (
            <li key={index} data-testid="studio-form-used-on-entry">
              <Flex vertical gap={4}>
                <Flex align="center" gap="small" wrap>
                  <Tag>{entry.relation}</Tag>
                  <Button
                    type="link"
                    style={{ padding: 0, height: 'auto' }}
                    onClick={() => onOpen(entry.node.id, entry.path)}
                  >
                    {entry.node.name ?? entry.node.file}
                  </Button>
                </Flex>
                <Typography.Text type="secondary">{`${entry.file} · ${t('shell.studio.path')}: ${entry.path}`}</Typography.Text>
              </Flex>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
