import { Checkbox, Radio, Switch, Upload } from 'antd'

// A checkbox, a switch, an upload and a radio group keep their own width, see docs/reference/shell.md#layout. The
// form widgets that draw their own checkbox or radio group add theirs with `keepsOwnWidth`, as `AxisField` cannot
// import them without a cycle.
const ownWidth = new WeakSet<object>([Checkbox, Radio.Group, Switch, Upload])

/**
 * Marks an input component that keeps its own width, such as the checkbox a form item draws. `AxisField` draws no
 * wrapper round it, see docs/reference/shell.md#layout.
 */
export function keepsOwnWidth<T extends object>(input: T): T {
  ownWidth.add(input)
  return input
}

/** Whether an input's component keeps its own width. A string, such as a plain `input` tag, never does. */
export function hasOwnWidth(type: unknown): boolean {
  return ownWidth.has(type as object)
}
