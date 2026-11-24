import { Fragment, type ReactNode } from 'react'
import { placeholderPattern } from './fillText'
import { runningTextClass } from './antTheme'

/**
 * A text with a node at each `{name}` placeholder, see docs/reference/shared-widgets.md#which-widget. A placeholder with no
 * node stays as written. The span marks the sentence as running text, so its links are underlined, see
 * docs/reference/actions-and-controls.md#opening-things.
 */
export function FilledText({ text, parts }: { text: string; parts: Record<string, ReactNode> }) {
  return (
    <span className={runningTextClass}>
      {text.split(placeholderPattern).map((piece, index) => {
        if (index % 2 === 0) {
          return piece
        }
        const name = piece.slice(1, -1)
        return Object.hasOwn(parts, name) ? <Fragment key={index}>{parts[name]}</Fragment> : piece
      })}
    </span>
  )
}
