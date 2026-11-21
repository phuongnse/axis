import { ConfigProvider, Flex, Typography } from 'antd'
import { AxisButton } from '../platform/AxisButton'
import { RowActions } from '../platform/AxisTable'
import { SectionPart } from '../platform/SectionParts'
import { useText } from '../platform/texts'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

/**
 * The `signIn` list of a site: rows that each pick one of the application's identity providers by name, in a part
 * headed with the list's title and help. Removing the last row removes the key, and the form then says that the site
 * allows every provider. Its Add provider sits on the right of the part's heading, and each row's Remove is in its "…"
 * menu. A read-only editor, Ant Design's `componentDisabled`, shows neither, see
 * docs/reference/studio-editors-by-kind.md#a-sites-sign-in.
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
  const { componentDisabled } = ConfigProvider.useConfig()
  const entries = Array.isArray(value) ? value : []

  return (
    <SectionPart
      testName="signIn"
      heading={{ title: t('shell.studio.site.signIn'), line: t('shell.studio.site.signIn.help') }}
      action={
        componentDisabled ? undefined : (
          <AxisButton role="secondary" onClick={() => onChange('/signIn', [...entries, ''])}>
            {t('shell.studio.site.addProvider')}
          </AxisButton>
        )
      }
    >
      <div data-testid="studio-field-/signIn">
        <Flex vertical gap="small">
          {entries.map((entry, index) => (
            <Flex key={index} gap="small" align="flex-start" data-testid={`studio-field-/signIn/${index}`}>
              <div style={{ flex: 1 }}>
                <ReferencePicker
                  name={t('shell.studio.site.signIn')}
                  value={typeof entry === 'string' ? entry : undefined}
                  required
                  options={providers}
                  onChange={(next) => onChange(`/signIn/${index}`, next ?? '')}
                />
              </div>
              {componentDisabled ? null : (
                <RowActions
                  fold
                  actions={[
                    {
                      key: 'remove',
                      labelKey: 'shell.studio.site.removeProvider',
                      destructive: true,
                      onClick: () => {
                        const rest = entries.filter((_, position) => position !== index)
                        onChange('/signIn', rest.length > 0 ? rest : undefined)
                      },
                    },
                  ]}
                />
              )}
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
