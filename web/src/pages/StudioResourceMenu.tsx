import { EllipsisOutlined } from '@ant-design/icons'
import { Dropdown } from 'antd'
import { AxisButton } from '../platform/AxisButton'
import { useText } from '../platform/texts'

/**
 * The "…" menu at the right of a resource's heading. Its one entry, Delete…, is drawn in the danger colour and is off
 * while `disabled`, see studio.md#delete.
 */
export function StudioResourceMenu({ disabled, onDelete }: { disabled: boolean; onDelete: () => void }) {
  const t = useText()
  return (
    <Dropdown
      trigger={['click']}
      autoFocus
      menu={{
        items: [{ key: 'delete', label: t('shell.studio.delete.button'), danger: true, disabled, onClick: onDelete }],
        'aria-label': t('shell.studio.resourceActions'),
      }}
    >
      <AxisButton role="icon" nameKey="shell.studio.resourceActions" icon={<EllipsisOutlined />} />
    </Dropdown>
  )
}
