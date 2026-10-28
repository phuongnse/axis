import { Button, Flex, Input, Radio, Select, Typography } from 'antd'
import { useContext } from 'react'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { LabelTexts } from './LabelTexts'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'
import { LabelTextsContext } from './labelTextsContext'

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
  { key: 'displayName', placeholder: 'name', label: 'shell.studio.identityProvider.displayNameClaim' },
  { key: 'email', placeholder: 'email', label: 'shell.studio.identityProvider.emailClaim' },
  { key: 'locale', placeholder: 'locale', label: 'shell.studio.identityProvider.localeClaim' },
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
 * The editor of an identity provider. It shows the name and the type, then the fields that the type takes: the
 * bindings, the "Continue with" label, the claims, the role mapping and the assurance levels. A binding is only a
 * name: the box neither shows nor writes a value, because values live in tenant settings. The editor edits the draft
 * through `onChange`, so a key it does not show stays as it is.
 */
export function IdentityProviderEditor({ value, onChange }: { value: Content; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
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
    name: string,
    current: unknown,
    placeholder?: string,
    write?: (next: string) => void,
  ) {
    return (
      <div data-testid={`studio-field-${pointer}`}>
        <div>{name}</div>
        <Input
          aria-label={name}
          placeholder={placeholder}
          value={text(current)}
          onChange={(event) => (write ?? ((next: string) => onChange(pointer, next)))(event.target.value)}
        />
      </div>
    )
  }

  function bindingField(key: string, name: string, kind: 'url' | 'secret') {
    const kindText = t(
      kind === 'url' ? 'shell.studio.identityProvider.urlBinding' : 'shell.studio.identityProvider.secretBinding',
    )
    return (
      <div data-testid={`studio-field-/${key}`}>
        <div>
          {name} <Typography.Text type="secondary">({kindText})</Typography.Text>
        </div>
        <Input
          aria-label={`${name} (${kindText})`}
          value={bindingName(value[key])}
          onChange={(event) => onChange(`/${key}/binding`, event.target.value)}
        />
        <Typography.Text type="secondary">{t('shell.studio.identityProvider.bindingHint')}</Typography.Text>
      </div>
    )
  }

  return (
    <Flex vertical gap="small" data-testid="studio-identity-provider-editor">
      {textField('/name', t('shell.studio.identityProvider.name'), value.name)}
      <div data-testid="studio-field-/type">
        <div>{t('shell.studio.identityProvider.type')}</div>
        <Select
          aria-label={t('shell.studio.identityProvider.type')}
          style={{ minWidth: 240 }}
          virtual={false}
          value={type}
          options={types.map((option) => ({ value: option, label: option }))}
          onChange={(next: string) => changeType(next)}
        />
      </div>
      {type === 'oidc' && (
        <>
          {bindingField('issuer', t('shell.studio.identityProvider.issuer'), 'url')}
          {bindingField('clientId', t('shell.studio.identityProvider.clientId'), 'secret')}
          <div data-testid="studio-field-/clientCredential">
            <div>{t('shell.studio.identityProvider.clientCredential')}</div>
            <Radio.Group
              aria-label={t('shell.studio.identityProvider.clientCredential')}
              value={usesKey ? 'clientKey' : 'clientSecret'}
              options={[
                { value: 'clientSecret', label: t('shell.studio.identityProvider.clientSecret') },
                { value: 'clientKey', label: t('shell.studio.identityProvider.clientKey') },
              ]}
              onChange={(event) => changeCredential(event.target.value as 'clientSecret' | 'clientKey')}
            />
          </div>
          {usesKey
            ? bindingField('clientKey', t('shell.studio.identityProvider.clientKey'), 'secret')
            : bindingField('clientSecret', t('shell.studio.identityProvider.clientSecret'), 'secret')}
        </>
      )}
      {type === 'saml' && bindingField('metadata', t('shell.studio.identityProvider.metadata'), 'url')}
      {(type === 'oidc' || type === 'saml') && (
        <div data-testid="studio-field-/label">
          <div>{t('shell.studio.identityProvider.label')}</div>
          {names !== undefined && label !== null ? (
            <LabelTexts pointer="/label" value={value.label} onChange={onChange} />
          ) : (
            <Input
              aria-label={t('shell.studio.labelKey')}
              value={text(isContent(value.label) ? value.label.textKey : undefined)}
              onChange={(event) => onChange('/label/textKey', event.target.value)}
            />
          )}
        </div>
      )}
      {(type === 'oidc' || type === 'saml') && (
        <div data-testid="studio-field-/claims">
          <Flex vertical gap="small">
            <Typography.Text strong>{t('shell.studio.identityProvider.claims')}</Typography.Text>
            {claimDefaults.map((claim) =>
              textField(`/claims/${claim.key}`, t(claim.label), claims[claim.key], claim.placeholder, (next) =>
                changeClaim(claim.key, next),
              ),
            )}
          </Flex>
        </div>
      )}
      <div data-testid="studio-field-/roles">
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.studio.identityProvider.roles')}</Typography.Text>
          {textField(
            '/roles/claim',
            t('shell.studio.identityProvider.rolesClaim'),
            roles.claim,
            'roles',
            changeRolesClaim,
          )}
          <div data-testid="studio-field-/roles/values">
            <Flex vertical gap="small">
              <div>{t('shell.studio.identityProvider.roleValues')}</div>
              {Object.entries(roleValues).map(([claim, role], position) => (
                <Flex key={position} gap="small" align="center" wrap data-testid={`studio-role-row-${position}`}>
                  <Input
                    aria-label={t('shell.studio.identityProvider.claimValue')}
                    value={claim}
                    onChange={(event) => changeRoleRow(position, event.target.value, text(role))}
                  />
                  <ReferencePicker
                    name={t('shell.studio.identityProvider.role')}
                    value={text(role)}
                    required
                    options={names?.roles.map((declared) => declared.name) ?? []}
                    onChange={(next) => changeRoleRow(position, claim, next ?? '')}
                  />
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
                <Button onClick={addRoleRow}>{t('shell.studio.identityProvider.addRole')}</Button>
              </div>
              {Object.keys(roleValues).length === 0 && (
                <Typography.Text type="secondary" data-testid="studio-identity-provider-same-name">
                  {t('shell.studio.identityProvider.sameNameHint')}
                </Typography.Text>
              )}
            </Flex>
          </div>
        </Flex>
      </div>
      <div data-testid="studio-field-/acr">
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.studio.identityProvider.acr')}</Typography.Text>
          {acr.map((level, index) => (
            <Flex key={index} gap="small" align="center" data-testid={`studio-field-/acr/${index}`}>
              <Input
                aria-label={t('shell.studio.identityProvider.acr')}
                value={text(level)}
                onChange={(event) => onChange(`/acr/${index}`, event.target.value)}
              />
              <Button
                onClick={() =>
                  onChange('/acr', acr.length > 1 ? acr.filter((_, position) => position !== index) : undefined)
                }
              >
                {t('shell.studio.identityProvider.remove')}
              </Button>
            </Flex>
          ))}
          <div>
            <Button onClick={() => onChange('/acr', [...acr, ''])}>{t('shell.studio.identityProvider.addAcr')}</Button>
          </div>
        </Flex>
      </div>
    </Flex>
  )
}
