import { Flex, Radio, Tag, Typography, type FormItemProps } from 'antd'
import { AxisField } from './AxisField'

/** An application or a template the field offers: its id, its title and description, and an optional mark such as "Built in". */
export interface StartFromChoice {
  id: string
  title: string
  description: string
  mark?: string
}

/**
 * The choice of what a new application or tenant starts from, see docs/reference/studio.md#new-application and
 * docs/reference/sign-up.md#start-from. Blank comes first and is chosen at the start, and sends an empty string. Each
 * choice follows with its title, its optional mark and its description. While `choices` is undefined the list is loading,
 * and Blank is the only choice. When `choices` is `'failed'` the list did not load, and a warning under the choices says
 * Blank is still there. The caller gives every text in the page's language.
 */
export function StartFromField({
  name,
  label,
  help,
  blank,
  choices,
  failedMessage,
}: {
  name: NonNullable<FormItemProps['name']>
  label: string
  help: string
  /** Blank's name. */
  blank: string
  /** Undefined while the list loads, `'failed'` when it did not load. */
  choices: readonly StartFromChoice[] | 'failed' | undefined
  /** The warning under the choices that Blank is still there. */
  failedMessage: string
}) {
  return (
    <AxisField
      name={name}
      label={label}
      help={help}
      initialValue=""
      status={choices === 'failed' ? { state: 'failed', message: failedMessage } : { state: 'idle' }}
    >
      <Radio.Group aria-label={label}>
        <Flex vertical>
          <Radio value="">{blank}</Radio>
          {typeof choices === 'object' &&
            choices.map((item) => (
              <Radio key={item.id} value={item.id}>
                <span>{item.title}</span>
                {item.mark && <Tag style={{ marginInlineStart: 8 }}>{item.mark}</Tag>}
                <Typography.Text type="secondary" style={{ display: 'block' }}>
                  {item.description}
                </Typography.Text>
              </Radio>
            ))}
        </Flex>
      </Radio.Group>
    </AxisField>
  )
}
