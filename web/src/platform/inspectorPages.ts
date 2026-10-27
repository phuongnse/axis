import { useEffect, useState } from 'react'
import { fetchPage } from './site'
import { leafWidgets } from './widgetTree'

/** The address of an instance on its inspector page, as the router has it. */
export function inspectorPath(sitePath: string, page: string, id: string): string {
  return `/${sitePath}/${page.toLowerCase()}/${id}`
}

/**
 * The first navigation page whose process inspector names each process, once loaded. The pages'
 * metadata loads only when `wanted` is true, and once. A page that fails to load is left out.
 */
export function useInspectorPages(
  sitePath: string,
  pages: readonly string[],
  wanted: boolean,
): ReadonlyMap<string, string> | undefined {
  const [inspectors, setInspectors] = useState<ReadonlyMap<string, string>>()
  const loaded = inspectors !== undefined

  useEffect(() => {
    if (!wanted || loaded) {
      return
    }
    const controller = new AbortController()
    void Promise.allSettled(pages.map((name) => fetchPage(sitePath, name, controller.signal))).then((results) => {
      if (controller.signal.aborted) {
        return
      }
      const found = new Map<string, string>()
      results.forEach((result, index) => {
        if (result.status === 'rejected') {
          console.warn(`Loading the page '${pages[index]}' for the inspector links failed`, result.reason)
          return
        }
        const widget = leafWidgets(result.value?.widgets ?? []).find((leaf) => leaf.type === 'processInspector')
        if (widget?.process && !found.has(widget.process)) {
          found.set(widget.process, pages[index])
        }
      })
      setInspectors(found)
    })
    return () => controller.abort()
  }, [wanted, loaded, sitePath, pages])

  return inspectors
}
