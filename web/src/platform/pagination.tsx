import type { PaginationProps } from 'antd'
import { cloneElement, isValidElement, type ReactElement, type ReactNode } from 'react'
import { Chevron } from './Chevron'

/**
 * The items of a pagination. The current page's item is marked with `aria-current="page"`, which the UI library leaves
 * out. The previous and next arrows draw the one chevron, see docs/reference/shared-widgets.md#which-widget. Ant Design
 * draws the jump arrows itself, so they keep its look.
 */
export function paginationItems(current: number): PaginationProps['itemRender'] {
  return (page, type, element) => {
    if (!isValidElement(element)) return element
    if (type === 'prev' || type === 'next') {
      return cloneElement(
        element as ReactElement<{ children?: ReactNode }>,
        {},
        <Chevron point={type === 'prev' ? 'left' : 'right'} />,
      )
    }
    return type === 'page' && page === current
      ? cloneElement(element as ReactElement<{ 'aria-current'?: string }>, { 'aria-current': 'page' })
      : element
  }
}
