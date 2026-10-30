import { Alert, Descriptions, Flex, Table } from 'antd'
import { useMemo } from 'react'
import { useText } from '../platform/texts'
import { StudioModuleReadOnly } from './StudioModuleReadOnly'
import { useStudioModuleContent } from './useStudioModuleContent'

const targetKinds = ['entity', 'dataSource', 'process', 'endpoint'] as const

interface GrantRow {
  key: number
  target: string
  actions: string
  filter: string
  fields: string[]
}

function strings(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
}

function grantRows(content: Record<string, unknown>): GrantRow[] {
  const grants: unknown[] = Array.isArray(content.grants) ? content.grants : []
  return grants.map((grant, index) => {
    const entry = (typeof grant === 'object' && grant !== null ? grant : {}) as Record<string, unknown>
    const kind = targetKinds.find((candidate) => typeof entry[candidate] === 'string')
    return {
      key: index,
      target: kind ? `${kind} ${entry[kind] as string}` : '',
      actions: strings(entry.actions).join(', '),
      filter: typeof entry.filter === 'string' ? entry.filter : '',
      fields: strings(entry.fields),
    }
  })
}

/**
 * A policy of a module the application uses, read from the module version the application pins and shown as its role
 * slots and a table of its grants: the target, the actions, the filter and the fields. It has no input and no button,
 * because the module owns the file.
 */
export function StudioModulePolicy({ application, module, id }: { application: string; module: string; id: string }) {
  const t = useText()
  const { content, loadFailed } = useStudioModuleContent(application, module, id)
  const rows = useMemo(() => (content ? grantRows(content) : []), [content])

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (!content) {
    return null
  }

  return (
    <Flex vertical gap="middle">
      <StudioModuleReadOnly />
      <Descriptions
        column={1}
        items={[
          {
            key: 'roles',
            label: t('shell.studio.moduleRoles'),
            children: strings(content.roles).join(', ') || t('shell.studio.moduleGrants.none'),
          },
        ]}
      />
      <Table<GrantRow>
        data-testid="studio-module-policy-grants"
        size="small"
        pagination={false}
        dataSource={rows}
        columns={[
          { key: 'target', title: t('shell.studio.moduleGrants.target'), dataIndex: 'target' },
          { key: 'actions', title: t('shell.studio.moduleGrants.actions'), dataIndex: 'actions' },
          { key: 'filter', title: t('shell.studio.moduleGrants.filter'), dataIndex: 'filter' },
          {
            key: 'fields',
            title: t('shell.studio.moduleGrants.fields'),
            dataIndex: 'fields',
            render: (fields: string[]) =>
              fields.length === 0 ? t('shell.studio.moduleGrants.allFields') : fields.join(', '),
          },
        ]}
      />
    </Flex>
  )
}
