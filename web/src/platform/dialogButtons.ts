import type { ModalProps } from 'antd'
import { dialogSizeTokens, spacingTokens } from './designTokens'

/** Ant Design's padding of a dialog's frame at the sides, the theme's `paddingContentHorizontalLG`. */
const framePadding = spacingTokens.lg

/**
 * Proposed: what every Ant Design dialog shares. Cancel beside the solid OK is a text button. A dialog is as tall as
 * its content, at most the window less its margins: its body scrolls while its title and buttons stay. The body
 * reaches the frame's edges and keeps the frame's padding inside, so its scrollbar runs at the dialog's right edge,
 * from under the title to above the buttons, as Material, Fluent and Carbon place it, and a field's 2 px focus ring
 * is never cut at its sides. The scrollbar is the thin one, in the mode the page draws in.
 */
export const dialogButtons = {
  cancelButtonProps: { type: 'text' as const },
  styles: {
    body: {
      maxHeight: 'calc(100dvh - 248px)',
      overflowY: 'auto' as const,
      marginInline: -framePadding,
      paddingInline: framePadding,
      marginBlock: -4,
      paddingBlock: 4,
      scrollbarWidth: 'thin' as const,
    },
  },
}

export type DialogSize = keyof typeof dialogSizeTokens

/**
 * Proposed: the width a dialog names by its size. From the one breakpoint up it is the size's token; below it every
 * dialog takes the window's width less the page margin, whatever its size. Ant Design's `lg` is `breakpointTokens.narrowWidth`, 992 px.
 */
export function dialogWidth(size: DialogSize): ModalProps['width'] {
  return { xs: 'calc(100vw - 32px)', lg: dialogSizeTokens[size] }
}
