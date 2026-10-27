import { Button, Tooltip, type ButtonProps } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from './themeMode'

interface IconActionProps extends Omit<ButtonProps, 'icon' | 'children' | 'danger' | 'aria-label'> {
  /** The action's name: its tooltip and its accessible name. */
  label: string
  icon: ReactNode
  /** A destructive action, drawn in dangerText. */
  danger?: boolean
}

/**
 * Proposed: a button that shows only an icon, for an action repeated on every row. Its name shows as a tooltip on
 * hover and on keyboard focus, and is its accessible name, so nobody has to guess what the icon does.
 */
export function IconAction({ label, icon, danger = false, style, ...rest }: IconActionProps) {
  const { colour } = useDesignTokens()
  return (
    <Tooltip title={label} trigger={['hover', 'focus']}>
      <Button
        type="text"
        danger={danger}
        aria-label={label}
        icon={icon}
        // Ant Design draws a danger text button in the danger fill, too dark on a dark surface.
        style={{ ...(danger && { color: colour.dangerText }), ...style }}
        {...rest}
      />
    </Tooltip>
  )
}
