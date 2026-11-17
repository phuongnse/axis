import { Fragment, type ReactNode } from 'react'

/**
 * Proposed, see the review on #3650: a text with `{name}` placeholders filled with drawn parts, such as a person's name
 * that links to their page inside "{person} chose {outcome}", so a sentence keeps the words and order of its locale.
 * A placeholder with no part stays as written.
 */
export function textNodes(text: string, parts: Record<string, ReactNode>): ReactNode {
  return text.split(/(\{\w+\})/).map((piece, index) => {
    const name = /^\{(\w+)\}$/.exec(piece)?.[1]
    return <Fragment key={index}>{name !== undefined && name in parts ? parts[name] : piece}</Fragment>
  })
}
