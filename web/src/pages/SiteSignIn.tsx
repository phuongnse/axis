import { Button, Flex, Typography } from 'antd'
import { AxisButton } from '../platform/AxisButton'
import { SectionPart } from '../platform/SectionParts'
import { useText } from '../platform/texts'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

/**
 * The `signIn` list of a site: rows that each pick one of the application's identity providers by name, in a part
 * headed with the list's title and help. Removing the last row removes the key, and the form then says that the site
 * allows every provider. Its Add provider sits on the right of the part's heading, see
 * docs/reference/studio-resource-form.md#sections.
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
    <SectionPart
      testName="signIn"
      heading={{ title: t('shell.studio.site.signIn'), line: t('shell.studio.site.signIn.help') }}
      action={
        <AxisButton role="secondary" onClick={() => onChange('/signIn', [...entries, ''])}>
          {t('shell.studio.site.addProvider')}
        </AxisButton>
      }
    >
      <div data-testid="studio-field-/signIn">
        <Flex vertical gap="small">
          {entries.map((entry, index) => (
            <Flex key={index} gap="small" align="center" data-testid={`studio-field-/signIn/${index}`}>
              <ReferencePicker
                name={t('shell.studio.site.signIn')}
                value={typeof entry === 'string' ? entry : undefined}
                required
                options={providers}
                onChange={(next) => onChange(`/signIn/${index}`, next ?? '')}
              />
              {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
              <Button
                onClick={() => {
                  const rest = entries.filter((_, position) => position !== index)
                  onChange('/signIn', rest.length > 0 ? rest : undefined)
                }}
              >
                {t('shell.studio.site.removeProvider')}
              </Button>
            </Flex>
          ))}
          {entries.length === 0 && (
            <Typography.Text type="secondary" data-testid="studio-sign-in-all">
              {t('shell.studio.site.allProvidersHint')}
            </Typography.Text>
          )}
        </Flex>
      </div>
    </SectionPart>
  )
}
