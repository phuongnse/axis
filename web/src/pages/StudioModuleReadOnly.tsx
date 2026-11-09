import { Alert } from 'antd'
import { useText } from '../platform/texts'

/** The notice on a module file that Studio shows read-only, such as a policy, page or a kind with no editor yet: the module owns the file. It has the alert role, so assistive technology announces it. */
export function StudioModuleReadOnly() {
  const t = useText()
  return (
    // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
    <Alert role="alert" data-testid="studio-module-read-only" type="info" title={t('shell.studio.moduleReadOnly')} />
  )
}
