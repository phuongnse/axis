import { AxisAlert } from '../platform/AxisAlert'
import { useText } from '../platform/texts'

/** The notice a canvas shows in place of what it cannot draw. */
export function CanvasAlert({ testId, textKey }: { testId: string; textKey: string }) {
  const t = useText()
  return <AxisAlert data-testid={testId} kind="warning" message={t(textKey)} />
}
