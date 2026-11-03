import { Alert } from 'antd'
import { useText } from '../platform/texts'

/** The notice on a module entity, process, policy or contract: the module owns the file, so Studio shows it read-only. It has the alert role, so assistive technology announces it. */
export function StudioModuleReadOnly() {
  const t = useText()
  return (
    <Alert role="alert" data-testid="studio-module-read-only" type="info" title={t('shell.studio.moduleReadOnly')} />
  )
}
