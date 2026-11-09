import { Checkbox, Form, Typography } from 'antd'
import type { FormItemProps } from 'antd'
import { Fragment } from 'react'
import { useDesignTokens } from './themeMode'

/** A document that a sentence names: its label and its address. Without an address the label shows as plain text. */
export interface DocumentLink {
  label: string
  href?: string
}

/**
 * Proposed: a sentence whose `{name}` placeholders name documents as links, which open in a new tab so the person keeps
 * the page. A placeholder whose document has no address shows the document's label as plain text.
 */
export function TextWithLinks({ text, links }: { text: string; links: Record<string, DocumentLink> }) {
  const { colour } = useDesignTokens()
  return (
    <>
      {text.split(/(\{\w+\})/).map((part, index) => {
        const link = /^\{(\w+)\}$/.exec(part)?.[1]
        const document = link === undefined ? undefined : links[link]
        if (document === undefined) {
          return <Fragment key={index}>{part}</Fragment>
        }
        return document.href ? (
          <Typography.Link
            key={index}
            href={document.href}
            target="_blank"
            rel="noopener noreferrer"
            style={{ color: colour.primaryText }}
          >
            {document.label}
          </Typography.Link>
        ) : (
          <Fragment key={index}>{document.label}</Fragment>
        )
      })}
    </>
  )
}

/**
 * Proposed: the one required box that accepts terms, inside an Ant Design form. Its label is a sentence that names each
 * document as a link, see `TextWithLinks`. The box is never ticked at first, and finishing the form with it unticked
 * marks it with `requiredMessage`. The identity provider's terms step and the sign-up page both draw their box with it.
 */
export function AcceptTermsField({
  name,
  label,
  links,
  requiredMessage,
}: {
  name: NonNullable<FormItemProps['name']>
  /** The sentence, such as "I accept the {terms} and the {privacy}.". */
  label: string
  links: Record<string, DocumentLink>
  /** The message under the box when it is unticked, such as "Accept the terms to continue.". */
  requiredMessage: string
}) {
  return (
    <Form.Item
      name={name}
      valuePropName="checked"
      rules={[
        {
          validator: (_rule, value) =>
            value === true ? Promise.resolve() : Promise.reject(new Error(requiredMessage)),
        },
      ]}
    >
      <Checkbox aria-required>
        <TextWithLinks text={label} links={links} />
      </Checkbox>
    </Form.Item>
  )
}
