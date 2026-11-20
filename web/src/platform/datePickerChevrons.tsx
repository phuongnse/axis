import { Chevron } from './Chevron'

/**
 * The previous and next month arrows of a date picker, drawn as the chevron. The double arrows that jump a year keep
 * Ant Design's look. ConfigProvider cannot set these arrows, so each DatePicker spreads this object, see
 * docs/reference/shared-widgets.md#which-widget.
 */
export const datePickerChevrons = {
  prevIcon: <Chevron point="left" />,
  nextIcon: <Chevron point="right" />,
}
