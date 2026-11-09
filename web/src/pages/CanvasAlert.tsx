import { Alert } from 'antd'
import { useText } from '../platform/texts'

/** The notice a canvas shows in place of what it cannot draw. */
export function CanvasAlert({ testId, textKey }: { testId: string; textKey: string }) {
  const t = useText()
  // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
  return <Alert data-testid={testId} type="warning" title={t(textKey)} />
}
