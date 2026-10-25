import { describe, expect, it, vi } from 'vitest'
import { dropOnForm } from './formPaletteSupport'

const words: Record<string, string> = {
  'shell.studio.palette.moved': 'Moved',
  'shell.studio.palette.position': 'position',
}
const t = (key: string) => words[key] ?? key

describe('dropOnForm', () => {
  it('appends a section dropped on a step of the switch to that step and reorders sections', () => {
    const draft = {
      sections: [
        { name: 'a', fields: [] },
        { name: 'b', fields: [] },
      ],
      steps: [
        { title: { textKey: 'steps.one' }, sections: [] },
        { title: { textKey: 'steps.two' }, sections: ['a', 'b'] },
      ],
    }
    const onChange = vi.fn()
    const onSelect = vi.fn()
    const onAnnounce = vi.fn()

    dropOnForm(t, draft, { section: 1, label: 'b' }, { pointer: '/steps/0' }, onChange, onSelect, onAnnounce)

    expect(onChange).toHaveBeenCalledWith('/sections', [draft.sections[1], draft.sections[0]])
    expect(onChange).toHaveBeenCalledWith('/steps', [
      { title: { textKey: 'steps.one' }, sections: ['b'] },
      { title: { textKey: 'steps.two' }, sections: ['a'] },
    ])
    expect(onSelect).toHaveBeenCalledWith('/sections/0')
    expect(onAnnounce).toHaveBeenCalledWith('Moved b · steps.one · position 1')
  })

  it('refuses an unnamed section dropped on a step and changes nothing', () => {
    const draft = {
      sections: [{ fields: [] }],
      steps: [{ title: { textKey: 'steps.one' }, sections: [] }],
    }
    const onChange = vi.fn()
    const onSelect = vi.fn()
    const onAnnounce = vi.fn()

    dropOnForm(t, draft, { section: 0, label: 'Section 1' }, { pointer: '/steps/0' }, onChange, onSelect, onAnnounce)

    expect(onChange).not.toHaveBeenCalled()
    expect(onSelect).not.toHaveBeenCalled()
    expect(onAnnounce).not.toHaveBeenCalled()
  })
})
