import { Fragment, type ReactNode } from 'react'
import { placeholderPattern } from './fillText'

/** A text with a node at each `{name}` placeholder, see docs/reference/shared-widgets.md#which-widget. A placeholder with no node stays as written. */
export function FilledText({ text, parts }: { text: string; parts: Record<string, ReactNode> }) {
  return (
    <>
      {text.split(placeholderPattern).map((piece, index) => {
        if (index % 2 === 0) {
          return piece
        }
        const name = piece.slice(1, -1)
        return Object.hasOwn(parts, name) ? <Fragment key={index}>{parts[name]}</Fragment> : piece
      })}
    </>
  )
}
