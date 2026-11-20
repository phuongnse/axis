import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { colourTokens } from './designTokens'
import type { RunState } from './inspection'
import { StateBadge } from './StateBadge'
import type { StatusTone } from './StatusTag'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

type ColourName = keyof typeof colourTokens

const catalogs = [{ texts: platformEnglish, fallbackTexts: platformEnglish }]

/** Each run state's words, its tone and the icon colour of that tone, as the process graph's rules say. */
const states: [RunState, string, StatusTone, ColourName][] = [
  ['done', 'Done', 'success', 'successText'],
  ['running', 'Running', 'neutral', 'infoText'],
  ['waitingForEvent', 'Waiting for an event', 'neutral', 'infoText'],
  ['waitingForPerson', 'Waiting for a person', 'attention', 'warningText'],
  ['failed', 'Failed', 'danger', 'dangerText'],
  ['cancelled', 'Cancelled', 'muted', 'textMuted'],
]

function renderBadge(state: RunState | undefined) {
  return render(
    <ThemeModeProvider initialMode="light">
      <TextProvider catalogs={catalogs} development={false}>
        <StateBadge state={state} />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

describe('StateBadge', () => {
  it.each(states)('draws %s in its tone, with its words as its name', (state, words, tone, iconColour) => {
    renderBadge(state)

    const badge = screen.getByRole('img', { name: words })

    expect(badge).toHaveAttribute('data-tone', tone)
    expect(badge).toHaveStyle({ color: colourTokens[iconColour].light, background: colourTokens.surface.light })
  })

  it('draws an icon on the disc', () => {
    renderBadge('done')

    expect(screen.getByRole('img', { name: 'Done' }).querySelector('.anticon')).toBeInTheDocument()
  })

  it('draws nothing for a step that was not reached', () => {
    const { container } = renderBadge(undefined)

    // The theme provider's own style element is the only thing in the container.
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(container.querySelectorAll('*:not(style)')).toHaveLength(0)
  })
})
