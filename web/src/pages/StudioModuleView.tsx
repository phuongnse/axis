import { Alert, Flex } from 'antd'
import type { ModuleResourceItem, StudioDiagnostic, StudioResource, StudioResourceItem } from '../platform/studio'
import { useText } from '../platform/texts'
import { StudioModuleContract } from './StudioModuleContract'
import { StudioModuleEditor } from './StudioModuleEditor'
import { StudioModulePage } from './StudioModulePage'
import { StudioModuleReadOnly } from './StudioModuleReadOnly'
import { useStudioModuleContent } from './useStudioModuleContent'

/**
 * A module file of a kind with no editor yet, read from the module version the application pins: the read-only notice
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
 * The view of a module resource, by its kind. An entity, a process, a policy and a text are edited as a layer, a contract and a page
 * have their own read-only view, and any other kind, scenarios included, opens in the read-only JSON view.
 */
export function ModuleResourceView({
  application,
  item,
  manifest,
  onModuleWritten,
  onManifestSaved,
  onManifestStale,
}: {
  application: string
  item: ModuleResourceItem
  manifest?: StudioResource
  onModuleWritten: (diagnostics: StudioDiagnostic[], layer: { added?: StudioResourceItem; removedId?: string }) => void
  onManifestSaved: (manifest: StudioResource, diagnostics: StudioDiagnostic[]) => void
  onManifestStale: () => void
}) {
  switch (item.kind) {
    case 'entity':
    case 'process':
    case 'policy':
    case 'text':
      return <StudioModuleEditor application={application} item={item} onWritten={onModuleWritten} />
    case 'contract':
      return (
        <StudioModuleContract
          application={application}
          module={item.module}
          id={item.id}
          manifest={manifest}
          onManifestSaved={onManifestSaved}
          onManifestStale={onManifestStale}
        />
      )
    case 'page':
      return <StudioModulePage application={application} module={item.module} id={item.id} />
    default:
      return <StudioModuleJson application={application} module={item.module} id={item.id} />
  }
}
