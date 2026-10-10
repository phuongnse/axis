import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { changedPaths, decide, matches, smokeSuites } from './lib/affected.mjs'

const unreachable = () => assert.fail('The changed paths should not be listed.')

describe('decide', () => {
  test('runs when the base is unset or empty', () => {
    for (const base of [undefined, '']) {
      assert.equal(decide({ base, matchers: smokeSuites.compose, listChanged: unreachable }).run, true)
    }
  })

  test('runs when the changed paths cannot be listed', () => {
    const listChanged = () => {
      throw new Error('bad revision')
    }
    const decision = decide({ base: 'abc', matchers: smokeSuites['dev-script'], listChanged })

    assert.equal(decision.run, true)
    assert.match(decision.reason, /bad revision/)
  })

  test('runs both suites when a path they cover changed', () => {
    for (const matchers of Object.values(smokeSuites)) {
      assert.equal(decide({ base: 'abc', matchers, listChanged: () => ['compose.yaml'] }).run, true)
    }
  })

  test('skips both suites when only unrelated paths changed', () => {
    const listChanged = () => ['docs/roadmap.md', 'src/Axis.Engine/Foo.cs']
    for (const matchers of Object.values(smokeSuites)) {
      assert.equal(decide({ base: 'abc', matchers, listChanged }).run, false)
    }
  })
})

describe('matches', () => {
  test('covers csproj files and appsettings.json files under src for compose', () => {
    assert.equal(matches('src/Axis.Data/Axis.Data.csproj', smokeSuites.compose), true)
    assert.equal(matches('src/Axis.Server/appsettings.json', smokeSuites.compose), true)
  })

  test('treats appsettings.Development.json as dev-script only', () => {
    const path = 'src/Axis.Server/appsettings.Development.json'

    assert.equal(matches(path, smokeSuites['dev-script']), true)
    assert.equal(matches(path, smokeSuites.compose), false)
  })

  test('covers scripts/lib for both suites', () => {
    for (const matchers of Object.values(smokeSuites)) {
      assert.equal(matches('scripts/lib/x.mjs', matchers), true)
    }
  })
})

describe('changedPaths', () => {
  let dir

  before(() => {
    dir = mkdtempSync(path.join(tmpdir(), 'affected-'))
    const git = (...args) => execFileSync('git', args, { cwd: dir, stdio: 'pipe' })
    git('init', '-q')
    writeFileSync(path.join(dir, 'README.md'), '# Sample\n')
    git('add', '.')
    git(
      '-c',
      'user.name=t',
      '-c',
      'user.email=t@example.com',
      '-c',
      'commit.gpgsign=false',
      'commit',
      '-q',
      '-m',
      'Add readme',
    )
  })

  after(() => rmSync(dir, { recursive: true, force: true }))

  test('lists untracked files, so a new matching file runs the suite', () => {
    assert.deepEqual(changedPaths('HEAD', dir), [])
    writeFileSync(path.join(dir, 'compose.yaml'), 'services: {}\n')

    assert.deepEqual(changedPaths('HEAD', dir), ['compose.yaml'])
    assert.equal(
      decide({ base: 'HEAD', matchers: smokeSuites.compose, listChanged: (base) => changedPaths(base, dir) }).run,
      true,
    )
  })

  test('throws when the base commit does not exist', () => {
    assert.throws(() => changedPaths('0000000000000000000000000000000000000000', dir))
  })
})
