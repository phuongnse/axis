import { Flex, Segmented } from 'antd'
import { useMemo, useState } from 'react'
import { CodeBlock } from './CodeBlock'
import { useText } from './texts'
import { ValueTable } from './ValueTable'
import { JsonNumber } from './valueRows'

/** The reviver's third argument, which a browser with source text access for `JSON.parse` gives. */
type NumberContext = { source?: string }

/**
 * The body parsed, with each number keeping its source digits where the browser gives them, or null when it is not an
 * object or a list in JSON. A scalar has no names, so it is not a table.
 */
function parsedBody(body: string): { value: object; pretty: string } | null {
  try {
    const value: unknown = JSON.parse(body, ((_key: string, member: unknown, context?: NumberContext) =>
      typeof member === 'number' && context?.source !== undefined
        ? new JsonNumber(context.source)
        : member) as Parameters<typeof JSON.parse>[1])
    if (value === null || typeof value !== 'object' || value instanceof JsonNumber) {
      return null
    }
    return { value, pretty: JSON.stringify(JSON.parse(body), null, 2) }
  } catch {
    return null
  }
}

type Shown = 'table' | 'json'

/**
 * A body that may be JSON. One that is an object or a list shows as a {@link ValueTable} by default, with a Table and
 * JSON switch. Other text shows as it is in a {@link CodeBlock}, see docs/reference/actions-and-controls.md#text. Shared
 * widgets are in docs/reference/shared-widgets.md#which-widget.
 */
export function BodyView({ body, testId }: { body: string; testId?: string }) {
  const t = useText()
  const [shown, setShown] = useState<Shown>('table')
  const parsed = useMemo(() => parsedBody(body), [body])
  if (!parsed) {
    return <CodeBlock testId={testId} text={body} />
  }
  return (
    <Flex vertical gap="small" data-testid={testId}>
      <Segmented<Shown>
        aria-label={t('shell.value.showAs')}
        value={shown}
        onChange={setShown}
        options={[
          { value: 'table', label: t('shell.value.table') },
          { value: 'json', label: t('shell.value.json') },
        ]}
      />
      {shown === 'table' ? <ValueTable value={parsed.value} /> : <CodeBlock text={parsed.pretty} />}
    </Flex>
  )
}
