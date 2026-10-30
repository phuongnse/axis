import { Alert, Flex } from 'antd'
import { lazy, Suspense, useMemo } from 'react'
import { useText } from '../platform/texts'
import { buildProcessGraph } from './processGraph'
import { StudioModuleReadOnly } from './StudioModuleReadOnly'
import { useStudioModuleContent } from './useStudioModuleContent'

const StudioProcessGraph = lazy(() => import('./StudioProcessGraph'))

function ignore() {}

/**
 * A process of a module the application uses, read from the module version the application pins and shown as its graph
 * alone. The graph has no palette, no connect handle, no step panel and no delete, because the module owns the file.
 */
export function StudioModuleProcess({ application, module, id }: { application: string; module: string; id: string }) {
  const t = useText()
  const { content, loadFailed } = useStudioModuleContent(application, module, id)
  const graph = useMemo(() => (content ? buildProcessGraph(content, []) : undefined), [content])

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (!graph) {
    return null
  }

  return (
    <Flex vertical gap="middle">
      <StudioModuleReadOnly />
      <Suspense fallback={null}>
        <StudioProcessGraph
          graph={graph}
          selected={null}
          selectedEdge={null}
          onSelect={ignore}
          onSelectEdge={ignore}
          onRemove={ignore}
          onConnect={ignore}
          readOnly
        />
      </Suspense>
    </Flex>
  )
}
