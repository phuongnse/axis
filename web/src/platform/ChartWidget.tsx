import { Alert, Card } from 'antd'
import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { Chart } from './Chart'
import { formatValue, type FormatOptions } from './formatValue'
import { fetchRecords, type RecordItem, type RecordValue } from './records'
import type { DataSourceColumn, WidgetMetadata } from './site'
import { useText } from './texts'

interface ChartWidgetProps {
  /** A chart widget: it has a data source and its `chart` metadata. */
  widget: WidgetMetadata
  /** The UI locale, for formatting values. */
  locale: string
}

/** The most groups a chart draws. The data source rows endpoint allows no larger page. */
const maxGroups = 100

/** The outcome of the last finished request. */
interface LoadState {
  /** The request path, so a result for an older widget shows as loading. */
  path: string
  /** The groups, in the order the data source sorts them. */
  rows: RecordItem[]
  /** How many groups the data source has, which can be more than `rows`. */
  totalCount: number
  failed: boolean
}

/** Hides the table from the eye and keeps it in the accessibility tree. It has no colour of its own. */
const visuallyHidden: CSSProperties = {
  position: 'absolute',
  width: 1,
  height: 1,
  margin: -1,
  padding: 0,
  border: 0,
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
}

/** A value as a table cell shows it, or an empty text for a null value. */
function cellText(
  column: DataSourceColumn | undefined,
  item: RecordItem,
  field: string,
  options: FormatOptions,
): string {
  const value = item.values[field] as RecordValue | undefined
  if (value === null || value === undefined) {
    return ''
  }
  return column ? formatValue(column, value, item.labels, options) : String(value)
}

/**
 * Draws the groups of a chart widget's grouped data source as a bar, line or pie chart. The category is the
 * group field and each measure is one series, labelled from its text key. The chart asks for the groups once,
 * with no query parameters beyond the page size, as the compiler makes every parameter of a chart's data
 * source optional. It draws at most 100 groups and says so when the data source has more. A null category
 * shows the platform text for an empty value. The chart's data is also in a table that only screen readers
 * reach, with the category and each measure as columns, and its values look as they do in a table cell.
 */
export function ChartWidget({ widget, locale }: ChartWidgetProps) {
  const t = useText()
  // The server sets both on a chart widget.
  const { name, rowsPath, columns } = widget.dataSource!
  const { kind, titleKey, category, measures } = widget.chart!
  const [state, setState] = useState<LoadState>()
  const loading = state?.path !== rowsPath
  const failed = !loading && state.failed

  useEffect(() => {
    const controller = new AbortController()
    fetchRecords(rowsPath, `pageSize=${maxGroups}`, controller.signal)
      .then((page) => setState({ path: rowsPath, rows: page.items, totalCount: page.totalCount, failed: false }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Loading the groups of '${name}' failed`, error)
        setState({ path: rowsPath, rows: [], totalCount: 0, failed: true })
      })
    return () => controller.abort()
  }, [rowsPath, name])

  const title = t(titleKey)
  const rows = useMemo(() => (loading ? [] : state.rows), [loading, state])
  const categoryColumn = columns.find((candidate) => candidate.name === category)

  const categories = useMemo(
    () =>
      rows.map((item) =>
        item.values[category] === null
          ? t('shell.chart.noValue')
          : cellText(categoryColumn, item, category, { locale, text: t }),
      ),
    [rows, category, categoryColumn, locale, t],
  )
  const series = useMemo(
    () =>
      measures.map((measure) => ({
        label: t(measure.labelKey),
        values: rows.map((item) => {
          const value = item.values[measure.name]
          return value === null || value === undefined ? null : Number(value)
        }),
      })),
    [rows, measures, t],
  )

  let content
  if (loading) {
    content = t('shell.chart.loading')
  } else if (failed) {
    content = <Alert data-testid="chart-error" type="error" title={t('shell.chart.loadFailed')} />
  } else if (rows.length === 0) {
    content = <div data-testid="chart-empty">{t('shell.chart.empty')}</div>
  } else {
    content = (
      <>
        <Chart title={title} kind={kind} categories={categories} series={series} />
        {state.totalCount > maxGroups && (
          <Alert data-testid="chart-truncated" type="info" title={t('shell.chart.truncated')} />
        )}
        <table style={visuallyHidden}>
          <caption>{title}</caption>
          <thead>
            <tr>
              <th scope="col">{categoryColumn?.labelKey ? t(categoryColumn.labelKey) : category}</th>
              {measures.map((measure) => (
                <th key={measure.name} scope="col">
                  {t(measure.labelKey)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((item, index) => (
              <tr key={index}>
                <th scope="row">{categories[index]}</th>
                {measures.map((measure) => (
                  <td key={measure.name}>
                    {cellText(
                      columns.find((candidate) => candidate.name === measure.name),
                      item,
                      measure.name,
                      { locale, text: t },
                    )}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </>
    )
  }

  return (
    <Card data-testid="chart-widget" title={title} aria-busy={loading}>
      {content}
    </Card>
  )
}
