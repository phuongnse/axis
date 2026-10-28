import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
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

async function chooseType(type: string) {
  await userEvent.click(screen.getByRole('combobox', { name: 'Type' }))
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
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('company')
    expect(screen.getByRole('textbox', { name: 'Issuer (URL binding)' })).toHaveValue('CompanyIssuer')
    expect(screen.getByRole('textbox', { name: 'Client id (Secret binding)' })).toHaveValue('CompanyClientId')
    expect(screen.getByRole('textbox', { name: 'Client secret (Secret binding)' })).toHaveValue('CompanyClientSecret')
    expect(screen.getByRole('textbox', { name: 'Display name claim' })).toHaveValue('name')
    expect(screen.getByRole('textbox', { name: 'Email claim' })).toHaveAttribute('placeholder', 'email')
  })
})

describe('IdentityProviderEditor', () => {
  it('shows each binding as a name box and never a value, whatever else the file holds', () => {
    renderEditor({ ...oidc, issuer: { binding: 'CompanyIssuer', value: 'https://secret.example' } })

    expect(screen.queryByDisplayValue('https://secret.example')).not.toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Issuer (URL binding)' })).toHaveValue('CompanyIssuer')
  })

  it('edits a binding by its name at the binding pointer', async () => {
    const onChange = renderEditor()

    await userEvent.type(screen.getByRole('textbox', { name: 'Issuer (URL binding)' }), 'X')

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

  it('shows no type fields for accounts', () => {
    renderEditor(accounts)

    expect(screen.queryByRole('textbox', { name: /binding/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'Display name claim' })).not.toBeInTheDocument()
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

    expect(screen.getByRole('textbox', { name: 'Client key (Secret binding)' })).toHaveValue('CompanyKey')
    await userEvent.click(screen.getByRole('radio', { name: 'Client secret' }))

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

    await userEvent.clear(screen.getByRole('textbox', { name: 'Display name claim' }))

    expect(onChange).toHaveBeenLastCalledWith('/claims', undefined)
  })

  it('removes the roles claim key when its box is cleared', async () => {
    const onChange = renderEditor()

    await userEvent.clear(screen.getByRole('textbox', { name: 'Roles claim' }))

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

    await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'X')

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenLastCalledWith('/name', 'companyX')
    expect(onChange).not.toHaveBeenCalledWith('/extra', expect.anything())
  })

  it('shows a plain key box for the label until the names load', async () => {
    const onChange = renderEditor(oidc, vi.fn(), undefined)

    await userEvent.type(screen.getByRole('textbox', { name: 'Text key' }), 'X')

    expect(onChange).toHaveBeenLastCalledWith('/label/textKey', 'signIn.companyX')
  })
})
