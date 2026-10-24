import { Alert } from 'antd'
import { useText } from '../platform/texts'

/** The notice a canvas shows in place of what it cannot draw. */
export function CanvasAlert({ testId, textKey }: { testId: string; textKey: string }) {
  const t = useText()
  return <Alert data-testid={testId} type="warning" title={t(textKey)} />
}
