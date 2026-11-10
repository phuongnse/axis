import { render } from '@testing-library/react'
import { Space } from 'antd'
import { expect, it } from 'vitest'
import { takeAntdDeprecations } from './antdDeprecations'

it('records the Ant Design warning for a deprecated prop', () => {
  render(<Space direction="vertical" />)

  const warnings = takeAntdDeprecations()

  expect(warnings).toHaveLength(1)
  expect(warnings[0]).toContain('[antd: Space]')
})
