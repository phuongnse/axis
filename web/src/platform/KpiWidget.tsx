import { Alert, Card, Flex } from 'antd'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { formatValue } from './formatValue'
import { navigatePath } from './navigateAction'
import { fetchRecords, type RecordItem, type RecordValue } from './records'
import type { WidgetMetadata } from './site'
import { useText } from './texts'

interface KpiWidgetProps {
  sitePath: string
  /** A kpi widget: it has a data source and its `kpi` metadata. */
  widget: WidgetMetadata
  /** The UI locale, for formatting values. */
  locale: string
  /** The values of the page's parameters from its URL, by page parameter name. A navigate action reads only these. */
  pageValues?: Record<string, string>
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The request path, so a result for an older widget shows as loading. */
  path: string
  /** The data source's total row, or `null` when it returned none. */
  row: RecordItem | null
  failed: boolean
}

/**
 * Shows the one total row of a kpi widget's data source as a tile: `<label>: <value>` and, with a
 * comparison, `<comparison label>: <comparison value>`. A value looks as it does in a table cell, the
 * SPA computes no difference or percentage, and a `null` measure shows the platform text for an empty
 * value. The tile asks for the row once, with no query parameters, as the compiler makes every
 * parameter of a kpi's data source optional. With a navigate action the whole tile is a link to the
 * target page, ending in the action's label, and the action reads only the page's parameter values.
 * Without one, or when the SPA cannot build the address, the tile is plain.
 */
export function KpiWidget({ sitePath, widget, locale, pageValues }: KpiWidgetProps) {
  const t = useText()
  const { dataSource, kpi } = widget
  // The server sets both on a kpi widget.
  const { name, rowsPath, columns } = dataSource!
  const [state, setState] = useState<LoadState>()
  const loading = state?.path !== rowsPath
  const failed = !loading && state.failed

  useEffect(() => {
    const controller = new AbortController()
    fetchRecords(rowsPath, '', controller.signal)
      .then((page) => setState({ path: rowsPath, row: page.items[0] ?? null, failed: false }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Loading the total of '${name}' failed`, error)
        setState({ path: rowsPath, row: null, failed: true })
      })
    return () => controller.abort()
  }, [rowsPath, name])

  const row = loading ? null : state.row
  const display = (measure: string): string => {
    const value = row?.values[measure] as RecordValue | undefined
    if (value === null || value === undefined) {
      return t('shell.kpi.empty')
    }
    const column = columns.find((candidate) => candidate.name === measure)
    return column ? formatValue(column, value, row!.labels, { locale, text: t }) : String(value)
  }

  const { measure, labelKey, comparison, navigate } = kpi!
  // A tile that failed to load has nothing to open from.
  const path = !failed && navigate && navigatePath(sitePath, navigate, { id: null, values: pageValues ?? {} })

  let content
  if (loading) {
    content = t('shell.kpi.loading')
  } else if (failed) {
    content = <Alert data-testid="kpi-error" type="error" message={t('shell.kpi.loadFailed')} />
  } else {
    content = (
      <Flex vertical gap="small">
        <div data-testid="kpi-value">
          {t(labelKey)}: <strong>{display(measure)}</strong>
        </div>
        {comparison && (
          <div data-testid="kpi-comparison">
            {t(comparison.labelKey)}: <strong>{display(comparison.measure)}</strong>
          </div>
        )}
        {navigate && path && <div>{t(navigate.labelKey)}</div>}
      </Flex>
    )
  }

  const tile = (
    <Card data-testid="kpi-tile" aria-busy={loading} hoverable={Boolean(path)}>
      {content}
    </Card>
  )
  return path ? <Link to={path}>{tile}</Link> : tile
}
