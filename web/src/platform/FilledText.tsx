import { Fragment, type ReactNode } from 'react'

/**
 * Proposed: a text whose `{name}` placeholders are filled with parts, such as a name in bold or a document as a link, so
 * a sentence keeps its word order in every language. A placeholder with no part stays as it is written.
 */
export function FilledText({ text, parts }: { text: string; parts: Record<string, ReactNode> }) {
  return (
    <>
      {text.split(/(\{\w+\})/).map((piece, index) => {
        const name = /^\{(\w+)\}$/.exec(piece)?.[1]
        return <Fragment key={index}>{name !== undefined && name in parts ? parts[name] : piece}</Fragment>
      })}
    </>
  )
}
