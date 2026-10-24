import type { WidgetMetadata } from './site'

/**
 * The query that gives the data source parameters a widget binds with `parameters` the values of the page
 * parameters. It lists each bound parameter in declaration order under the data source parameter's name,
 * and leaves out one whose page parameter has no value.
 */
export function boundParameterQuery(
  widget: WidgetMetadata,
  pageValues: Record<string, string> | undefined,
): URLSearchParams {
  const query = new URLSearchParams()
  for (const parameter of widget.dataSource?.parameters ?? []) {
    if (!Object.hasOwn(widget.parameters, parameter.name)) {
      continue
    }
    const value = pageValues?.[widget.parameters[parameter.name]]
    if (value) {
      query.append(parameter.name, value)
    }
  }
  return query
}
