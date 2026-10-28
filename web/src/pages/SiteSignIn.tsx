import { Button, Flex, Typography } from 'antd'
import { useText } from '../platform/texts'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

/**
 * The `signIn` list of a site: rows that each pick one of the application's identity providers by name. Removing the
 * last row removes the key, and the form then says that the site allows every provider.
 */
export function SiteSignIn({
  providers,
  value,
  onChange,
}: {
  providers: string[]
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const entries = Array.isArray(value) ? value : []

  return (
    <div data-testid="studio-field-/signIn">
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.site.signIn')}</Typography.Text>
        {entries.map((entry, index) => (
          <Flex key={index} gap="small" align="center" data-testid={`studio-field-/signIn/${index}`}>
            <ReferencePicker
              name={t('shell.studio.site.signIn')}
              value={typeof entry === 'string' ? entry : undefined}
              required
              options={providers}
              onChange={(next) => onChange(`/signIn/${index}`, next ?? '')}
            />
            <Button
              onClick={() => {
                const rest = entries.filter((_, position) => position !== index)
                onChange('/signIn', rest.length > 0 ? rest : undefined)
              }}
            >
              {t('shell.studio.identityProvider.remove')}
            </Button>
          </Flex>
        ))}
        <div>
          <Button onClick={() => onChange('/signIn', [...entries, ''])}>{t('shell.studio.site.addProvider')}</Button>
        </div>
        {entries.length === 0 && (
          <Typography.Text type="secondary" data-testid="studio-sign-in-all">
            {t('shell.studio.site.allProvidersHint')}
          </Typography.Text>
        )}
      </Flex>
    </div>
  )
}
