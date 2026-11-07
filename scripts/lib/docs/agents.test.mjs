import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { checkAgents, maxAgentsRuleWords, maxAgentsWords } from './agents.mjs'
import { useTempRoot } from './testing.mjs'

const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkAgents)

const words = (count) => Array(count).fill('word').join(' ')

describe('agents', () => {
  test('reports AGENTS.md over the word limit on its first line', () => {
    write('AGENTS.md', 'word\n'.repeat(maxAgentsWords + 1))

    assert.deepEqual(messages(), [
      `AGENTS.md:1: ${maxAgentsWords + 1} words, over the limit of ${maxAgentsWords}: keep what almost every session needs and link the doc that holds the detail`,
    ])
  })

  test('does not count table pipes or dashes as words', () => {
    write('AGENTS.md', '# Agents\n\n' + '| --- | --- | --- |\n'.repeat(maxAgentsWords))

    assert.deepEqual(messages(), [])
  })

  test('passes a rule of exactly the word limit, and reports one word more at its start line', () => {
    write(
      'AGENTS.md',
      `# Agents\n\n- ${words(maxAgentsRuleWords - 1)}\n  continued\n\n- ${words(maxAgentsRuleWords)}\n  continued\n`,
    )

    assert.deepEqual(messages(), [
      `AGENTS.md:6: a rule of ${maxAgentsRuleWords + 1} words, over the limit of ${maxAgentsRuleWords}: keep the rule short and link the doc that holds the detail`,
    ])
  })

  test('reports a Commands row that names a missing script, and passes an existing one', () => {
    write('scripts/present.sh', '')
    write(
      'AGENTS.md',
      '# Agents\n\n## Commands\n\n| Script | What |\n| --- | --- |\n| `scripts/present.sh` | Runs |\n| `scripts/missing.sh` | Runs |\n\n## Next\n',
    )

    assert.deepEqual(messages(), ['AGENTS.md:8: names scripts/missing.sh, which does not exist'])
  })

  test('passes when every script the Commands table names exists', () => {
    write('scripts/present.sh', '')
    write('AGENTS.md', '# Agents\n\n## Commands\n\n| Script | What |\n| --- | --- |\n| `scripts/present.sh` | Runs |\n')

    assert.deepEqual(messages(), [])
  })
})
