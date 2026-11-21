import { describe, expect, it } from 'vitest'
import { designTokensFor } from './designTokens'
import { workspaceTabRow } from './workspaceTabRow'

describe('workspaceTabRow', () => {
  it('gives the workspace look with no options', () => {
    const tokens = designTokensFor('light')

    expect(workspaceTabRow(tokens)).toEqual({
      size: 'small',
      tabBarStyle: {
        boxSizing: 'border-box',
        height: 40,
        margin: 0,
        paddingInline: 16,
        borderBottom: `1px solid ${tokens.colour.borderSubtle}`,
      },
    })
  })

  it('reaches past the page padding by the bleed and sits spacing-sm into the gap above', () => {
    const tokens = designTokensFor('light')
    const { tabBarStyle } = workspaceTabRow(tokens, { bleed: 16 })

    expect(tabBarStyle).toEqual({
      boxSizing: 'border-box',
      height: 40,
      margin: '-12px -16px 16px',
      padding: '0 16px',
      borderBottom: `1px solid ${tokens.colour.borderSubtle}`,
    })
    expect(tabBarStyle).not.toHaveProperty('paddingInline')
  })
})
