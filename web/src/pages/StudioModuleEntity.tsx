import { Alert, Flex } from 'antd'
import { useText } from '../platform/texts'
import { EntityDesigner } from './EntityDesigner'
import { StudioModuleReadOnly } from './StudioModuleReadOnly'
import { useStudioModuleContent } from './useStudioModuleContent'

/**
 * An entity of a module the application uses, read from the module version the application pins and shown in the
 * entity designer with every input off. It has no save, no delete and no JSON view, because the module owns the file.
 */
export function StudioModuleEntity({ application, module, id }: { application: string; module: string; id: string }) {
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
      <EntityDesigner value={content} onChange={() => {}} readOnly />
    </Flex>
  )
}
