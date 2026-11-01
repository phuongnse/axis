import { Alert, Flex, Table, Typography } from 'antd'
import { lazy, Suspense, useMemo } from 'react'
import { useText } from '../platform/texts'
import { type ContractView, contractView } from './contractView'
import { StudioModuleReadOnly } from './StudioModuleReadOnly'
import { useStudioModuleContent } from './useStudioModuleContent'

const StudioContractLifecycle = lazy(() => import('./StudioContractLifecycle'))

function Point({ value }: { value: string }) {
  return <Typography.Text code>{value}</Typography.Text>
}

function Points({ values }: { values: string[] }) {
  return (
    <Flex vertical gap={4}>
      {values.map((value) => (
        <Point key={value} value={value} />
      ))}
    </Flex>
  )
}

function ContractTable<T extends object>({
  testId,
  title,
  rows,
  columns,
}: {
  testId: string
  title: string
  rows: T[]
  columns: { key: string; title: string; render: (row: T) => React.ReactNode }[]
}) {
  if (rows.length === 0) {
    return null
  }
  return (
    <Table<{ key: number; row: T }>
      data-testid={testId}
      size="small"
      pagination={false}
      title={() => <Typography.Text strong>{title}</Typography.Text>}
      dataSource={rows.map((row, key) => ({ key, row }))}
      columns={columns.map((column) => ({
        key: column.key,
        title: column.title,
        render: (_: unknown, entry: { row: T }) => column.render(entry.row),
      }))}
    />
  )
}

/**
 * A contract of a module the application uses, read from the module version the application pins and shown as its
 * lifecycle in a state diagram, then tables of the states, the transitions, the core validations, the effects and the
 * ceiling entries. Each row shows its named point, the `<entity>/<part>/<name>` handle that a relax entry or a contract
 * scenario names. It has no input and no button, because the module owns the file.
 */
export function StudioModuleContract({ application, module, id }: { application: string; module: string; id: string }) {
  const t = useText()
  const { content, loadFailed } = useStudioModuleContent(application, module, id)
  const view: ContractView | undefined = useMemo(() => (content ? contractView(content) : undefined), [content])

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (!view) {
    return null
  }

  const noGuard = t('shell.studio.contract.noGuard')
  return (
    <Flex vertical gap="middle">
      <StudioModuleReadOnly />
      {view.states.length > 0 && (
        <Suspense fallback={null}>
          <StudioContractLifecycle states={view.states} edges={view.edges} />
        </Suspense>
      )}
      <ContractTable
        testId="studio-contract-states"
        title={t('shell.studio.contract.states')}
        rows={view.states}
        columns={[
          { key: 'name', title: t('shell.studio.contract.state'), render: (row) => row.name },
          {
            key: 'marks',
            title: t('shell.studio.contract.lifecycle'),
            render: (row) =>
              [
                row.initial ? t('shell.studio.contract.initial') : '',
                row.deletable ? t('shell.studio.contract.deletable') : '',
              ]
                .filter((mark) => mark !== '')
                .join(', '),
          },
          { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Points values={row.points} /> },
        ]}
      />
      <ContractTable
        testId="studio-contract-transitions"
        title={t('shell.studio.contract.transitions')}
        rows={view.transitions}
        columns={[
          { key: 'name', title: t('shell.studio.contract.name'), render: (row) => row.name },
          { key: 'from', title: t('shell.studio.contract.from'), render: (row) => row.from.join(', ') },
          { key: 'to', title: t('shell.studio.contract.to'), render: (row) => row.to },
          { key: 'guard', title: t('shell.studio.contract.guard'), render: (row) => row.guard ?? noGuard },
          { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Point value={row.point} /> },
        ]}
      />
      <ContractTable
        testId="studio-contract-validations"
        title={t('shell.studio.contract.validations')}
        rows={view.validations}
        columns={[
          { key: 'name', title: t('shell.studio.contract.name'), render: (row) => row.name },
          { key: 'field', title: t('shell.studio.contract.field'), render: (row) => row.field },
          { key: 'expression', title: t('shell.studio.contract.expression'), render: (row) => row.expression },
          { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Point value={row.point} /> },
        ]}
      />
      <ContractTable
        testId="studio-contract-effects"
        title={t('shell.studio.contract.effects')}
        rows={view.effects}
        columns={[
          { key: 'name', title: t('shell.studio.contract.name'), render: (row) => row.name },
          { key: 'on', title: t('shell.studio.contract.on'), render: (row) => row.on },
          { key: 'creates', title: t('shell.studio.contract.creates'), render: (row) => row.entity },
          { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Point value={row.point} /> },
        ]}
      />
      <ContractTable
        testId="studio-contract-ceiling"
        title={t('shell.studio.contract.ceiling')}
        rows={view.ceiling}
        columns={[
          {
            key: 'slot',
            title: t('shell.studio.contract.slot'),
            render: (row) => (row.slot === 'others' ? t('shell.studio.contract.others') : row.slot),
          },
          { key: 'actions', title: t('shell.studio.moduleGrants.actions'), render: (row) => row.actions.join(', ') },
          {
            key: 'fields',
            title: t('shell.studio.moduleGrants.fields'),
            render: (row) =>
              row.fields.length === 0 ? t('shell.studio.moduleGrants.allFields') : row.fields.join(', '),
          },
          { key: 'filter', title: t('shell.studio.moduleGrants.filter'), render: (row) => row.filter },
          { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Point value={row.point} /> },
        ]}
      />
    </Flex>
  )
}
