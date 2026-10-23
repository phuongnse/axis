import { Alert, Button, Flex, Tag, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'
import {
  fetchResourceReferences,
  fetchTextReferences,
  ServerUnreachableError,
  type StudioReference,
  type StudioReferenceNode,
  type StudioResourceReferences,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'

type Direction = 'uses' | 'usedBy'

/** Opens a resource. `path` is set for a Used by entry, where the reference sits in that resource. */
export type OpenResource = (id: string, path: string | null) => void

/**
 * The Uses and Used by lists of a selected resource. A load failure shows an alert in their place. An entry
 * expands one more step, and its own lists load once.
 */
export function StudioReferences({
  application,
  resourceId,
  onOpen,
}: {
  application: string
  resourceId: string
  onOpen: OpenResource
}) {
  const t = useText()
  const { generation, lost } = useStudioConnection()
  const [references, setReferences] = useState<StudioResourceReferences>()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchResourceReferences(application, resourceId, controller.signal).then(setReferences, (error: unknown) => {
      if (controller.signal.aborted) {
        return
      }
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn(`Loading the references of '${resourceId}' failed`, error)
      setFailed(true)
    })
    return () => controller.abort()
  }, [application, resourceId, generation, lost])

  if (failed) {
    return <Alert type="error" title={t('shell.studio.referencesFailed')} />
  }
  if (!references) {
    return null
  }
  return (
    <Flex vertical gap="middle">
      <section data-testid="studio-uses">
        <Typography.Title level={5}>{t('shell.studio.uses')}</Typography.Title>
        <ReferenceList entries={references.uses} direction="uses" application={application} onOpen={onOpen} />
      </section>
      <section data-testid="studio-used-by">
        <Typography.Title level={5}>{t('shell.studio.usedBy')}</Typography.Title>
        <ReferenceList entries={references.usedBy} direction="usedBy" application={application} onOpen={onOpen} />
      </section>
    </Flex>
  )
}

function ReferenceList({
  entries,
  direction,
  application,
  onOpen,
}: {
  entries: StudioReference[]
  direction: Direction
  application: string
  onOpen: OpenResource
}) {
  const t = useText()
  if (entries.length === 0) {
    return <Typography.Text type="secondary">{t('shell.studio.noReferences')}</Typography.Text>
  }
  return (
    <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
      {entries.map((entry, index) => (
        <ReferenceEntry key={index} entry={entry} direction={direction} application={application} onOpen={onOpen} />
      ))}
    </ul>
  )
}

function ReferenceEntry({
  entry,
  direction,
  application,
  onOpen,
}: {
  entry: StudioReference
  direction: Direction
  application: string
  onOpen: OpenResource
}) {
  const t = useText()
  const { lost } = useStudioConnection()
  const { node } = entry
  const expandable = node.type === 'resource' || node.type === 'text'
  const [expanded, setExpanded] = useState(false)
  const [details, setDetails] = useState<StudioResourceReferences>()
  const [failed, setFailed] = useState(false)
  const requested = useRef(false)

  function toggle() {
    const next = !expanded
    setExpanded(next)
    if (next && !requested.current) {
      requested.current = true
      loadDetails(application, node)?.then(setDetails, (error: unknown) => {
        requested.current = false
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn('Loading the references of an entry failed', error)
        setFailed(true)
      })
    }
  }

  return (
    <li data-testid="studio-reference">
      <Flex vertical gap={4}>
        <Flex align="center" gap="small" wrap>
          <Tag>{entry.relation}</Tag>
          {node.type === 'resource' ? (
            <Button
              type="link"
              style={{ padding: 0, height: 'auto' }}
              onClick={() => onOpen(node.id, direction === 'usedBy' ? entry.path : null)}
            >
              {nodeTitle(node)}
            </Button>
          ) : (
            <span>{nodeTitle(node)}</span>
          )}
          {expandable && (
            <Button type="text" size="small" aria-expanded={expanded} onClick={toggle}>
              {expanded ? t('shell.studio.collapse') : t('shell.studio.expand')}
            </Button>
          )}
        </Flex>
        <Typography.Text type="secondary">{`${entry.file} · ${t('shell.studio.path')}: ${entry.path}`}</Typography.Text>
        {expanded && (
          <Flex vertical gap="small" style={{ paddingLeft: 16 }}>
            {failed && <Alert type="error" title={t('shell.studio.referencesFailed')} />}
            {details && (
              <>
                {node.type === 'resource' && (
                  <div>
                    <Typography.Text strong>{t('shell.studio.uses')}</Typography.Text>
                    <ReferenceList entries={details.uses} direction="uses" application={application} onOpen={onOpen} />
                  </div>
                )}
                <div>
                  <Typography.Text strong>{t('shell.studio.usedBy')}</Typography.Text>
                  <ReferenceList
                    entries={details.usedBy}
                    direction="usedBy"
                    application={application}
                    onOpen={onOpen}
                  />
                </div>
              </>
            )}
          </Flex>
        )}
      </Flex>
    </li>
  )
}

/** Loads one step of a reference. A text key has no Uses list, so its answer has none. Packages and modules have no references. */
function loadDetails(application: string, node: StudioReferenceNode): Promise<StudioResourceReferences> | undefined {
  if (node.type === 'resource') {
    return fetchResourceReferences(application, node.id)
  }
  if (node.type === 'text') {
    return fetchTextReferences(application, node.key).then(({ usedBy }) => ({ uses: [], usedBy }))
  }
  return undefined
}

function nodeTitle(node: StudioReferenceNode): string {
  if (node.type === 'resource') {
    return node.name ?? node.file
  }
  if (node.type === 'text') {
    return node.key
  }
  return node.id
}
