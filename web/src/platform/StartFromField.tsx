import { Flex, Radio, Tag, Typography } from 'antd'
import { AxisField } from './AxisField'

/** An application a new one can start from, with its texts already in the page's language. */
export interface StartingApplication {
  id: string
  title: string
  description?: string
  /** Marks an application that ships with Axis, among others that do not. */
  builtIn?: boolean
}

/**
 * Proposed: the field that chooses what a new application starts from, inside an Ant Design form: Blank, the value `''`,
 * then each offered application with its description. Blank is always offered. While the list loads only Blank shows,
 * and when it fails the field's `failed` status says under the choices that Blank is still there. Studio's New
 * application and the sign-up page draw it. See docs/reference/studio.md#new-application.
 */
export function StartFromField({
  name,
  texts,
  applications,
}: {
  name: string
  texts: { label: string; help: string; blank: string; builtIn?: string; failed: string }
  /** The offered applications, `undefined` while they load, or `'failed'`. */
  applications: StartingApplication[] | 'failed' | undefined
}) {
  return (
    <AxisField
      name={name}
      label={texts.label}
      help={texts.help}
      status={applications === 'failed' ? { state: 'failed', message: texts.failed } : undefined}
    >
      <Radio.Group aria-label={texts.label}>
        <Flex vertical>
          <Radio value="">{texts.blank}</Radio>
          {Array.isArray(applications) &&
            applications.map((item) => (
              <Radio key={item.id} value={item.id}>
                <span>{item.title}</span>
                {item.builtIn && texts.builtIn !== undefined && (
                  <Tag style={{ marginInlineStart: 8 }}>{texts.builtIn}</Tag>
                )}
                {item.description !== undefined && (
                  <Typography.Text type="secondary" style={{ display: 'block' }}>
                    {item.description}
                  </Typography.Text>
                )}
              </Radio>
            ))}
        </Flex>
      </Radio.Group>
    </AxisField>
  )
}
