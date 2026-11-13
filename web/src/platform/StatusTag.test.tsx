import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { requestStateTone } from '../pages/authoringRequests'
import { designTokensFor } from './designTokens'
import { StatusTag, type StatusTone } from './StatusTag'

// Proposed: a status as a tag whose tone sets its colours, see the review on #3263.
describe('StatusTag', () => {
  const { colour } = designTokensFor('light')

  it.each<[StatusTone, string]>([
    ['neutral', colour.text],
    ['attention', colour.warningText],
    ['success', colour.successText],
    ['danger', colour.dangerText],
    ['muted', colour.textMuted],
  ])('draws the %s tone in its text colour, with its words', (tone, text) => {
    render(<StatusTag tone={tone}>Ready</StatusTag>)

    const tag = screen.getByText('Ready')
    expect(tag).toHaveStyle({ color: text })
    expect(tag).toHaveAttribute('data-tone', tone)
    expect(tag).not.toHaveStyle({ fontWeight: 600 })
  })

  it('gives a request the tone of what its state asks', () => {
    expect(requestStateTone('working')).toBe('neutral')
    expect(requestStateTone('fixing')).toBe('neutral')
    expect(requestStateTone('awaitingApproval')).toBe('attention')
    expect(requestStateTone('ready')).toBe('attention')
    expect(requestStateTone('accepted')).toBe('success')
    expect(requestStateTone('failed')).toBe('danger')
    expect(requestStateTone('dropped')).toBe('muted')
  })
})
