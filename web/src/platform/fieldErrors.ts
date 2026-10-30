import type { ConfigProviderProps } from 'antd'

/**
 * Proposed: the error of every form field, in every Ant Design form, sits 4px under its input in 14/20 text and keeps
 * the field's own gap below it: `spacing-lg` on a form, none in a table cell or an inline form. Ant Design draws the
 * error inside that gap, so the next field's label sat right under it.
 */
export const fieldErrors: ConfigProviderProps['form'] = {
  styles: ({ props }) => ({
    help: {
      marginTop: 4,
      marginBottom: props.layout === 'inline' ? 0 : 'var(--ant-form-item-margin-bottom)',
      fontSize: 14,
      lineHeight: '20px',
    },
  }),
}
