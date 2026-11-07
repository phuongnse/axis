import { Button, Flex, Tag, Typography } from 'antd'
import { markOf, type LayerElement } from '../platform/layerMarks'
import { useText } from '../platform/texts'

/** What the designer needs to show a customization layer: its marked elements, and Reset to module for each. */
export interface DesignerLayer {
  elements: LayerElement[]
  disabled: boolean
  onReset: (list: string, key: string) => void
}

/**
 * The tag of what the layer did to the element `key` of `list`, with Reset to module, or nothing when the layer left it
 * alone. The tag and the button carry the element's name, so each has a name of its own.
 */
export function LayerMarkTag({
  layer,
  list,
  index,
  name,
}: {
  layer: DesignerLayer
  list: string
  index: number
  name: string
}) {
  const t = useText()
  const mark = markOf(layer.elements, list, name)
  if (mark === undefined || mark === 'removed') {
    return null
  }
  return (
    <Flex gap="small" align="center" wrap>
      <Tag
        role="note"
        aria-label={`${t(`shell.studio.module.${mark}`)} ${name}`}
        data-testid={`studio-layer-mark-${list}/${index}`}
      >
        {t(`shell.studio.module.${mark}`)}
      </Tag>
      <ResetElement layer={layer} list={list} name={name} />
    </Flex>
  )
}

/** The Reset to module button of one element, named after the element. */
function ResetElement({ layer, list, name }: { layer: DesignerLayer; list: string; name: string }) {
  const t = useText()
  return (
    // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
    <Button
      size="small"
      aria-label={`${t('shell.studio.module.resetElement')} ${name}`}
      disabled={layer.disabled}
      onClick={() => layer.onReset(list, name)}
    >
      {t('shell.studio.module.resetElement')}
    </Button>
  )
}

/** The elements of `list` that the layer removed, as muted rows with Reset to module. They are not rows of the draft. */
export function RemovedRows({ layer, list }: { layer: DesignerLayer | undefined; list: string }) {
  const t = useText()
  const removed = (layer?.elements ?? []).filter((element) => element.list === list && element.mark === 'removed')
  if (layer === undefined || removed.length === 0) {
    return null
  }
  return (
    <Flex vertical gap="small">
      {removed.map((element) => (
        <Flex
          key={element.key}
          gap="small"
          align="center"
          wrap
          data-muted
          data-testid={`studio-layer-removed-${list}/${element.key}`}
        >
          <Typography.Text type="secondary">{element.key}</Typography.Text>
          <Tag role="note" aria-label={`${t('shell.studio.module.removed')} ${element.key}`}>
            {t('shell.studio.module.removed')}
          </Tag>
          <ResetElement layer={layer} list={list} name={element.key} />
        </Flex>
      ))}
    </Flex>
  )
}
