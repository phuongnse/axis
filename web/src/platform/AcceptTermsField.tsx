import { Checkbox, Form, Typography, type FormItemProps } from 'antd'
import type { ReactNode } from 'react'
import { FilledText } from './FilledText'
import { setOwn } from './ownProperty'
import { useDesignTokens } from './themeMode'

/** A document the box names: its name in the sentence and its address. Without an address the name shows as plain text. */
export interface AcceptTermsDocument {
  label: string
  href?: string
}

/**
 * A required box that accepts terms, see docs/reference/legal.md#accepting-the-terms. It never starts ticked, names each
 * document as a link that opens in a new tab and marks itself with its message when the form is finished unticked.
 */
export function AcceptTermsField({
  name,
  text,
  documents,
  requiredMessage,
}: {
  name: NonNullable<FormItemProps['name']>
  /** The sentence, with a `{key}` placeholder for each document. */
  text: string
  documents: Record<string, AcceptTermsDocument>
  /** The message under the box when the form is finished unticked. */
  requiredMessage: string
}) {
  const { colour } = useDesignTokens()
  const parts: Record<string, ReactNode> = {}
  for (const [key, { label, href }] of Object.entries(documents)) {
    setOwn(
      parts,
      key,
      href ? (
        <Typography.Link href={href} target="_blank" rel="noopener noreferrer" style={{ color: colour.primaryText }}>
          {label}
        </Typography.Link>
      ) : (
        label
      ),
    )
  }
  return (
    <Form.Item
      name={name}
      valuePropName="checked"
      rules={[
        {
          validator: (_, value) => (value === true ? Promise.resolve() : Promise.reject(new Error(requiredMessage))),
        },
      ]}
    >
      <Checkbox>
        <FilledText text={text} parts={parts} />
      </Checkbox>
    </Form.Item>
  )
}
