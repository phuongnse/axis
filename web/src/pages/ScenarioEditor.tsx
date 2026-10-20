import { Button, Flex, Input, Select, Typography } from 'antd'
import { useContext, useState } from 'react'
import {
  findNamed,
  freshKey,
  isObject,
  literalText,
  parseLiteral,
  pointerSegment,
  renameKey,
} from '../platform/scenarioSteps'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { ReferencePicker } from './ReferencePicker'
import { Field, ScenarioStep } from './ScenarioStep'
import type { ChangeHandler } from './SchemaForm'

/** An input for a key of the file. The key changes when the input loses focus, and a key that is taken or empty is refused. */
function KeyInput({ name, value, onRename }: { name: string; value: string; onRename: (next: string) => boolean }) {
  const [text, setText] = useState(value)
  return (
    <Input
      aria-label={name}
      value={text}
      onChange={(event) => setText(event.target.value)}
      onBlur={() => {
        if (text !== value && !onRename(text)) {
          setText(value)
        }
      }}
    />
  )
}

function StubCard({
  pointer,
  stub,
  onChange,
}: {
  pointer: string
  stub: Record<string, unknown>
  onChange: ChangeHandler
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const connectors = names?.connectors ?? []
  const connector = isObject(stub.connector) ? stub.connector : undefined
  const source = connector ? 'connector' : typeof stub.operation === 'string' ? 'operation' : undefined
  const result = stub.outcome === 'failed' ? 'failed' : isObject(stub.output) ? 'output' : undefined
  const output = isObject(stub.output) ? stub.output : {}
  const connectorName = typeof connector?.name === 'string' ? connector.name : undefined
  const operations = findNamed(connectors, connectorName)?.operations.map((operation) => operation.name) ?? []

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div data-testid="studio-scenario-stub">
        <Flex vertical gap="small">
          <Field pointer={`${pointer}/source`} name={t('shell.studio.scenario.source')}>
            <Select
              virtual={false}
              aria-label={t('shell.studio.scenario.source')}
              value={source ?? null}
              style={{ minWidth: 200 }}
              options={[
                { value: 'connector', label: t('shell.studio.scenario.connector') },
                { value: 'operation', label: t('shell.studio.scenario.package') },
              ]}
              onChange={(next: string) => {
                onChange(`${pointer}/connector`, next === 'connector' ? { name: '', operation: '' } : undefined)
                onChange(`${pointer}/operation`, next === 'operation' ? '' : undefined)
              }}
            />
          </Field>
          {connector && (
            <Flex gap="small">
              <Field pointer={`${pointer}/connector/name`} name={t('shell.studio.scenario.connector')}>
                <ReferencePicker
                  name={t('shell.studio.scenario.connector')}
                  value={connectorName}
                  required
                  options={connectors.map((item) => item.name)}
                  onChange={(next) => onChange(`${pointer}/connector/name`, next ?? '')}
                />
              </Field>
              <Field pointer={`${pointer}/connector/operation`} name="operation">
                <Select
                  virtual={false}
                  aria-label="operation"
                  showSearch
                  value={typeof connector.operation === 'string' ? connector.operation : null}
                  style={{ minWidth: 200 }}
                  options={operations.map((operation) => ({ value: operation, label: operation }))}
                  onChange={(next: string) => onChange(`${pointer}/connector/operation`, next)}
                />
              </Field>
            </Flex>
          )}
          {source === 'operation' && (
            <Field pointer={`${pointer}/operation`} name="operation">
              <Input
                aria-label="operation"
                value={typeof stub.operation === 'string' ? stub.operation : ''}
                onChange={(event) => onChange(`${pointer}/operation`, event.target.value)}
              />
            </Field>
          )}
          <Field pointer={`${pointer}/result`} name={t('shell.studio.scenario.result')}>
            <Select
              virtual={false}
              aria-label={t('shell.studio.scenario.result')}
              value={result ?? null}
              style={{ minWidth: 200 }}
              options={[
                { value: 'output', label: t('shell.studio.scenario.output') },
                { value: 'failed', label: t('shell.studio.scenario.failed') },
              ]}
              onChange={(next: string) => {
                onChange(`${pointer}/output`, next === 'output' ? {} : undefined)
                onChange(`${pointer}/outcome`, next === 'failed' ? 'failed' : undefined)
              }}
            />
          </Field>
          {result === 'output' && (
            <Flex vertical gap="small">
              {Object.keys(output).map((key) => (
                <Flex key={key} gap="small">
                  <KeyInput
                    name={t('shell.studio.scenario.outputName')}
                    value={key}
                    onRename={(next) => {
                      const renamed = renameKey(output, key, next)
                      if (renamed === undefined) {
                        return false
                      }
                      onChange(`${pointer}/output`, renamed)
                      return true
                    }}
                  />
                  <Input
                    aria-label={key}
                    value={literalText(output[key])}
                    onChange={(event) =>
                      onChange(`${pointer}/output/${pointerSegment(key)}`, parseLiteral(event.target.value))
                    }
                  />
                  <Button onClick={() => onChange(`${pointer}/output/${pointerSegment(key)}`, undefined)}>
                    {t('shell.studio.scenario.remove')}
                  </Button>
                </Flex>
              ))}
              <div>
                <Button onClick={() => onChange(`${pointer}/output/${freshKey(output, 'output')}`, '')}>
                  {t('shell.studio.scenario.addOutput')}
                </Button>
              </div>
            </Flex>
          )}
          <div>
            <Button onClick={() => onChange(pointer, undefined)}>{t('shell.studio.scenario.remove')}</Button>
          </div>
        </Flex>
      </div>
    </div>
  )
}

/**
 * The typed editor of a scenario: its name, the time it starts at, its users with their roles, its stubs and its steps.
 * It edits the draft through `onChange`, which takes the JSON Pointer of the value and the new value. A key that the editor
 * does not show stays in the draft.
 */
export function ScenarioEditor({ value, onChange }: { value: Record<string, unknown>; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const users = isObject(value.users) ? value.users : {}
  const userNames = Object.keys(users)
  const steps = Array.isArray(value.steps) ? value.steps : []
  const stubs = Array.isArray(value.stubs) ? value.stubs : []
  const roleNames = names?.roles.map((role) => role.name) ?? []

  function renameUser(from: string, next: string): boolean {
    const renamed = renameKey(users, from, next)
    if (renamed === undefined) {
      return false
    }
    onChange('/users', renamed)
    return true
  }

  return (
    <Flex vertical gap="middle" data-testid="studio-scenario">
      <Field pointer="/name" name={t('shell.studio.scenario.name')}>
        <Input
          aria-label={t('shell.studio.scenario.name')}
          value={typeof value.name === 'string' ? value.name : ''}
          onChange={(event) => onChange('/name', event.target.value)}
        />
      </Field>
      <Field pointer="/now" name={t('shell.studio.scenario.now')}>
        <Input
          aria-label={t('shell.studio.scenario.now')}
          placeholder="yyyy-MM-ddTHH:mm:ssZ"
          value={typeof value.now === 'string' ? value.now : ''}
          onChange={(event) => onChange('/now', event.target.value === '' ? undefined : event.target.value)}
        />
      </Field>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.scenario.users')}</Typography.Text>
        {userNames.map((user) => {
          const pointer = `/users/${pointerSegment(user)}`
          const roles = Array.isArray(users[user]) ? (users[user] as string[]) : []
          return (
            <Field key={user} pointer={pointer} name={t('shell.studio.scenario.userId')}>
              <Flex gap="small" align="center">
                <KeyInput
                  name={t('shell.studio.scenario.userId')}
                  value={user}
                  onRename={(next) => renameUser(user, next)}
                />
                <Select
                  virtual={false}
                  aria-label={t('shell.studio.scenario.roles')}
                  mode="multiple"
                  style={{ minWidth: 240 }}
                  value={roles}
                  options={[...new Set([...roleNames, ...roles])].map((role) => ({ value: role, label: role }))}
                  onChange={(next: string[]) => onChange(pointer, next)}
                />
                <Button onClick={() => onChange(pointer, undefined)}>{t('shell.studio.scenario.remove')}</Button>
              </Flex>
            </Field>
          )
        })}
        <div>
          <Button onClick={() => onChange(`/users/${freshKey(users, 'user')}`, [])}>
            {t('shell.studio.scenario.addUser')}
          </Button>
        </div>
      </Flex>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.scenario.stubs')}</Typography.Text>
        {stubs.map((stub, index) => (
          <StubCard key={index} pointer={`/stubs/${index}`} stub={isObject(stub) ? stub : {}} onChange={onChange} />
        ))}
        <div>
          <Button
            onClick={() => onChange('/stubs', [...stubs, { connector: { name: '', operation: '' }, output: {} }])}
          >
            {t('shell.studio.scenario.addStub')}
          </Button>
        </div>
      </Flex>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.scenario.steps')}</Typography.Text>
        {steps.map((_, index) => (
          <ScenarioStep key={index} steps={steps} index={index} users={userNames} onChange={onChange} />
        ))}
        <div>
          <Button onClick={() => onChange('/steps', [...steps, { type: 'advanceTime', by: 'PT1H' }])}>
            {t('shell.studio.scenario.addStep')}
          </Button>
        </div>
      </Flex>
    </Flex>
  )
}
