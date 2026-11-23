import { Flex, Form, type FormProps } from 'antd'
import type { ReactNode } from 'react'
import { requiredMark } from './requiredMark'
import { useDesignTokens } from './themeMode'

export type AxisFormProps<Values> = Omit<
  FormProps<Values>,
  'children' | 'layout' | 'requiredMark' | 'scrollToFirstError'
> & {
  children?: ReactNode
  /** Marks no field as required, for the sign-in pages, see docs/reference/actions-and-controls.md#fields. */
  unmarked?: boolean
}

/**
 * A form on a platform screen: its labels above the inputs, the shared required mark, the focus on its first field in
 * error and its fields `spacing-lg` apart, see docs/reference/actions-and-controls.md#fields. A form that sets fields
 * in a row keeps `AxisFieldRow` or `ColumnGrid` inside it.
 */
export function AxisForm<Values = unknown>({ unmarked = false, children, ...props }: AxisFormProps<Values>) {
  const { spacing } = useDesignTokens()
  return (
    <Form
      {...props}
      layout="vertical"
      requiredMark={requiredMark}
      {...(unmarked ? { requiredMark: false } : {})}
      scrollToFirstError={{ focus: true }}
    >
      <Flex vertical gap={spacing.lg}>
        {children}
      </Flex>
    </Form>
  )
}
