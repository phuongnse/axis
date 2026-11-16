import type { ScopeOption } from './expressionScope'

// Proposed: starting points for an empty expression, by the type the place needs (#3265, step 2). The build has the
// server answer them with the scope of the place, see the rule comment on #3265.

/** A whole expression to start from, and the part of it to select once it is put in the box, the first value to change. */
export interface ExpressionStart {
  text: string
  select: { from: number; to: number } | null
}

/** Marks the part to select in a template. */
const mark = '\u0001'

function start(template: string): ExpressionStart {
  const from = template.indexOf(mark)
  if (from < 0) {
    return { text: template, select: null }
  }
  const to = template.indexOf(mark, from + 1)
  return { text: template.replaceAll(mark, ''), select: { from, to: to - 1 } }
}

function fieldsOf(options: ScopeOption[], ...types: string[]): ScopeOption[] {
  return options.filter(
    (option) => (option.type === 'field' || option.type === 'parameter') && types.includes(option.valueType ?? ''),
  )
}

/**
 * The starting points for a place that needs `type`, each made from the fields in scope that fit it, the most
 * common first: for yes or no, a choice, a range, a date against today and a value that must be there; for a number,
 * a sum, a rounding and a fallback; for a text, two texts put together; for a date, a date some days on. A template
 * whose fields are not in scope is left out, a start that is only a value is never offered, and a place that takes any
 * type has none.
 */
export function expressionStarts(type: string | null | undefined, options: ScopeOption[]): ExpressionStart[] {
  const enums = fieldsOf(options, 'enum').filter((field) => (field.values?.length ?? 0) > 0)
  const numbers = fieldsOf(options, 'integer', 'decimal')
  const dates = fieldsOf(options, 'date', 'date-time')
  const texts = fieldsOf(options, 'text')
  const booleans = fieldsOf(options, 'boolean')
  const any = [...texts, ...dates, ...fieldsOf(options, 'reference'), ...numbers]
  const templates: (string | null)[] = []
  switch (type) {
    case 'boolean':
      templates.push(
        enums[0] ? `${enums[0].label} == '${mark}${enums[0].values![0]}${mark}'` : null,
        numbers[0] ? `${numbers[0].label} >= ${mark}0${mark} and ${numbers[0].label} <= 100` : null,
        dates[0] ? `${dates[0].label} >= ${mark}today()${mark}` : null,
        any[0] ? `${any[0].label} is not null` : null,
        booleans[0] ? booleans[0].label : null,
      )
      break
    case 'integer':
    case 'decimal':
      templates.push(
        numbers[1] ? `${numbers[0].label} + ${numbers[1].label}` : null,
        numbers[0] ? `${numbers[0].label} * ${mark}1${mark}` : null,
        numbers[0] && type === 'decimal' ? `round(${numbers[0].label}, ${mark}2${mark})` : null,
        numbers[0] ? `coalesce(${numbers[0].label}, ${mark}0${mark})` : null,
      )
      break
    case 'text':
      templates.push(
        texts[1] ? `concat(${texts[0].label}, '${mark} ${mark}', ${texts[1].label})` : null,
        texts[0] ? `upper(${texts[0].label})` : null,
        texts[0] ? `coalesce(${texts[0].label}, '${mark}${mark}')` : null,
      )
      break
    case 'date':
      templates.push(
        'today()',
        `addDays(today(), ${mark}7${mark})`,
        dates[0] ? `addDays(${dates[0].label}, ${mark}7${mark})` : null,
      )
      break
    case 'date-time':
      templates.push('now()')
      break
  }
  return templates
    .filter((template): template is string => template !== null)
    .slice(0, 4)
    .map(start)
}
