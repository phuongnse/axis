import { Alert, Button, Card, Flex } from 'antd'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { boundParameterQuery } from './boundParameters'
import { useCanvasActions } from './canvasActions'
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
  /** The values of the page's parameters from its URL, by page parameter name. The widget's `parameters` and a navigate action read these. */
  pageValues?: Record<string, string>
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The request URL, so a result for an older widget or older parameter values shows as loading. */
  path: string
  /** The data source's total row, or `null` when it returned none. */
  row: RecordItem | null
  failed: boolean
}

/**
 * Shows the one total row of a kpi widget's data source as a tile: `<label>: <value>` and, with a
 * comparison, `<comparison label>: <comparison value>`. A value looks as it does in a table cell, the
 * SPA computes no difference or percentage, and a `null` measure shows the platform text for an empty
 * value. The tile asks for the row with the value of each page parameter the widget binds, under the data
 * source parameter's name, and asks again when one changes. The compiler makes every parameter that the
 * widget leaves unbound optional. With a navigate action the whole tile is a link to the
 * target page, ending in the action's label, and the action reads only the page's parameter values.
 * Without one, or when the SPA cannot build the address, the tile is plain. On the page designer's canvas the tile is
 * never a link: the navigate action is a link-style button on the label line, also when the SPA cannot build the address.
 */
export function KpiWidget({ sitePath, widget, locale, pageValues }: KpiWidgetProps) {
  const t = useText()
  const canvas = useCanvasActions()
  const { dataSource, kpi } = widget
  // The server sets both on a kpi widget.
  const { name, rowsPath, columns } = dataSource!
  const query = boundParameterQuery(widget, pageValues).toString()
  const requestUrl = query ? `${rowsPath}?${query}` : rowsPath
  const [state, setState] = useState<LoadState>()
  const loading = state?.path !== requestUrl
  const failed = !loading && state.failed

  useEffect(() => {
    const controller = new AbortController()
    fetchRecords(rowsPath, query, controller.signal)
      .then((page) => setState({ path: requestUrl, row: page.items[0] ?? null, failed: false }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Loading the total of '${name}' failed`, error)
        setState({ path: requestUrl, row: null, failed: true })
      })
    return () => controller.abort()
  }, [rowsPath, query, requestUrl, name])

  const row = loading ? null : state.row
  const display = (measure: string): string => {
    const value = row?.values[measure] as RecordValue | undefined
    if (value === null || value === undefined) {
      return t('shell.kpi.empty')
    }
    const column = columns.find((candidate) => candidate.name === measure)
    return column ? formatValue(column, value, row!.labels, { locale, text: t }, row!.values) : String(value)
  }

  const { measure, labelKey, comparison, navigate } = kpi!
  // A tile that failed to load has nothing to open from.
  const path = !failed && navigate && navigatePath(sitePath, navigate, { id: null, values: pageValues ?? {} })

  let content
  if (loading) {
    content = t('shell.kpi.loading')
  } else if (failed) {
    content = <Alert data-testid="kpi-error" type="error" title={t('shell.kpi.loadFailed')} />
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
        {navigate && canvas.active && (
          <div>
            <Button type="link" size="small" {...canvas.props('navigate')}>
              {t(navigate.labelKey)}
            </Button>
          </div>
        )}
        {navigate && !canvas.active && path && <div>{t(navigate.labelKey)}</div>}
      </Flex>
    )
  }

  const tile = (
    <Card data-testid="kpi-tile" aria-busy={loading} hoverable={Boolean(path)}>
      {content}
    </Card>
  )
  return path && !canvas.active ? <Link to={path}>{tile}</Link> : tile
}
