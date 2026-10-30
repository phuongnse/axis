import { Flex, Typography } from 'antd'
import { SiteMark } from './SiteMark'
import { useDesignTokens } from './themeMode'

/** The organization's 32 px mark and name, above the card of a sign-in page. No name means Axis. */
export function OrganizationHeading({ name }: { name?: string }) {
  const { spacing } = useDesignTokens()
  const shown = name || 'Axis'
  return (
    <Flex align="center" gap={spacing.xs} data-testid="organization-heading">
      <SiteMark title={shown} size={32} />
      <Typography.Text strong style={{ fontSize: 20 }}>
        {shown}
      </Typography.Text>
    </Flex>
  )
}
