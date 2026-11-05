import { Alert, Card, Descriptions } from 'antd'
import { useEffect, useState } from 'react'
import { fetchCell, type Cell } from '../platform/cell'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { useCellConsole } from './cellConsoleContext'

/** The Cell view of the cell console: the cell's id and region, and its number of tenants against the cap. It is read only. */
export function CellPage() {
  const t = useText()
  const { onRefused } = useCellConsole()
  const [cell, setCell] = useState<Cell>()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchCell(controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setCell(result.value)
        } else {
          onRefused(result.kind)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell failed', error)
          setFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [onRefused])

  return (
    <PageLayout title={t('shell.cell.overview.title')}>
      {failed && <Alert data-testid="cell-error" type="error" title={t('shell.cell.loadFailed')} />}
      {cell && (
        <Card data-testid="cell-overview">
          <Descriptions
            column={1}
            items={[
              { key: 'id', label: t('shell.cell.overview.id'), children: cell.id },
              { key: 'region', label: t('shell.cell.overview.region'), children: cell.region },
              {
                key: 'tenants',
                label: t('shell.cell.overview.tenants'),
                children:
                  cell.cap === null
                    ? cell.tenants
                    : t('shell.cell.overview.tenantsOfCap')
                        .replace('{tenants}', String(cell.tenants))
                        .replace('{cap}', String(cell.cap)),
              },
            ]}
          />
        </Card>
      )}
    </PageLayout>
  )
}
