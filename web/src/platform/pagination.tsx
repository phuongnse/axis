import type { PaginationProps } from 'antd'
import { cloneElement, isValidElement, type ReactElement } from 'react'

/** Marks the current page's item with `aria-current="page"`, which the UI library leaves out. */
export function markCurrentPage(current: number): PaginationProps['itemRender'] {
  return (page, type, element) =>
    type === 'page' && page === current && isValidElement(element)
      ? cloneElement(element as ReactElement<{ 'aria-current'?: string }>, { 'aria-current': 'page' })
      : element
}
