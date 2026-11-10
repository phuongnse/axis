import { Button, Flex, Input, Radio, Select, Typography } from 'antd'
import { useContext, type ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useFieldTexts } from './fieldTexts'
import { LabelTexts } from './LabelTexts'
import { ReferencePicker } from './ReferencePicker'
import { ResourceFormSection, ResourceFormSections } from './ResourceFormSection'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

const types = ['accounts', 'oidc', 'saml']

/** The keys a type forbids, from the identity provider schema. */
const forbiddenKeys: Record<string, string[]> = {
  accounts: ['label', 'issuer', 'clientId', 'clientSecret', 'clientKey', 'metadata', 'claims'],
  oidc: ['metadata'],
  saml: ['issuer', 'clientId', 'clientSecret', 'clientKey'],
}

/** The claim names that apply when a provider sets none. */
const claimDefaults = [
  { key: 'displayName', placeholder: 'name' },
  { key: 'email', placeholder: 'email' },
  { key: 'locale', placeholder: 'locale' },
] as const

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/** The name of a binding. Only a string name is shown, so nothing else a file holds can appear in the box. */
function bindingName(value: unknown): string {
  return isContent(value) ? text(value.binding) : ''
}

/**
 * The editor of an identity provider, in the shared sections General, Sign-in, Claims, Roles and Assurance, see
 * docs/reference/studio.md#identity-provider-editor. General holds the name, the type and the description. Sign-in
 * and Claims show only for `oidc` and `saml`: the bindings, the client credential and the "Continue with" label, and
 * the claims. Roles holds the role mapping and Assurance the assurance levels. Each field is drawn through
 * `AxisField` with its label and help. A binding is only a name: the box neither shows nor writes a value, because
 * values live in tenant settings. The editor edits the draft through `onChange`, so a key it does not show stays as it
 * is.
 */
export function IdentityProviderEditor({ value, onChange }: { value: Content; onChange: ChangeHandler }) {
  const t = useText()
  const { field, group } = useFieldTexts('identityProvider')
  const names = useContext(StudioNamesContext)
  const type = typeof value.type === 'string' ? value.type : undefined
  const claims = isContent(value.claims) ? value.claims : {}
  const roles = isContent(value.roles) ? value.roles : {}
  const roleValues = isContent(roles.values) ? roles.values : {}
  const acr = Array.isArray(value.acr) ? value.acr : []
  const usesKey = isContent(value.clientKey)

  function changeType(next: string) {
    // Each call stacks on the draft. The keys the new type forbids go, because the schema refuses them.
    onChange('/type', next)
    for (const key of forbiddenKeys[next]) {
      onChange(`/${key}`, undefined)
    }
    const required =
      next === 'oidc'
        ? ['issuer', 'clientId', ...(value.clientKey === undefined ? ['clientSecret'] : [])]
        : next === 'saml'
          ? ['metadata']
          : []
    for (const key of required) {
      if (value[key] === undefined) {
        onChange(`/${key}`, { binding: '' })
      }
    }
  }

  function changeCredential(next: 'clientSecret' | 'clientKey') {
    onChange(next === 'clientKey' ? '/clientSecret' : '/clientKey', undefined)
    onChange(`/${next}`, { binding: '' })
  }

  function changeClaim(key: string, next: string) {
    if (next !== '') {
      onChange(`/claims/${key}`, next)
    } else if (Object.keys(claims).some((other) => other !== key)) {
      onChange(`/claims/${key}`, undefined)
    } else {
      onChange('/claims', undefined)
    }
  }

  function changeRolesClaim(next: string) {
    if (next !== '') {
      onChange('/roles/claim', next)
    } else if (Object.keys(roles).some((other) => other !== 'claim')) {
      onChange('/roles/claim', undefined)
    } else {
      onChange('/roles', undefined)
    }
  }

  /** Writes the whole `values` object, so the order of the rows is kept and no key needs escaping. */
  function writeRoleValues(next: [string, string][]) {
    if (next.length > 0) {
      onChange('/roles/values', Object.fromEntries(next))
    } else if (Object.keys(roles).some((other) => other !== 'values')) {
      onChange('/roles/values', undefined)
    } else {
      onChange('/roles', undefined)
    }
  }

  function addRoleRow() {
    let index = Object.keys(roleValues).length + 1
    while (`value${index}` in roleValues) {
      index += 1
    }
    writeRoleValues([
      ...Object.entries(roleValues).map(([claim, role]): [string, string] => [claim, text(role)]),
      [`value${index}`, ''],
    ])
  }

  function changeRoleRow(position: number, claim: string, role: string) {
    const rows = Object.entries(roleValues).map(([key, current]): [string, string] => [key, text(current)])
    if (claim !== rows[position][0] && rows.some(([other], index) => index !== position && other === claim)) {
      return
    }
    writeRoleValues(rows.map((row, index): [string, string] => (index === position ? [claim, role] : row)))
  }

  function textField(
    pointer: string,
    path: string,
    current: unknown,
    placeholder?: string,
    write?: (next: string) => void,
  ) {
    return (
      <div data-testid={`studio-field-${pointer}`}>
        <AxisField {...field(path)}>
          <Input
            placeholder={placeholder}
            value={text(current)}
            onChange={(event) => (write ?? ((next: string) => onChange(pointer, next)))(event.target.value)}
          />
        </AxisField>
      </div>
    )
  }

  function bindingField(key: string) {
    return (
      <div data-testid={`studio-field-/${key}`}>
        <AxisField {...field(key)}>
          <Input
            value={bindingName(value[key])}
            onChange={(event) => onChange(`/${key}/binding`, event.target.value)}
          />
        </AxisField>
      </div>
    )
  }

  function section(name: string, children: ReactNode, action?: ReactNode) {
    const prefix = `shell.studio.section.identityProvider.${name}`
    return (
      <ResourceFormSection testName={name} heading={{ title: t(prefix), line: t(`${prefix}.line`) }} action={action}>
        <Flex vertical gap="small">
          {children}
        </Flex>
      </ResourceFormSection>
    )
  }

  const takesSignIn = type === 'oidc' || type === 'saml'

  return (
    <div data-testid="studio-identity-provider-editor">
      <ResourceFormSections>
        {section(
          'general',
          <>
            {textField('/name', 'name', value.name)}
            <div data-testid="studio-field-/type">
              <AxisField {...field('type')}>
                <Select
                  style={{ minWidth: 240 }}
                  virtual={false}
                  value={type}
                  options={types.map((option) => ({ value: option, label: option }))}
                  onChange={(next: string) => changeType(next)}
                />
              </AxisField>
            </div>
            <div data-testid="studio-field-/description">
              <AxisFieldGroup {...group('description')}>
                <LabelTexts pointer="/description" value={value.description} onChange={onChange} />
              </AxisFieldGroup>
            </div>
          </>,
        )}
        {takesSignIn &&
          section(
            'signIn',
            <>
              {type === 'oidc' && (
                <>
                  {bindingField('issuer')}
                  {bindingField('clientId')}
                  <div data-testid="studio-field-/clientCredential">
                    <AxisField {...field('clientCredential')}>
                      <Radio.Group
                        role="radiogroup"
                        aria-label={field('clientCredential').label}
                        value={usesKey ? 'clientKey' : 'clientSecret'}
                        options={[
                          { value: 'clientSecret', label: field('clientSecret').label },
                          { value: 'clientKey', label: field('clientKey').label },
                        ]}
                        onChange={(event) => changeCredential(event.target.value as 'clientSecret' | 'clientKey')}
                      />
                    </AxisField>
                  </div>
                  {bindingField(usesKey ? 'clientKey' : 'clientSecret')}
                </>
              )}
              {type === 'saml' && bindingField('metadata')}
              <div data-testid="studio-field-/label">
                <AxisFieldGroup {...group('label')}>
                  <LabelTexts pointer="/label" value={value.label} onChange={onChange} />
                </AxisFieldGroup>
              </div>
            </>,
          )}
        {takesSignIn &&
          section(
            'claims',
            <div data-testid="studio-field-/claims">
              <Flex vertical gap="small">
                {claimDefaults.map((claim) =>
                  textField(
                    `/claims/${claim.key}`,
                    `claims.${claim.key}`,
                    claims[claim.key],
                    claim.placeholder,
                    (next) => changeClaim(claim.key, next),
                  ),
                )}
              </Flex>
            </div>,
          )}
        {section(
          'roles',
          <div data-testid="studio-field-/roles">
            <Flex vertical gap="small">
              {textField('/roles/claim', 'roles.claim', roles.claim, 'roles', changeRolesClaim)}
              <div data-testid="studio-field-/roles/values">
                <AxisFieldGroup {...group('roles.values')}>
                  <Flex vertical gap="small">
                    {Object.entries(roleValues).map(([claim, role], position) => (
                      <Flex
                        key={position}
                        gap="small"
                        align="flex-end"
                        wrap
                        data-testid={`studio-role-row-${position}`}
                      >
                        <AxisField {...field('roles.values.claimValue')}>
                          <Input
                            value={claim}
                            onChange={(event) => changeRoleRow(position, event.target.value, text(role))}
                          />
                        </AxisField>
                        <AxisField {...field('roles.values.role')}>
                          <ReferencePicker
                            name={field('roles.values.role').label}
                            value={text(role)}
                            required
                            options={names?.roles.map((declared) => declared.name) ?? []}
                            onChange={(next) => changeRoleRow(position, claim, next ?? '')}
                          />
                        </AxisField>
                        {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                        <Button
                          onClick={() =>
                            writeRoleValues(
                              Object.entries(roleValues)
                                .map(([key, current]): [string, string] => [key, text(current)])
                                .filter((_, index) => index !== position),
                            )
                          }
                        >
                          {t('shell.studio.identityProvider.remove')}
                        </Button>
                      </Flex>
                    ))}
                    <div>
                      {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                      <Button onClick={addRoleRow}>{t('shell.studio.identityProvider.addRole')}</Button>
                    </div>
                    {Object.keys(roleValues).length === 0 && (
                      <Typography.Text type="secondary" data-testid="studio-identity-provider-same-name">
                        {t('shell.studio.identityProvider.sameNameHint')}
                      </Typography.Text>
                    )}
                  </Flex>
                </AxisFieldGroup>
              </div>
            </Flex>
          </div>,
        )}
        {section(
          'assurance',
          <div data-testid="studio-field-/acr">
            <Flex vertical gap="small">
              {acr.map((level, index) => (
                <Flex key={index} gap="small" align="flex-end" data-testid={`studio-field-/acr/${index}`}>
                  <AxisField {...field('acr')}>
                    <Input value={text(level)} onChange={(event) => onChange(`/acr/${index}`, event.target.value)} />
                  </AxisField>
                  {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                  <Button
                    onClick={() =>
                      onChange('/acr', acr.length > 1 ? acr.filter((_, position) => position !== index) : undefined)
                    }
                  >
                    {t('shell.studio.identityProvider.remove')}
                  </Button>
                </Flex>
              ))}
            </Flex>
          </div>,
          <AxisButton role="secondary" onClick={() => onChange('/acr', [...acr, ''])}>
            {t('shell.studio.identityProvider.addAcr')}
          </AxisButton>,
        )}
      </ResourceFormSections>
    </div>
  )
}
