import { AxisAlert } from '../platform/AxisAlert'
import { useText } from '../platform/texts'

export function LocaleLoadFailed({ onClose }: { onClose: () => void }) {
  const t = useText()
  return (
    <AxisAlert
      data-testid="locale-error"
      kind="error"
      banner
      closable
      message={t('shell.locale.loadFailed')}
      onClose={onClose}
    />
  )
}

/** Shown when the site cannot be loaded. There may be no texts to render, so the string is fixed. */
export function ShellError() {
  return <AxisAlert data-testid="shell-error" kind="error" banner message="Could not load the site." />
}
