import { useEffect, useState } from 'react'
import { fetchStudioModuleResource, ServerUnreachableError } from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'

/**
 * The content of one file of the module version that the application pins, loaded again after a reconnect. `content` is
 * `undefined` until it loads, and `loadFailed` is set when the load fails for a reason other than an unreachable server,
 * which shows the reconnect message instead.
 */
export function useStudioModuleContent(application: string, module: string, id: string) {
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

  return { content, loadFailed }
}
