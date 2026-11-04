import { Alert } from 'antd'
import { useText } from '../platform/texts'

/** The notice on a module file that Studio shows read-only, such as a policy, contract, page or a kind with no editor yet: the module owns the file. It has the alert role, so assistive technology announces it. */
export function StudioModuleReadOnly() {
  const t = useText()
  return (
    <Alert role="alert" data-testid="studio-module-read-only" type="info" title={t('shell.studio.moduleReadOnly')} />
  )
}
