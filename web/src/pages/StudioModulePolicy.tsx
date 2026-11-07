import { Button, Checkbox, Descriptions, Flex, Tag, Typography } from 'antd'
import { useText } from '../platform/texts'
import { LayerMarkTag, RemovedRows, type DesignerLayer } from './LayerMarkViews'
import { targetActions } from './policyMatrixDraft'

const targetKinds = ['entity', 'dataSource', 'process', 'endpoint'] as const

type Grant = Record<string, unknown>

function strings(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
}

function asGrant(value: unknown): Grant {
  return typeof value === 'object' && value !== null ? (value as Grant) : {}
}

/**
 * The editor of a policy of a module the application uses, with the application's layer applied: its role slots and its
 * grants, each with the target, a checkbox for each action the target takes, the filter and the fields. An author ticks
 * actions and removes grants, and the filter and the fields are read-only. A grant the layer added, changed or removed
 * carries a mark with Reset to module. The parent owns the draft and the save.
 */
export function StudioModulePolicy({
  value,
  onChange,
  layer,
}: {
  value: Record<string, unknown>
  onChange: (pointer: string, next: unknown) => void
  layer?: DesignerLayer
}) {
  const t = useText()
  const grants: unknown[] = Array.isArray(value.grants) ? value.grants : []

  return (
    <Flex vertical gap="middle">
      <Descriptions
        column={1}
        items={[
          {
            key: 'roles',
            label: t('shell.studio.moduleRoles'),
            children: strings(value.roles).join(', ') || t('shell.studio.moduleGrants.none'),
          },
        ]}
      />
      <Flex vertical gap="small" data-testid="studio-module-policy-grants">
        {grants.map((item, index) => {
          const grant = asGrant(item)
          const kind = targetKinds.find((candidate) => typeof grant[candidate] === 'string')
          const accepted = kind ? targetActions(kind) : []
          const actions = strings(grant.actions)
          const unaccepted = actions.filter((action) => !accepted.includes(action))
          const fields = strings(grant.fields)
          return (
            <Flex key={index} vertical gap={4} data-testid={`studio-module-policy-grant-${index}`}>
              <Flex gap="small" align="center" wrap>
                {typeof grant.name === 'string' && <Typography.Text code>{grant.name}</Typography.Text>}
                <span>{kind ? `${kind} ${grant[kind] as string}` : ''}</span>
                {layer !== undefined && typeof grant.name === 'string' && (
                  <LayerMarkTag layer={layer} list="/grants" index={index} name={grant.name} />
                )}
              </Flex>
              <Flex gap="small" align="center" wrap>
                <Checkbox.Group
                  options={accepted}
                  value={actions.filter((action) => accepted.includes(action))}
                  onChange={(ticked) =>
                    onChange(`/grants/${index}/actions`, [
                      ...accepted.filter((action) => ticked.includes(action)),
                      ...unaccepted,
                    ])
                  }
                />
                {unaccepted.map((action) => (
                  // oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token.
                  <Tag key={action} color="warning" style={{ marginInlineEnd: 0 }}>
                    {t('shell.studio.matrix.unacceptedAction')} {action}
                  </Tag>
                ))}
                {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                <Button
                  size="small"
                  disabled={grants.length === 1}
                  onClick={() =>
                    onChange(
                      '/grants',
                      grants.filter((_, position) => position !== index),
                    )
                  }
                >
                  {t('shell.studio.matrix.removeGrant')}
                </Button>
              </Flex>
              <Flex gap="small" align="center" wrap>
                {typeof grant.filter === 'string' && grant.filter !== '' ? (
                  <Typography.Text code>{grant.filter}</Typography.Text>
                ) : (
                  <Typography.Text type="secondary">{t('shell.studio.matrix.noFilter')}</Typography.Text>
                )}
                {fields.length === 0 ? (
                  <Typography.Text type="secondary">{t('shell.studio.matrix.allFields')}</Typography.Text>
                ) : (
                  fields.map((field) => <Tag key={field}>{field}</Tag>)
                )}
              </Flex>
            </Flex>
          )
        })}
      </Flex>
      <RemovedRows layer={layer} list="/grants" />
    </Flex>
  )
}
