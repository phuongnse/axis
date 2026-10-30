import { Flex, Typography } from 'antd'
import { SiteMark } from './SiteMark'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: the organization's mark and name above a sign-in card, the same on Axis's sign-in screen and on every
 * page of the identity provider. The mark is the first letter of the name on a `primary` tile, like a site's.
 */
export function OrganizationHeading({ name }: { name: string }) {
  const { spacing } = useDesignTokens()
  return (
    <Flex align="center" gap={spacing.xs}>
      <SiteMark title={name} size={32} />
      <Typography.Text strong style={{ fontSize: 20 }}>
        {name}
      </Typography.Text>
    </Flex>
  )
}
