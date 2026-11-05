import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// The request approval sample template is removed. The built-in approvals application replaced it, so nothing in the
// samples, the scripts, the images, the tests, the built-in packages or the docs may name it again. The decisions keep
// their history and are not scanned. The module's own processes/request-approval.json and the purchase request
// process are other things and stay legal.
const root = fileURLToPath(new URL('..', import.meta.url))

const patterns = [/(?<![\w-])request-approval(?![\w-]|\.json)/, /request-approval-journey/]

const skippedFolders = new Set(['node_modules', 'bin', 'obj', 'test-results', 'playwright-report', 'TestResults'])

function files(dir, path, include) {
  const full = join(dir, path)
  let stat
  try {
    stat = statSync(full)
  } catch {
    return []
  }
  if (!stat.isDirectory()) return include(path) ? [path] : []
  return readdirSync(full, { withFileTypes: true }).flatMap((entry) => {
    if (skippedFolders.has(entry.name)) return []
    return files(dir, `${path}/${entry.name}`, include)
  })
}

export function scan(dir) {
  const self = 'scripts/sample-templates.test.mjs'
  const all = (path) => path !== self
  const candidates = [
    ...[
      'samples',
      'scripts',
      'Dockerfile',
      'compose.yaml',
      'charts',
      'tests',
      'web/src',
      'modules',
      'applications',
      'AGENTS.md',
    ].flatMap((path) => files(dir, path, all)),
    ...files(dir, 'docs', (path) => !path.startsWith('docs/decisions/')),
  ]
  const hits = []
  for (const file of new Set(candidates)) {
    const lines = readFileSync(join(dir, file), 'utf8').replaceAll('\\', '/').split('\n')
    lines.forEach((text, index) => {
      if (patterns.some((pattern) => pattern.test(text))) hits.push(`${file}:${index + 1}: ${text.trim()}`)
    })
  }
  return hits
}

function scanTree(setup) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-sample-templates-'))
  try {
    setup((file, text) => {
      mkdirSync(dirname(join(dir, file)), { recursive: true })
      writeFileSync(join(dir, file), text)
    })
    return scan(dir)
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

describe('the request approval sample template', () => {
  test('is not named by the samples, scripts, images, tests, built-in packages or docs', () => {
    const hits = scan(root)
    assert.deepEqual(hits, [], `The request approval sample template is removed:\n${hits.join('\n')}`)
  })

  test('is found in a test project that copies it, whichever way the path is written', () => {
    const hits = scanTree((write) => {
      write('tests/A/A.csproj', '<None Include="..\\..\\samples\\templates\\request-approval\\**\\*" />')
    })
    assert.equal(hits.length, 1)
    assert.match(hits[0], /^tests\/A\/A\.csproj:1:/)
  })

  test('is found as a quoted id in test code', () => {
    const hits = scanTree((write) => {
      write('tests/A/ATests.cs', 'const string Template = "request-approval";')
    })
    assert.equal(hits.length, 1)
    assert.match(hits[0], /^tests\/A\/ATests\.cs:1:/)
  })

  test('is found in a doc mention and in the name of its journey spec', () => {
    const hits = scanTree((write) => {
      write('docs/reference/x.md', 'The `request-approval` template.\n')
      write('docs/y.md', 'See request-approval-journey.spec.ts.\n')
    })
    assert.equal(hits.length, 2)
  })

  test('is not found in the decisions, in other names or in folders of local output', () => {
    const hits = scanTree((write) => {
      write('docs/decisions/d1.md', 'The `request-approval` template.')
      write('samples/apps/x/processes/purchase-request-approval.json', '{ "name": "purchase-request-approval" }')
      write('tests/A/ATests.cs', 'Path.Combine("approvals", "processes", "request-approval.json")')
      write('samples/x/node_modules/y.md', 'request-approval')
      write('tests/e2e/test-results/z.md', 'request-approval')
    })
    assert.deepEqual(hits, [])
  })
})
