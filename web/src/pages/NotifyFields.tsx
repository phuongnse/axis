import { Button, Checkbox, Flex, Radio, Typography } from 'antd'
import { useContext } from 'react'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useFieldTexts } from './fieldTexts'
import { LabelTexts } from './LabelTexts'
import { MappingRows } from './MappingRows'
import { Expression, NamePicker } from './ProcessFields'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

const channelOptions = ['inApp', 'email'] as const

const recipientKinds = ['user', 'role', 'email'] as const

type RecipientKind = (typeof recipientKinds)[number]

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function JsonOnly({ pointer, title, help }: { pointer: string; title: string; help: string }) {
  const t = useText()
  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisFieldGroup title={title} help={help}>
        <Typography.Text type="secondary" data-testid="studio-json-only">
          {t('shell.studio.editInJson')}
        </Typography.Text>
      </AxisFieldGroup>
    </div>
  )
}

/**
 * The editor of a notification: its recipients, channels, subject and body, placeholder values and an optional page
 * to open. `pointer` is the object that holds the `to`, `channels`, `message` and `open` keys, and the editor never
 * shows the `next` of a step that holds them, so a task's escalation can use it too. `path` is that object's path in the
 * process, from the root, which names the texts of its fields and groups, see docs/reference/studio-resource-form.md#fields.
 * Each value is edited through its own pointer, and an empty list stays in the draft for the compiler to report.
 */
export function NotifyFields({
  pointer,
  path,
  value,
  onChange,
}: {
  pointer: string
  path: string
  value: Content
  onChange: ChangeHandler
}) {
  return (
    <>
      <Recipients pointer={`${pointer}/to`} path={path} value={value.to} onChange={onChange} />
      <Channels pointer={`${pointer}/channels`} path={path} value={value.channels} onChange={onChange} />
      <Message pointer={`${pointer}/message`} path={path} value={value.message} onChange={onChange} />
      <OpenPage pointer={`${pointer}/open`} path={path} value={value.open} onChange={onChange} />
    </>
  )
}

/** The recipients as rows. A row is a user expression, a role or an email expression, and a switch keeps one key. */
function Recipients({
  pointer,
  path,
  value,
  onChange,
}: {
  pointer: string
  path: string
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const { field, group } = useFieldTexts('process')
  const recipientsGroup = group(`${path}.to`)
  if (value !== undefined && !Array.isArray(value)) {
    return <JsonOnly pointer={pointer} {...recipientsGroup} />
  }
  const recipients: unknown[] = value ?? []
  const labels: Record<RecipientKind, string> = {
    user: t('shell.studio.process.recipientUser'),
    role: t('shell.studio.process.recipientRole'),
    email: t('shell.studio.process.recipientEmail'),
  }

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisFieldGroup {...recipientsGroup}>
        <Flex vertical gap="small">
          {recipients.map((recipient, index) => {
            const row = isContent(recipient) ? recipient : {}
            const rowPointer = `${pointer}/${index}`
            const kind = recipientKinds.find((candidate) => row[candidate] !== undefined) ?? 'user'
            const entry = field(`${path}.to.${kind}`)
            return (
              <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${rowPointer}`}>
                <Radio.Group
                  name={`recipient-${rowPointer}`}
                  optionType="button"
                  aria-label={recipientsGroup.title}
                  value={kind}
                  options={recipientKinds.map((candidate) => ({ value: candidate, label: labels[candidate] }))}
                  onChange={(event) => onChange(rowPointer, { [event.target.value]: text(row[event.target.value]) })}
                />
                <div style={{ flex: 1, minWidth: 240 }}>
                  {kind === 'role' ? (
                    <NamePicker
                      pointer={`${rowPointer}/role`}
                      {...field(`${path}.to.role`)}
                      kind="role"
                      value={row.role}
                      onPick={(next) => onChange(`${rowPointer}/role`, next)}
                    />
                  ) : (
                    <div data-testid={`studio-field-${rowPointer}/${kind}`}>
                      <AxisField {...entry}>
                        <Expression
                          pointer={`${rowPointer}/${kind}`}
                          name={entry.label}
                          value={text(row[kind])}
                          onChange={onChange}
                        />
                      </AxisField>
                    </div>
                  )}
                </div>
                {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                <Button onClick={() => onChange(rowPointer, undefined)}>
                  {t('shell.studio.process.removeRecipient')}
                </Button>
              </Flex>
            )
          })}
          <div>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={() => onChange(pointer, [...recipients, { user: '' }])}>
              {t('shell.studio.process.addRecipient')}
            </Button>
          </div>
        </Flex>
      </AxisFieldGroup>
    </div>
  )
}

/** The channels as two checkboxes. They are written in a fixed order, and none checked leaves an empty list. */
function Channels({
  pointer,
  path,
  value,
  onChange,
}: {
  pointer: string
  path: string
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const { group } = useFieldTexts('process')
  const channelsGroup = group(`${path}.channels`)
  if (value !== undefined && !Array.isArray(value)) {
    return <JsonOnly pointer={pointer} {...channelsGroup} />
  }
  const labels = { inApp: t('shell.studio.process.channelInApp'), email: t('shell.studio.process.channelEmail') }

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisFieldGroup {...channelsGroup}>
        <Checkbox.Group
          options={channelOptions.map((option) => ({ value: option, label: labels[option] }))}
          value={(value ?? []) as string[]}
          onChange={(checked) =>
            onChange(
              pointer,
              channelOptions.filter((option) => checked.includes(option)),
            )
          }
        />
      </AxisFieldGroup>
    </div>
  )
}

/** The subject and body, which are text references, and the placeholder values that fill the `{name}` in them. */
function Message({
  pointer,
  path,
  value,
  onChange,
}: {
  pointer: string
  path: string
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const { group } = useFieldTexts('process')
  const subjectGroup = group(`${path}.message.subject`)
  const bodyGroup = group(`${path}.message.body`)
  if (value !== undefined && !isContent(value)) {
    return <JsonOnly pointer={pointer} {...subjectGroup} />
  }
  const message = value ?? {}

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <div data-testid={`studio-field-${pointer}/subject`}>
          <AxisFieldGroup {...subjectGroup}>
            <LabelTexts
              key={`${pointer}/subject`}
              pointer={`${pointer}/subject`}
              value={message.subject}
              onChange={onChange}
            />
          </AxisFieldGroup>
        </div>
        <div data-testid={`studio-field-${pointer}/body`}>
          <AxisFieldGroup {...bodyGroup}>
            <LabelTexts key={`${pointer}/body`} pointer={`${pointer}/body`} value={message.body} onChange={onChange} />
          </AxisFieldGroup>
        </div>
        <MappingRows
          pointer={`${pointer}/values`}
          {...group(`${path}.message.values`)}
          value={message.values}
          keyLabel={t('shell.studio.process.placeholder')}
          target={{ kind: 'expression' }}
          onChange={onChange}
        />
      </Flex>
    </div>
  )
}

/** The optional page to open: a checkbox, then the page and its parameters, which are keyed by the page's own. */
function OpenPage({
  pointer,
  path,
  value,
  onChange,
}: {
  pointer: string
  path: string
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const { field, group } = useFieldTexts('process')
  const names = useContext(StudioNamesContext)
  if (value !== undefined && !isContent(value)) {
    return <JsonOnly pointer={pointer} {...group(`${path}.open`)} />
  }
  const open = value
  const picked =
    open === undefined || typeof open.page !== 'string'
      ? undefined
      : names?.pages.find((page) => page.name.toLowerCase() === (open.page as string).toLowerCase())

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <AxisField {...field(`${path}.open`)}>
          <Checkbox
            checked={open !== undefined}
            onChange={(event) => onChange(pointer, event.target.checked ? { page: '' } : undefined)}
          />
        </AxisField>
        {open !== undefined && (
          <>
            <NamePicker
              pointer={`${pointer}/page`}
              {...field(`${path}.open.page`)}
              kind="page"
              value={open.page}
              onPick={(next) => onChange(`${pointer}/page`, next)}
            />
            <MappingRows
              pointer={`${pointer}/parameters`}
              {...group(`${path}.open.parameters`)}
              value={open.parameters}
              keyLabel={t('shell.studio.page.parameterName')}
              keyOptions={picked?.parameters.map((parameter) => parameter.name)}
              target={{ kind: 'expression' }}
              onChange={onChange}
            />
          </>
        )}
      </Flex>
    </div>
  )
}
