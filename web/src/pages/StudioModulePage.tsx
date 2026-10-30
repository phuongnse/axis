import { Alert, Flex } from 'antd'
import { lazy, Suspense } from 'react'
import { useText } from '../platform/texts'
import { StudioModuleReadOnly } from './StudioModuleReadOnly'
import { useStudioModuleContent } from './useStudioModuleContent'

// The canvas pulls in the drag and drop library, which the main bundle keeps out.
const StudioPageCanvas = lazy(() =>
  import('./StudioPageCanvas').then((module) => ({ default: module.StudioPageCanvas })),
)

function ignore() {}

/**
 * A page of a module the application uses, read from the module version the application pins and drawn on the page
 * canvas alone. It has no palette, no data panel, no widget tree, no save, no delete and no moves, because the module owns
 * the file.
 */
export function StudioModulePage({ application, module, id }: { application: string; module: string; id: string }) {
  const t = useText()
  const { content, loadFailed } = useStudioModuleContent(application, module, id)

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (!content) {
    return null
  }

  return (
    <Flex vertical gap="middle">
      <StudioModuleReadOnly />
      <Suspense fallback={null}>
        <StudioPageCanvas
          application={application}
          module={module}
          resourceId={id}
          draft={content}
          selected=""
          onSelect={ignore}
        />
      </Suspense>
    </Flex>
  )
}
