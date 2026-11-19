import {
  CheckOutlined,
  ClockCircleOutlined,
  CloseOutlined,
  ExclamationOutlined,
  MinusOutlined,
} from '@ant-design/icons'
import { Tooltip } from 'antd'
import type { ComponentType } from 'react'
import { runStateText, runStateTone, type RunState } from './inspection'
import { toneIconColours } from './statusTones'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** The icon of each run state, outlined. */
const icons: Record<RunState, ComponentType> = {
  done: CheckOutlined,
  running: ClockCircleOutlined,
  waitingForEvent: ClockCircleOutlined,
  waitingForPerson: ExclamationOutlined,
  failed: CloseOutlined,
  cancelled: MinusOutlined,
}

/**
 * A step's run state as an icon on a disc of `surface`, in its tone's icon colour, see
 * docs/reference/process-inspector-widget.md#process-graph. Its words are its tooltip and its accessible name. A step
 * not reached has no state and draws nothing. It has no margin of its own, so the card that holds it places it.
 * Data attributes pass to the badge, and `data-tone` names the tone.
 */
export function StateBadge({
  state,
  ...data
}: { state: RunState | undefined } & Record<`data-${string}`, string | undefined>) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  if (state === undefined) {
    return null
  }
  const tone = runStateTone(state)
  const words = runStateText(t, state)
  const Icon = icons[state]
  return (
    <Tooltip title={words}>
      <span
        role="img"
        aria-label={words}
        data-tone={tone}
        {...data}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: spacing.lg,
          height: spacing.lg,
          borderRadius: '50%',
          background: colour.surface,
          color: colour[toneIconColours[tone]],
          fontSize: spacing.md,
        }}
      >
        <Icon />
      </span>
    </Tooltip>
  )
}
