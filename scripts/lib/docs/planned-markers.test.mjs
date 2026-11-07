import assert from 'node:assert/strict'
import { beforeEach, describe, test } from 'node:test'
import { checkPlannedMarkers } from './planned-markers.mjs'
import { useTempRoot } from './testing.mjs'

const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkPlannedMarkers)

describe('planned markers', () => {
  const roadmap = [
    '# Roadmap',
    '',
    '| Milestone | Name | Status | Delivers |',
    '| --- | --- | --- | --- |',
    '| **M1** | Walking skeleton | Done | Entities. |',
    '| **M2** | Data and rules | Planned | Rules. |',
  ].join('\n')

  beforeEach(() => {
    write('docs/roadmap.md', roadmap + '\n')
  })

  test('passes a marker for a milestone that is not Done', () => {
    write('docs/reference/guide.md', '# Guide\n\n## Rules\n\n_(planned for M2)_\n')

    assert.deepEqual(messages(), [])
  })

  test('passes a marker under a heading, after a bold lead-in, in a table row, in code and in a fence', () => {
    write(
      'docs/reference/guide.md',
      [
        '# Guide',
        '',
        '## Rules',
        '',
        '_(planned for M2)_',
        '',
        '- **A rule** _(planned for M2)_. Text.',
        '',
        '| Row | `x` _(planned for M2)_ |',
        '',
        'Write `_(planned for M2)_` under the heading.',
        '',
        '```',
        'Text _(planned for M2)_ here.',
        '```',
      ].join('\n') + '\n',
    )

    assert.deepEqual(messages(), [])
  })

  test('reports a marker in a sentence, and one on its own line that no heading heads', () => {
    write('docs/reference/guide.md', '# Guide\n\nText _(planned for M2)_ here.\n\nText.\n\n_(planned for M2)_\n')

    assert.deepEqual(messages(), [
      'docs/reference/guide.md:3: _(planned for M2)_ sits in a sentence: put it on a line of its own under the heading of a planned section, or right after the bold lead-in of a planned rule',
      'docs/reference/guide.md:7: _(planned for M2)_ sits in a sentence: put it on a line of its own under the heading of a planned section, or right after the bold lead-in of a planned rule',
    ])
  })

  test('reports each Done milestone a marker names, alone or in a list', () => {
    write('docs/reference/guide.md', '# Guide\n\n## Entities\n\n_(planned for M1)_\n\n- **Text** _(planned for M2 and M1)_.\n')

    assert.deepEqual(messages(), [
      'docs/reference/guide.md:5: planned for M1, which the roadmap calls Done: remove the marker, or move it to the milestone that builds the rest',
      'docs/reference/guide.md:7: planned for M1, which the roadmap calls Done: remove the marker, or move it to the milestone that builds the rest',
    ])
  })
})
