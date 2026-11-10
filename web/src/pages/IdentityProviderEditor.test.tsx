import { type BoundFunctions, type ByRoleMatcher, type queries, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { optionTexts } from '../test/antd'
import { IdentityProviderEditor } from './IdentityProviderEditor'

const names = {
  locales: ['en'],
  roles: [
    { id: 'r1', name: 'department-manager' },
    { id: 'r2', name: 'finance-reviewer' },
  ],
  texts: [],
} as unknown as StudioNames

const oidc = {
  id: '5d0b8c1e-2f4a-4b6c-9d8e-7a1b2c3d4e51',
  kind: 'identityProvider',
  name: 'company',
  formatVersion: 1,
  type: 'oidc',
  label: { textKey: 'signIn.company' },
  issuer: { binding: 'CompanyIssuer' },
  clientId: { binding: 'CompanyClientId' },
  clientSecret: { binding: 'CompanyClientSecret' },
  claims: { displayName: 'name' },
  roles: { claim: 'groups', values: { managers: 'department-manager', finance: 'finance-reviewer' } },
  acr: ['urn:level2'],
  extra: { kept: true },
}

const accounts = { id: oidc.id, kind: 'identityProvider', name: 'accounts', formatVersion: 1, type: 'accounts' }

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderEditor(
  value: Record<string, unknown> = oidc,
  onChange = vi.fn(),
  loaded: StudioNames | undefined = names,
) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={loaded}>
            <IdentityProviderEditor value={value} onChange={onChange} />
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

/** The label and the help of an identity provider field's text, in English. */
function texts(path: string) {
  const key = `shell.studio.field.identityProvider.${path}`
  const catalogue: Record<string, string> = platformEnglish
  return { label: catalogue[key], help: catalogue[`${key}.help`] }
}

/** The input with the label of an identity provider field, which has the field's help as its description. */
function field(path: string, role: ByRoleMatcher = 'textbox', scope: BoundFunctions<typeof queries> = screen) {
  const { label, help } = texts(path)
  const input = scope.getByRole(role, { name: label })
  expect(input).toHaveAccessibleDescription(help)
  return input
}

async function chooseType(type: string) {
  await userEvent.click(field('type', 'combobox'))
  await userEvent.click(await screen.findByTitle(type))
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('IdentityProviderEditor in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with the fields of the type', () => {
    renderEditor()

    const editor = screen.getByTestId('studio-identity-provider-editor')
    expect(editor.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(editor).toBeVisible()
    expect(field('name')).toHaveValue('company')
    expect(field('issuer')).toHaveValue('CompanyIssuer')
    expect(field('clientId')).toHaveValue('CompanyClientId')
    expect(field('clientSecret')).toHaveValue('CompanyClientSecret')
    expect(field('claims.displayName')).toHaveValue('name')
    expect(field('claims.email')).toHaveAttribute('placeholder', 'email')
  })
})

describe('IdentityProviderEditor', () => {
  it('shows each binding as a name box and never a value, whatever else the file holds', () => {
    renderEditor({ ...oidc, issuer: { binding: 'CompanyIssuer', value: 'https://secret.example' } })

    expect(screen.queryByDisplayValue('https://secret.example')).not.toBeInTheDocument()
    expect(field('issuer')).toHaveValue('CompanyIssuer')
  })

  it('edits a binding by its name at the binding pointer', async () => {
    const onChange = renderEditor()

    await userEvent.type(field('issuer'), 'X')

    expect(onChange).toHaveBeenLastCalledWith('/issuer/binding', 'CompanyIssuerX')
  })

  it('drops the keys of an oidc provider that accounts forbids', async () => {
    const onChange = renderEditor()

    await chooseType('accounts')

    expect(onChange.mock.calls).toEqual([
      ['/type', 'accounts'],
      ['/label', undefined],
      ['/issuer', undefined],
      ['/clientId', undefined],
      ['/clientSecret', undefined],
      ['/clientKey', undefined],
      ['/metadata', undefined],
      ['/claims', undefined],
    ])
  })

  it('drops the oidc bindings and adds the metadata binding for saml', async () => {
    const onChange = renderEditor()

    await chooseType('saml')

    expect(onChange.mock.calls).toEqual([
      ['/type', 'saml'],
      ['/issuer', undefined],
      ['/clientId', undefined],
      ['/clientSecret', undefined],
      ['/clientKey', undefined],
      ['/metadata', { binding: '' }],
    ])
  })

  it('adds the bindings oidc requires with an empty name and drops the metadata', async () => {
    const onChange = renderEditor(accounts)

    await chooseType('oidc')

    expect(onChange.mock.calls).toEqual([
      ['/type', 'oidc'],
      ['/metadata', undefined],
      ['/issuer', { binding: '' }],
      ['/clientId', { binding: '' }],
      ['/clientSecret', { binding: '' }],
    ])
  })

  it('shows the description after the name and the type, before the label, and writes its key', async () => {
    const onChange = renderEditor({ ...oidc, description: { textKey: 'signIn.company.description' } })

    const description = screen.getByTestId('studio-field-/description')
    const box = within(description).getByRole('textbox', { name: 'Text key' })
    expect(box).toHaveValue('signIn.company.description')
    for (const before of ['/name', '/type']) {
      expect(
        screen.getByTestId(`studio-field-${before}`).compareDocumentPosition(description) &
          Node.DOCUMENT_POSITION_FOLLOWING,
      ).not.toBe(0)
    }
    expect(
      description.compareDocumentPosition(screen.getByTestId('studio-field-/label')) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).not.toBe(0)

    await userEvent.type(box, 'x')
    expect(onChange).toHaveBeenLastCalledWith('/description/textKey', 'signIn.company.descriptionx')
  })

  it('shows the description after the type for accounts, which has no label', () => {
    renderEditor({ ...accounts, description: { textKey: 'signIn.accounts.description' } })

    expect(screen.queryByTestId('studio-field-/label')).not.toBeInTheDocument()
    expect(
      screen
        .getByTestId('studio-field-/type')
        .compareDocumentPosition(screen.getByTestId('studio-field-/description')) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).not.toBe(0)
  })

  it('shows no type fields for accounts', () => {
    renderEditor(accounts)

    expect(screen.queryByRole('textbox', { name: /binding/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: texts('claims.displayName').label })).not.toBeInTheDocument()
    expect(screen.queryByTestId('studio-field-/label')).not.toBeInTheDocument()
  })

  it('keeps an unknown type visible and shows no type fields', () => {
    renderEditor({ ...accounts, type: 'ldap' })

    expect(screen.getByTestId('studio-field-/type')).toHaveTextContent('ldap')
    expect(screen.queryByTestId('studio-field-/label')).not.toBeInTheDocument()
  })

  it('shows the client key and swaps it for the client secret on a choice', async () => {
    const { clientSecret: _secret, ...rest } = oidc
    const onChange = renderEditor({ ...rest, clientKey: { binding: 'CompanyKey' } })

    expect(field('clientKey')).toHaveValue('CompanyKey')
    await userEvent.click(screen.getByRole('radio', { name: texts('clientSecret').label }))

    expect(onChange.mock.calls).toEqual([
      ['/clientKey', undefined],
      ['/clientSecret', { binding: '' }],
    ])
  })

  it('lists only the declared roles in the picker of a role row', async () => {
    renderEditor()

    await userEvent.click(within(screen.getByTestId('studio-role-row-0')).getByRole('combobox'))

    expect(optionTexts()).toEqual(['department-manager', 'finance-reviewer'])
  })

  it('writes the whole values object when a role row changes, is added or is removed', async () => {
    const onChange = renderEditor()

    await userEvent.type(within(screen.getByTestId('studio-role-row-0')).getByRole('textbox'), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/roles/values', {
      managersX: 'department-manager',
      finance: 'finance-reviewer',
    })

    await userEvent.click(screen.getByRole('button', { name: 'Add role' }))
    expect(onChange).toHaveBeenLastCalledWith('/roles/values', {
      managers: 'department-manager',
      finance: 'finance-reviewer',
      value3: '',
    })

    await userEvent.click(within(screen.getByTestId('studio-role-row-0')).getByRole('button', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/roles/values', { finance: 'finance-reviewer' })
  })

  it('removes values, and roles when nothing else is left, with the last role row', async () => {
    const onChange = renderEditor({ ...oidc, roles: { claim: 'groups', values: { managers: 'department-manager' } } })

    await userEvent.click(within(screen.getByTestId('studio-role-row-0')).getByRole('button', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/roles/values', undefined)
  })

  it('removes roles with the last role row when it holds nothing else', async () => {
    const onChange = renderEditor({ ...oidc, roles: { values: { managers: 'department-manager' } }, acr: undefined })

    await userEvent.click(within(screen.getByTestId('studio-role-row-0')).getByRole('button', { name: 'Remove' }))

    expect(onChange).toHaveBeenLastCalledWith('/roles', undefined)
  })

  it('says that a claim value gives the role of the same name when there are no rows', () => {
    renderEditor({ ...oidc, roles: undefined })

    expect(screen.getByTestId('studio-identity-provider-same-name')).toBeVisible()
  })

  it('removes a claim key when its box is cleared, and claims with the last key', async () => {
    const onChange = renderEditor()

    await userEvent.clear(field('claims.displayName'))

    expect(onChange).toHaveBeenLastCalledWith('/claims', undefined)
  })

  it('removes the roles claim key when its box is cleared', async () => {
    const onChange = renderEditor()

    await userEvent.clear(field('roles.claim'))

    expect(onChange).toHaveBeenLastCalledWith('/roles/claim', undefined)
  })

  it('edits acr as rows and removes the key with the last row', async () => {
    const onChange = renderEditor()

    await userEvent.click(screen.getByRole('button', { name: 'Add assurance level' }))
    expect(onChange).toHaveBeenLastCalledWith('/acr', ['urn:level2', ''])

    await userEvent.click(within(screen.getByTestId('studio-field-/acr/0')).getByRole('button', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/acr', undefined)
  })

  it('writes only the pointer it edits, so a key it does not show is passed through', async () => {
    const onChange = renderEditor()

    await userEvent.type(field('name'), 'X')

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenLastCalledWith('/name', 'companyX')
    expect(onChange).not.toHaveBeenCalledWith('/extra', expect.anything())
  })

  it('shows a plain key box for the label until the names load', async () => {
    const onChange = renderEditor(oidc, vi.fn(), undefined)

    await userEvent.type(
      within(screen.getByTestId('studio-field-/label')).getByRole('textbox', { name: 'Text key' }),
      'X',
    )

    expect(onChange).toHaveBeenLastCalledWith('/label/textKey', 'signIn.companyX')
  })
})

describe('draws every field through AxisField with its label and help', () => {
  const { clientSecret: _secret, ...withoutSecret } = oidc
  const { issuer: _issuer, clientId: _clientId, clientSecret: _clientSecret, ...withoutBindings } = oidc
  const saml = { ...withoutBindings, type: 'saml', metadata: { binding: 'CompanyMetadata' } }
  const rolesAndAcr: [string, string, ByRoleMatcher][] = [
    ['name', '/name', 'textbox'],
    ['type', '/type', 'combobox'],
    ['roles.claim', '/roles/claim', 'textbox'],
    ['roles.values.claimValue', 'role-row-0', 'textbox'],
    ['roles.values.role', 'role-row-0', 'combobox'],
    ['acr', '/acr/0', 'textbox'],
  ]
  const signIn: [string, string, ByRoleMatcher][] = [
    ['claims.displayName', '/claims/displayName', 'textbox'],
    ['claims.email', '/claims/email', 'textbox'],
    ['claims.locale', '/claims/locale', 'textbox'],
  ]
  const oidcFields: [string, string, ByRoleMatcher][] = [
    ['issuer', '/issuer', 'textbox'],
    ['clientId', '/clientId', 'textbox'],
    ['clientCredential', '/clientCredential', 'radiogroup'],
  ]
  const variants: { name: string; value: Record<string, unknown>; fields: [string, string, ByRoleMatcher][] }[] = [
    {
      name: 'an oidc provider with a client secret',
      value: oidc,
      fields: [...oidcFields, ['clientSecret', '/clientSecret', 'textbox']],
    },
    {
      name: 'an oidc provider with a client key',
      value: { ...withoutSecret, clientKey: { binding: 'CompanyKey' } },
      fields: [...oidcFields, ['clientKey', '/clientKey', 'textbox']],
    },
    { name: 'a saml provider', value: saml, fields: [['metadata', '/metadata', 'textbox']] },
  ]

  it.each(variants)('finds each field of $name', ({ value, fields }) => {
    renderEditor({ ...value, description: { textKey: 'k' } })

    for (const [path, pointer, role] of [...rolesAndAcr, ...signIn, ...fields]) {
      const scope = pointer.startsWith('role-row') ? `studio-${pointer}` : `studio-field-${pointer}`
      field(path, role, within(screen.getByTestId(scope)))
    }
    for (const [path, pointer] of [
      ['description', '/description'],
      ['label', '/label'],
      ['roles.values', '/roles/values'],
    ]) {
      const { label, help } = texts(path)
      const group = within(screen.getByTestId(`studio-field-${pointer}`)).getAllByRole('group', { name: label })[0]
      expect(group).toHaveAccessibleDescription(help)
    }
  })

  it('has no separate line under a binding, because its help says it all', () => {
    renderEditor()

    expect(screen.queryByText(/Only the name is stored/)).toBeNull()
    expect(screen.queryByText(/URL binding\)|Secret binding\)/)).toBeNull()
    expect(field('issuer')).toHaveAccessibleDescription(/Only the binding's name is stored/)
  })
})

describe('the sections of the identity provider editor', () => {
  const sectionNames = ['general', 'signIn', 'claims', 'roles', 'assurance']

  function shownSections() {
    return [
      ...screen.getByTestId('studio-identity-provider-editor').querySelectorAll('[data-testid^="studio-section-"]'),
    ]
  }

  it('lays an oidc provider out in General, Sign-in, Claims, Roles and Assurance', () => {
    renderEditor()

    expect(shownSections().map((section) => section.getAttribute('data-testid'))).toEqual(
      sectionNames.map((name) => `studio-section-${name}`),
    )
    for (const name of sectionNames) {
      const prefix = `shell.studio.section.identityProvider.${name}`
      const section = within(screen.getByTestId(`studio-section-${name}`))
      expect(
        section.getByRole('heading', { name: platformEnglish[prefix as keyof typeof platformEnglish] }),
      ).toBeVisible()
      expect(section.getByText(platformEnglish[`${prefix}.line` as keyof typeof platformEnglish])).toBeVisible()
    }
    expect(
      within(screen.getByTestId('studio-section-assurance')).getByRole('button', { name: 'Add assurance level' }),
    ).toBeVisible()
    expect(
      within(screen.getByTestId('studio-section-roles')).queryByRole('button', { name: 'Add assurance level' }),
    ).toBeNull()
  })

  it('puts the "Continue with" label in Sign-in after the bindings, and the description in General', () => {
    renderEditor({ ...oidc, description: { textKey: 'k' } })

    const signIn = within(screen.getByTestId('studio-section-signIn'))
    expect(signIn.getByTestId('studio-field-/label')).toBeVisible()
    expect(
      signIn
        .getByTestId('studio-field-/clientSecret')
        .compareDocumentPosition(signIn.getByTestId('studio-field-/label')) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).not.toBe(0)
    expect(within(screen.getByTestId('studio-section-general')).getByTestId('studio-field-/description')).toBeVisible()
  })

  it('shows only General, Roles and Assurance for accounts', () => {
    renderEditor(accounts)

    expect(shownSections().map((section) => section.getAttribute('data-testid'))).toEqual([
      'studio-section-general',
      'studio-section-roles',
      'studio-section-assurance',
    ])
  })

  it('shows Sign-in and Claims for saml', () => {
    renderEditor({ ...accounts, type: 'saml', label: { textKey: 'k' }, metadata: { binding: 'M' } })

    expect(shownSections().map((section) => section.getAttribute('data-testid'))).toEqual(
      sectionNames.map((name) => `studio-section-${name}`),
    )
  })
})

describe('the texts of the identity provider editor', () => {
  const drawnPaths = [
    'name',
    'type',
    'description',
    'issuer',
    'clientId',
    'clientCredential',
    'clientSecret',
    'clientKey',
    'metadata',
    'label',
    'claims.displayName',
    'claims.email',
    'claims.locale',
    'roles.claim',
    'roles.values',
    'roles.values.claimValue',
    'roles.values.role',
    'acr',
  ]

  it.each(drawnPaths)('has a label and a help for %s in English and Vietnamese', (path) => {
    for (const catalogue of [platformEnglish, platformVietnamese] as Record<string, string>[]) {
      expect(catalogue[`shell.studio.field.identityProvider.${path}`]).toBeTruthy()
      expect(catalogue[`shell.studio.field.identityProvider.${path}.help`]).toBeTruthy()
    }
  })

  it.each(['general', 'signIn', 'claims', 'roles', 'assurance'])(
    'has a heading and a line for the %s section in English and Vietnamese',
    (name) => {
      for (const catalogue of [platformEnglish, platformVietnamese] as Record<string, string>[]) {
        expect(catalogue[`shell.studio.section.identityProvider.${name}`]).toBeTruthy()
        expect(catalogue[`shell.studio.section.identityProvider.${name}.line`]).toBeTruthy()
      }
    },
  )
})
