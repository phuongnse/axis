import { Button, Checkbox, Flex, Input, Radio, Typography } from 'antd'
import { useContext } from 'react'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { LabelTextsContext } from './labelTextsContext'
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

function JsonOnly({ pointer, title }: { pointer: string; title: string }) {
  const t = useText()
  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div>{title}</div>
      <Typography.Text type="secondary" data-testid="studio-json-only">
        {t('shell.studio.editInJson')}
      </Typography.Text>
    </div>
  )
}

/**
 * The editor of a notification: its recipients, channels, subject and body, placeholder values and an optional page
 * to open. `pointer` is the object that holds the `to`, `channels`, `message` and `open` keys, and the editor never
 * shows the `next` of a step that holds them, so a task's escalation can use it too. Each value is edited through its
 * own pointer, and an empty list stays in the draft for the compiler to report.
 */
export function NotifyFields({
  pointer,
  value,
  onChange,
}: {
  pointer: string
  value: Content
  onChange: ChangeHandler
}) {
  return (
    <>
      <Recipients pointer={`${pointer}/to`} value={value.to} onChange={onChange} />
      <Channels pointer={`${pointer}/channels`} value={value.channels} onChange={onChange} />
      <Message pointer={`${pointer}/message`} value={value.message} onChange={onChange} />
      <OpenPage pointer={`${pointer}/open`} value={value.open} onChange={onChange} />
    </>
  )
}

/** The recipients as rows. A row is a user expression, a role or an email expression, and a switch keeps one key. */
function Recipients({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  if (value !== undefined && !Array.isArray(value)) {
    return <JsonOnly pointer={pointer} title={t('shell.studio.process.recipients')} />
  }
  const recipients: unknown[] = value ?? []
  const labels: Record<RecipientKind, string> = {
    user: t('shell.studio.process.recipientUser'),
    role: t('shell.studio.process.recipientRole'),
    email: t('shell.studio.process.recipientEmail'),
  }

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.process.recipients')}</Typography.Text>
        {recipients.map((recipient, index) => {
          const row = isContent(recipient) ? recipient : {}
          const rowPointer = `${pointer}/${index}`
          const kind = recipientKinds.find((candidate) => row[candidate] !== undefined) ?? 'user'
          return (
            <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${rowPointer}`}>
              <Radio.Group
                name={`recipient-${rowPointer}`}
                optionType="button"
                aria-label={t('shell.studio.process.recipients')}
                value={kind}
                options={recipientKinds.map((candidate) => ({ value: candidate, label: labels[candidate] }))}
                onChange={(event) => onChange(rowPointer, { [event.target.value]: text(row[event.target.value]) })}
              />
              <div style={{ flex: 1, minWidth: 240 }}>
                {kind === 'role' ? (
                  <NamePicker
                    pointer={`${rowPointer}/role`}
                    label={labels.role}
                    kind="role"
                    value={row.role}
                    onPick={(next) => onChange(`${rowPointer}/role`, next)}
                  />
                ) : (
                  <div data-testid={`studio-field-${rowPointer}/${kind}`}>
                    <Expression
                      pointer={`${rowPointer}/${kind}`}
                      name={labels[kind]}
                      value={text(row[kind])}
                      onChange={onChange}
                    />
                  </div>
                )}
              </div>
              <Button onClick={() => onChange(rowPointer, undefined)}>
                {t('shell.studio.process.removeRecipient')}
              </Button>
            </Flex>
          )
        })}
        <div>
          <Button onClick={() => onChange(pointer, [...recipients, { user: '' }])}>
            {t('shell.studio.process.addRecipient')}
          </Button>
        </div>
      </Flex>
    </div>
  )
}

/** The channels as two checkboxes. They are written in a fixed order, and none checked leaves an empty list. */
function Channels({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  if (value !== undefined && !Array.isArray(value)) {
    return <JsonOnly pointer={pointer} title={t('shell.studio.process.channels')} />
  }
  const labels = { inApp: t('shell.studio.process.channelInApp'), email: t('shell.studio.process.channelEmail') }

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.process.channels')}</Typography.Text>
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
      </Flex>
    </div>
  )
}

/** A text reference as a key and a text box per locale, or as the plain key until the names are loaded. */
function TextReference({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)

  if (names !== undefined && label !== null) {
    return <LabelTexts key={pointer} pointer={pointer} value={value} onChange={onChange} />
  }
  return (
    <Input
      aria-label={t('shell.studio.labelKey')}
      value={text(isContent(value) ? value.textKey : undefined)}
      onChange={(event) => onChange(`${pointer}/textKey`, event.target.value)}
    />
  )
}

/** The subject and body, which are text references, and the placeholder values that fill the `{name}` in them. */
function Message({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  if (value !== undefined && !isContent(value)) {
    return <JsonOnly pointer={pointer} title={t('shell.studio.process.notifySubject')} />
  }
  const message = value ?? {}

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <div data-testid={`studio-field-${pointer}/subject`}>
          <div>{t('shell.studio.process.notifySubject')}</div>
          <TextReference pointer={`${pointer}/subject`} value={message.subject} onChange={onChange} />
        </div>
        <div data-testid={`studio-field-${pointer}/body`}>
          <div>{t('shell.studio.process.notifyBody')}</div>
          <TextReference pointer={`${pointer}/body`} value={message.body} onChange={onChange} />
        </div>
        <MappingRows
          pointer={`${pointer}/values`}
          title={t('shell.studio.process.placeholderValues')}
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
function OpenPage({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  if (value !== undefined && !isContent(value)) {
    return <JsonOnly pointer={pointer} title={t('shell.studio.process.openPage')} />
  }
  const open = value
  const picked =
    open === undefined || typeof open.page !== 'string'
      ? undefined
      : names?.pages.find((page) => page.name.toLowerCase() === (open.page as string).toLowerCase())

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <Checkbox
          checked={open !== undefined}
          onChange={(event) => onChange(pointer, event.target.checked ? { page: '' } : undefined)}
        >
          {t('shell.studio.process.openPage')}
        </Checkbox>
        {open !== undefined && (
          <>
            <NamePicker
              pointer={`${pointer}/page`}
              label={t('shell.studio.process.openPageName')}
              kind="page"
              value={open.page}
              onPick={(next) => onChange(`${pointer}/page`, next)}
            />
            <MappingRows
              pointer={`${pointer}/parameters`}
              title={t('shell.studio.process.pageParameters')}
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
