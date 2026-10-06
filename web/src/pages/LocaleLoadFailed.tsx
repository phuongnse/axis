import { Alert } from 'antd'
import { useText } from '../platform/texts'

export function LocaleLoadFailed({ onClose }: { onClose: () => void }) {
  const t = useText()
  return (
    <Alert
      data-testid="locale-error"
      type="error"
      banner
      closable
      message={t('shell.locale.loadFailed')}
      onClose={onClose}
    />
  )
}

/** Shown when the site cannot be loaded. There may be no texts to render, so the string is fixed. */
export function ShellError() {
  return <Alert data-testid="shell-error" type="error" banner message="Could not load the site." />
}
