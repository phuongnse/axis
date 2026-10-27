import type { ConfigProviderProps } from 'antd'
import { FieldLabel } from './FieldGrid'

/** Every form labels its fields the same way: medium and muted, and a `*` after the label of a required field. */
export const formConfig: ConfigProviderProps['form'] = {
  requiredMark: (label, { required }) => <FieldLabel label={label} required={required} />,
}
