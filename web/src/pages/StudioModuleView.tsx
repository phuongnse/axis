import { Alert, Flex } from 'antd'
import type { ModuleResourceItem, StudioDiagnostic, StudioResource, StudioResourceItem } from '../platform/studio'
import { useText } from '../platform/texts'
import { StudioModuleContract } from './StudioModuleContract'
import { StudioModuleEditor } from './StudioModuleEditor'
import { StudioModuleReadOnly } from './StudioModuleReadOnly'
import { useStudioModuleContent } from './useStudioModuleContent'

/**
 * A module file of a kind with no editor, a scenario, read from the module version the application pins: the read-only notice
 * and the file's content as formatted JSON.
 */
function StudioModuleJson({ application, module, id }: { application: string; module: string; id: string }) {
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
      <pre data-testid="studio-module-content" style={{ margin: 0, overflow: 'auto' }}>
        {JSON.stringify(content, null, 2)}
      </pre>
    </Flex>
  )
}

/**
 * The view of a module resource, by its kind. Every kind but a scenario is edited as a layer, and so are the states and
 * transitions that a contract takes from the application. A scenario opens in the read-only JSON view. `diagnostics`
 * are the folder's.
 */
export function ModuleResourceView({
  application,
  item,
  manifest,
  diagnostics,
  onModuleWritten,
  onManifestSaved,
  onManifestStale,
}: {
  application: string
  item: ModuleResourceItem
  manifest?: StudioResource
  diagnostics: StudioDiagnostic[]
  onModuleWritten: (diagnostics: StudioDiagnostic[], layer: { added?: StudioResourceItem; removedId?: string }) => void
  onManifestSaved: (manifest: StudioResource, diagnostics: StudioDiagnostic[]) => void
  onManifestStale: () => void
}) {
  switch (item.kind) {
    case 'entity':
    case 'process':
    case 'policy':
    case 'text':
    case 'form':
    case 'page':
    case 'dataSource':
    case 'rule':
    case 'trigger':
    case 'sequence':
    case 'event':
    case 'seed':
      return <StudioModuleEditor application={application} item={item} onWritten={onModuleWritten} />
    case 'contract':
      return (
        <StudioModuleContract
          application={application}
          module={item.module}
          id={item.id}
          manifest={manifest}
          diagnostics={diagnostics}
          onWritten={onModuleWritten}
          onManifestSaved={onManifestSaved}
          onManifestStale={onManifestStale}
        />
      )
    default:
      return <StudioModuleJson application={application} module={item.module} id={item.id} />
  }
}
