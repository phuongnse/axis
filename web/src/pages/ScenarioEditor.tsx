import { Button, Flex, Input, Select } from 'antd'
import { useContext, type ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { findNamed, freshKey, isObject, pointerSegment, renameKey } from '../platform/scenarioSteps'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useFieldTexts } from './fieldTexts'
import { LabelTexts } from './LabelTexts'
import { ReferencePicker } from './ReferencePicker'
import { ResourceFormSection, ResourceFormSections } from './ResourceFormSection'
import { KeyInput, KeyValueRows, ScenarioStep } from './ScenarioStep'
import type { ChangeHandler } from './SchemaForm'

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
  const texts = useFieldTexts('scenario')
  const names = useContext(StudioNamesContext)
  const connectors = names?.connectors ?? []
  const connector = isObject(stub.connector) ? stub.connector : undefined
  const source = connector ? 'connector' : typeof stub.operation === 'string' ? 'operation' : undefined
  const result = stub.outcome === 'failed' ? 'failed' : isObject(stub.output) ? 'output' : undefined
  const output = isObject(stub.output) ? stub.output : {}
  const connectorName = typeof connector?.name === 'string' ? connector.name : undefined
  const operationNames = findNamed(connectors, connectorName)?.operations.map((operation) => operation.name) ?? []
  const fieldText = (path: string) => texts.field(`stubs.${path}`)

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div data-testid="studio-scenario-stub">
        <Flex vertical gap="small">
          <div data-testid={`studio-field-${pointer}/source`}>
            <AxisField {...fieldText('source')}>
              <Select
                virtual={false}
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
            </AxisField>
          </div>
          {connector && (
            <Flex gap="small">
              <div data-testid={`studio-field-${pointer}/connector/name`}>
                <AxisField {...fieldText('connector.name')}>
                  <ReferencePicker
                    name={fieldText('connector.name').label}
                    value={connectorName}
                    required
                    options={connectors.map((item) => item.name)}
                    onChange={(next) => onChange(`${pointer}/connector/name`, next ?? '')}
                  />
                </AxisField>
              </div>
              <div data-testid={`studio-field-${pointer}/connector/operation`}>
                <AxisField {...fieldText('connector.operation')}>
                  <Select
                    virtual={false}
                    showSearch
                    value={typeof connector.operation === 'string' ? connector.operation : null}
                    style={{ minWidth: 200 }}
                    options={operationNames.map((operation) => ({ value: operation, label: operation }))}
                    onChange={(next: string) => onChange(`${pointer}/connector/operation`, next)}
                  />
                </AxisField>
              </div>
            </Flex>
          )}
          {source === 'operation' && (
            <div data-testid={`studio-field-${pointer}/operation`}>
              <AxisField {...fieldText('operation')}>
                <Input
                  value={typeof stub.operation === 'string' ? stub.operation : ''}
                  onChange={(event) => onChange(`${pointer}/operation`, event.target.value)}
                />
              </AxisField>
            </div>
          )}
          <div data-testid={`studio-field-${pointer}/result`}>
            <AxisField {...fieldText('result')}>
              <Select
                virtual={false}
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
            </AxisField>
          </div>
          {result === 'output' && (
            <KeyValueRows
              pointer={`${pointer}/output`}
              values={output}
              group={texts.group('stubs.output')}
              keyField={fieldText('output.name')}
              addLabel={t('shell.studio.scenario.addOutput')}
              prefix="output"
              onChange={onChange}
            />
          )}
          <div>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
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
  const texts = useFieldTexts('scenario')
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

  function section(name: string, children: ReactNode, action?: ReactNode) {
    const prefix = `shell.studio.section.scenario.${name}`
    return (
      <ResourceFormSection testName={name} heading={{ title: t(prefix), line: t(`${prefix}.line`) }} action={action}>
        <Flex vertical gap="small">
          {children}
        </Flex>
      </ResourceFormSection>
    )
  }

  return (
    <div data-testid="studio-scenario">
      <ResourceFormSections>
        {section(
          'general',
          <>
            <div data-testid="studio-field-/name">
              <AxisField {...texts.field('name')}>
                <Input
                  value={typeof value.name === 'string' ? value.name : ''}
                  onChange={(event) => onChange('/name', event.target.value)}
                />
              </AxisField>
            </div>
            <div data-testid="studio-field-/description">
              <AxisFieldGroup {...texts.group('description')}>
                <LabelTexts pointer="/description" value={value.description} onChange={onChange} />
              </AxisFieldGroup>
            </div>
            <div data-testid="studio-field-/now">
              <AxisField {...texts.field('now')}>
                <Input
                  placeholder="yyyy-MM-ddTHH:mm:ssZ"
                  value={typeof value.now === 'string' ? value.now : ''}
                  onChange={(event) => onChange('/now', event.target.value === '' ? undefined : event.target.value)}
                />
              </AxisField>
            </div>
          </>,
        )}
        {section(
          'users',
          userNames.map((user) => {
            const pointer = `/users/${pointerSegment(user)}`
            const roles = Array.isArray(users[user]) ? (users[user] as string[]) : []
            return (
              <div key={user} data-testid={`studio-field-${pointer}`}>
                <Flex gap="small" align="flex-end">
                  <AxisField {...texts.field('users.id')}>
                    <KeyInput value={user} onRename={(next) => renameUser(user, next)} />
                  </AxisField>
                  <AxisField {...texts.field('users.roles')}>
                    <Select
                      virtual={false}
                      mode="multiple"
                      style={{ minWidth: 240 }}
                      value={roles}
                      options={[...new Set([...roleNames, ...roles])].map((role) => ({ value: role, label: role }))}
                      onChange={(next: string[]) => onChange(pointer, next)}
                    />
                  </AxisField>
                  {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                  <Button onClick={() => onChange(pointer, undefined)}>{t('shell.studio.scenario.remove')}</Button>
                </Flex>
              </div>
            )
          }),
          <AxisButton role="secondary" onClick={() => onChange(`/users/${freshKey(users, 'user')}`, [])}>
            {t('shell.studio.scenario.addUser')}
          </AxisButton>,
        )}
        {section(
          'stubs',
          stubs.map((stub, index) => (
            <StubCard key={index} pointer={`/stubs/${index}`} stub={isObject(stub) ? stub : {}} onChange={onChange} />
          )),
          <AxisButton
            role="secondary"
            onClick={() => onChange('/stubs', [...stubs, { connector: { name: '', operation: '' }, output: {} }])}
          >
            {t('shell.studio.scenario.addStub')}
          </AxisButton>,
        )}
        {section(
          'steps',
          steps.map((_, index) => (
            <ScenarioStep key={index} steps={steps} index={index} users={userNames} onChange={onChange} />
          )),
          <AxisButton
            role="secondary"
            onClick={() => onChange('/steps', [...steps, { type: 'advanceTime', by: 'PT1H' }])}
          >
            {t('shell.studio.scenario.addStep')}
          </AxisButton>,
        )}
      </ResourceFormSections>
    </div>
  )
}
