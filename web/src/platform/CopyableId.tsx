import { CheckOutlined, CopyOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import { AxisButton } from './AxisButton'
import { useCopy } from './useCopy'

const shownLength = 8

/**
 * An id as its first 8 characters in the code font, with a copy button that copies the whole id and says Copied,
 * see docs/reference/frontend.md#shown-values.
 */
export function CopyableId({ id }: { id: string }) {
  const { copied, copy } = useCopy()
  return (
    <Flex component="span" align="center" gap="small" style={{ display: 'inline-flex' }}>
      <Typography.Text code>{id.slice(0, shownLength)}</Typography.Text>
      <AxisButton
        role="icon"
        nameKey="shell.copy.copy"
        doneKey={copied ? 'shell.copy.copied' : undefined}
        icon={copied ? <CheckOutlined /> : <CopyOutlined />}
        onClick={() => void copy(id)}
      />
    </Flex>
  )
}
