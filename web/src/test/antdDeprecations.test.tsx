import { render } from '@testing-library/react'
import { Alert } from 'antd'
import { expect, it } from 'vitest'
import { takeAntdDeprecations } from './antdDeprecations'

it('records the Ant Design warning for a deprecated prop', () => {
  render(<Alert message="x" />)

  const warnings = takeAntdDeprecations()

  expect(warnings).toHaveLength(1)
  expect(warnings[0]).toContain('[antd: Alert]')
})
