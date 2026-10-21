import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { chmodSync, existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { after, before, describe, test } from 'node:test'
import { changedPaths, decide, decideSafe, matches, safeSuites, smokeSuites } from './lib/affected.mjs'

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
    for (const matchers of [smokeSuites.compose, smokeSuites['dev-script']]) {
      assert.equal(decide({ base: 'abc', matchers, listChanged: () => ['compose.yaml'] }).run, true)
    }
  })

  test('skips every suite when only unrelated paths changed', () => {
    const listChanged = () => ['docs/roadmap.md', 'src/Axis.Engine/Foo.cs']
    for (const matchers of Object.values(smokeSuites)) {
      assert.equal(decide({ base: 'abc', matchers, listChanged }).run, false)
    }
  })
})

describe('decideSafe', () => {
  const runs = (name, paths, base = 'abc') =>
    decideSafe({ base, matchers: safeSuites[name], listChanged: () => paths }).run

  test('skips integration when a change is only under web/src', () => {
    assert.equal(runs('integration', ['web/src/App.tsx']), false)
  })

  test('runs integration when a change also touches a path the tests read', () => {
    const others = [
      'src/Axis.Server/Program.cs',
      'samples/apps/purchase-requests/application.json',
      'web/package.json',
      'web/package-lock.json',
      'tests/Axis.Integration.Tests/Foo.cs',
    ]
    for (const other of others) {
      assert.equal(runs('integration', ['web/src/App.tsx', other]), true, other)
    }
  })

  test('skips integration for docs, Markdown and E2E spec changes', () => {
    for (const paths of [
      ['docs/delivery.md'],
      ['README.md'],
      ['tests/e2e/specs/x.spec.ts'],
      ['docs/x.md', 'web/src/App.tsx'],
    ]) {
      assert.equal(runs('integration', paths), false, paths.join(' '))
    }
  })

  test('skips Playwright when every change is under docs or a Markdown file', () => {
    assert.equal(runs('playwright', ['docs/delivery.md']), false)
    assert.equal(runs('playwright', ['README.md', 'docs/x.md']), false)
  })

  test('runs Playwright when a change touches the SPA or the E2E specs', () => {
    assert.equal(runs('playwright', ['web/src/App.tsx']), true)
    assert.equal(runs('playwright', ['tests/e2e/specs/x.spec.ts']), true)
  })

  test('runs both suites with no base, an empty base, a listing error or no changed path', () => {
    const listError = () => {
      throw new Error('bad revision')
    }
    for (const name of Object.keys(safeSuites)) {
      const matchers = safeSuites[name]
      assert.equal(decideSafe({ base: undefined, matchers, listChanged: unreachable }).run, true)
      assert.equal(decideSafe({ base: '', matchers, listChanged: unreachable }).run, true)
      assert.equal(decideSafe({ base: 'abc', matchers, listChanged: listError }).run, true)
      assert.equal(decideSafe({ base: 'abc', matchers, listChanged: () => [] }).run, true)
    }
  })
})

describe('matches', () => {
  test('covers csproj files and appsettings.json files under src for compose', () => {
    assert.equal(matches('src/Axis.Data/Axis.Data.csproj', smokeSuites.compose), true)
    assert.equal(matches('src/Axis.Server/appsettings.json', smokeSuites.compose), true)
  })

  test('covers appsettings.Development.json under src for both suites', () => {
    for (const path of ['src/Axis.Server/appsettings.Development.json', 'src/Axis.Worker/appsettings.Development.json']) {
      assert.equal(matches(path, smokeSuites['dev-script']), true)
      assert.equal(matches(path, smokeSuites.compose), true)
    }
  })

  test('covers the sample extension packages for both suites', () => {
    for (const matchers of [smokeSuites.compose, smokeSuites['dev-script']]) {
      assert.equal(matches('samples/packages/PaymentReferences/axis-package.json', matchers), true)
    }
  })

  test('covers the sample modules for both suites', () => {
    for (const matchers of [smokeSuites.compose, smokeSuites['dev-script']]) {
      assert.equal(matches('samples/modules/departments/module.json', matchers), true)
    }
  })

  test('covers the purchasing system stub for both suites', () => {
    for (const matchers of [smokeSuites.compose, smokeSuites['dev-script']]) {
      assert.equal(matches('tests/Axis.PurchasingStub/PurchasingStubApp.cs', matchers), true)
    }
  })

  test('covers scripts/lib for both suites', () => {
    for (const matchers of [smokeSuites.compose, smokeSuites['dev-script']]) {
      assert.equal(matches('scripts/lib/x.mjs', matchers), true)
    }
  })
})

describe('images', () => {
  test('covers the Dockerfile and the admin host, but not a sample application', () => {
    assert.equal(matches('Dockerfile', smokeSuites.images), true)
    assert.equal(matches('src/Axis.Admin/AdminCommands.cs', smokeSuites.images), true)
    assert.equal(matches('samples/apps/leave-requests/application.json', smokeSuites.images), false)
  })
})

describe('onprem', () => {
  test('covers the Compose example and the admin host, but not a sample it does not activate', () => {
    assert.equal(matches('deploy/compose/compose.yaml', smokeSuites.onprem), true)
    assert.equal(matches('src/Axis.Admin/AdminCommands.cs', smokeSuites.onprem), true)
    assert.equal(matches('samples/apps/leave-requests/application.json', smokeSuites.onprem), true)
    assert.equal(matches('samples/apps/purchase-requests/application.json', smokeSuites.onprem), false)
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

describe('check scripts', () => {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
  let dir
  let env

  const writeExecutable = (name, body) => {
    writeFileSync(path.join(dir, name), `#!/bin/sh\n${body}`)
    chmodSync(path.join(dir, name), 0o755)
  }

  const scenarioSuites = () =>
    ['samples/apps', 'samples/templates'].flatMap((group) =>
      readdirSync(path.join(root, group))
        .map((folder) => path.join(root, group, folder, 'application.json'))
        .filter((manifest) => existsSync(manifest))
        .map((manifest) => `scenarios-${JSON.parse(readFileSync(manifest, 'utf8')).name}`),
    )

  const run = (script, changed) =>
    spawnSync('bash', [script], {
      cwd: root,
      encoding: 'utf8',
      timeout: 60_000,
      env: { ...env, FAKE_CHANGED: changed },
    })

  before(() => {
    dir = mkdtempSync(path.join(tmpdir(), 'check-scripts-'))
    writeExecutable('git', `[ "$1" = diff ] && printf '%s\\n' "$FAKE_CHANGED"\nexit 0\n`)
    writeExecutable('docker', `printf '%s\\n' "$*" >>"$FAKE_DOCKER_LOG"\nexit 1\n`)
    env = {
      ...process.env,
      PATH: `${dir}${path.delimiter}${process.env.PATH}`,
      NEXKIT_BASE_SHA: 'abc',
      AXIS_TEST_RESULTS_DIR: path.join(dir, 'results'),
      FAKE_DOCKER_LOG: path.join(dir, 'docker-calls'),
    }
    delete env.GITHUB_ACTIONS
    delete env.GITHUB_STEP_SUMMARY
  })

  after(() => rmSync(dir, { recursive: true, force: true }))

  test('integration.sh skips the integration tests and every scenario suite for a web/src change', () => {
    const result = run('scripts/integration.sh', 'web/src/x.ts')

    assert.equal(result.status, 0, result.stdout + result.stderr)
    const suites = ['Axis.Integration.Tests', ...scenarioSuites()]
    assert.ok(scenarioSuites().length > 0)
    for (const suite of suites) {
      assert.ok(result.stdout.includes(`${suite} (skipped: not affected by this change)`), suite)
    }
  })

  test('e2e.sh skips Playwright and starts no container for a docs change', () => {
    const result = run('scripts/e2e.sh', 'docs/x.md')

    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.ok(result.stdout.includes('playwright (skipped: not affected by this change)'), result.stdout)
    const calls = existsSync(env.FAKE_DOCKER_LOG) ? readFileSync(env.FAKE_DOCKER_LOG, 'utf8') : ''
    assert.doesNotMatch(calls, /^run /m)
  })
})
