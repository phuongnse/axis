import { Alert } from 'antd'
import { useText } from '../platform/texts'

export function LocaleLoadFailed({ onClose }: { onClose: () => void }) {
  const t = useText()
  return (
    // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
    <Alert
      data-testid="locale-error"
      type="error"
      banner
      closable
      title={t('shell.locale.loadFailed')}
      onClose={onClose}
    />
  )
}

/** Shown when the site cannot be loaded. There may be no texts to render, so the string is fixed. */
export function ShellError() {
  // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
  return <Alert data-testid="shell-error" type="error" banner title="Could not load the site." />
}
