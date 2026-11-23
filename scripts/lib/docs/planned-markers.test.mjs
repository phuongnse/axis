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

  test('passes a numbered planned marker under a heading, after a bold lead-in and in a table row', () => {
    write(
      'docs/reference/guide.md',
      ['# Guide', '', '## Rules', '', '_(planned, #12)_', '', '- **A rule** _(planned for M35, #12)_. Text.', '', '| Row | _(planned, #12)_ |', ''].join('\n'),
    )

    assert.deepEqual(messages(), [])
  })

  test('reports a bare planned marker after a bold lead-in and in a table row, with its file and line', () => {
    write(
      'docs/reference/guide.md',
      ['# Guide', '', '## Rules', '', '- **A rule** _(planned)_. Text.', '', '| Row | _(planned)_ |', ''].join('\n'),
    )

    assert.deepEqual(messages(), [
      'docs/reference/guide.md:5: _(planned)_ names no issue: write _(planned, #<n>)_ with the open issue that builds the rule',
      'docs/reference/guide.md:7: _(planned)_ names no issue: write _(planned, #<n>)_ with the open issue that builds the rule',
    ])
  })

  test('ignores a bare planned marker in a code span or a fence', () => {
    write(
      'docs/reference/guide.md',
      ['# Guide', '', 'Write `_(planned)_` for agreed work.', '', '```', 'Text _(planned)_ here.', '```', ''].join('\n'),
    )

    assert.deepEqual(messages(), [])
  })

  test('reports a bare planned marker in a sentence, as a sentence and as naming no issue', () => {
    write('docs/reference/guide.md', '# Guide\n\nText _(planned)_ here.\n')

    assert.deepEqual(messages(), [
      'docs/reference/guide.md:3: _(planned)_ sits in a sentence: put it on a line of its own under the heading of a planned section, or right after the bold lead-in of a planned rule',
      'docs/reference/guide.md:3: _(planned)_ names no issue: write _(planned, #<n>)_ with the open issue that builds the rule',
    ])
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
