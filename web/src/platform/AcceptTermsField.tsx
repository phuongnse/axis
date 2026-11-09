import { Checkbox, Form, Typography } from 'antd'
import type { FormItemProps } from 'antd'
import type { ReactNode } from 'react'
import { FilledText } from './FilledText'
import { setOwn } from './ownProperty'
import { useDesignTokens } from './themeMode'

/** A document that a sentence names: its label and its address. Without an address the label shows as plain text. */
export interface DocumentLink {
  label: string
  href?: string
}

/**
 * Proposed: a sentence that names documents as links, which open in a new tab so the person keeps the page, through
 * `FilledText`. A document with no address shows its label as plain text.
 */
export function TextWithLinks({ text, links }: { text: string; links: Record<string, DocumentLink> }) {
  const { colour } = useDesignTokens()
  const parts: Record<string, ReactNode> = {}
  for (const [name, document] of Object.entries(links)) {
    setOwn(
      parts,
      name,
      document.href ? (
        <Typography.Link
          href={document.href}
          target="_blank"
          rel="noopener noreferrer"
          style={{ color: colour.primaryText }}
        >
          {document.label}
        </Typography.Link>
      ) : (
        document.label
      ),
    )
  }
  return <FilledText text={text} parts={parts} />
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
