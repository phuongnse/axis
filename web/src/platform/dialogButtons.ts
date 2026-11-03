import type { ModalProps } from 'antd'
import { dialogSizeTokens } from './designTokens'

/**
 * Proposed: what every Ant Design dialog shares. Cancel beside the solid OK is a text button. A dialog is as tall as
 * its content, at most the window less its margins: its body scrolls while its title and buttons stay.
 */
export const dialogButtons = {
  cancelButtonProps: { type: 'text' as const },
  styles: { body: { maxHeight: 'calc(100dvh - 248px)', overflowY: 'auto' as const } },
}

export type DialogSize = keyof typeof dialogSizeTokens

/**
 * Proposed: the width a dialog names by its size. From the one breakpoint up it is the size's token; below it every
 * dialog takes the window's width less the page margin, whatever its size. Ant Design's `lg` is `breakpointTokens.narrowWidth`, 992 px.
 */
export function dialogWidth(size: DialogSize): ModalProps['width'] {
  return { xs: 'calc(100vw - 32px)', lg: dialogSizeTokens[size] }
}
