import { Alert, Flex } from 'antd'
import { useEffect, useState } from 'react'
import { fetchStudioModuleResource, ServerUnreachableError } from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { EntityDesigner } from './EntityDesigner'

/**
 * An entity of a module the application uses, read from the module version the application pins and shown in the
 * entity designer with every input off. It has no save, no delete and no JSON view, because the module owns the file.
 */
export function StudioModuleEntity({ application, module, id }: { application: string; module: string; id: string }) {
  const t = useText()
  const { generation, lost } = useStudioConnection()
  const [content, setContent] = useState<Record<string, unknown>>()
  const [loadFailed, setLoadFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioModuleResource(application, module, id, controller.signal).then(
      (resource) => {
        setLoadFailed(false)
        setContent(resource.content)
      },
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the module resource '${id}' of '${module}' failed`, error)
        setLoadFailed(true)
      },
    )
    return () => controller.abort()
  }, [application, module, id, generation, lost])

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (!content) {
    return null
  }

  return (
    <Flex vertical gap="middle">
      <Alert data-testid="studio-module-read-only" type="info" title={t('shell.studio.moduleReadOnly')} />
      <EntityDesigner value={content} onChange={() => {}} readOnly />
    </Flex>
  )
}
