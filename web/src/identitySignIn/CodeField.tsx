import { Form, Input, type FormInstance } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from '../platform/themeMode'

/**
 * The 6-digit code in six boxes, one digit each, and the hidden input that posts it as `otp` or `totp`. The boxes keep
 * only digits. The page's form must hold a `code` field, and `invalid` marks the boxes with the error status.
 */
export function CodeField({
  form,
  postAs,
  label,
  requiredMessage,
  invalid,
}: {
  form: FormInstance<{ code?: string }>
  postAs: 'otp' | 'totp'
  label: ReactNode
  requiredMessage: string
  invalid: boolean
}) {
  const { colour } = useDesignTokens()
  const code = Form.useWatch('code', form) ?? ''
  return (
    <>
      <Form.Item
        name="code"
        label={<span style={{ fontWeight: 500, color: colour.textMuted }}>{label}</span>}
        rules={[{ required: true, len: 6, message: requiredMessage }]}
        validateStatus={invalid ? 'error' : undefined}
      >
        <Input.OTP
          length={6}
          formatter={(value) => value.replace(/\D/g, '')}
          // The field only changes once all six boxes are full, so a deleted digit clears it here.
          onInput={(cells) => form.setFieldValue('code', cells.join(''))}
        />
      </Form.Item>
      <input type="hidden" name={postAs} value={code} />
    </>
  )
}
