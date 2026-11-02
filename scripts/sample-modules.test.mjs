import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// The departments sample module is removed. The organization built-in module replaced it, so nothing in the samples,
// the scripts, the images, the end-to-end tests, the test projects or the docs may name it again. The decisions keep
// their history and are not scanned.
const root = fileURLToPath(new URL('..', import.meta.url))

const patterns = [
  /samples\/modules\/departments\b/,
  /artifacts\/modules\/departments\b/,
  /"id":\s*"departments"/,
  /(?<![\w-])departments\/[A-Z]/,
  /(?<![\w-])departments:[A-Z]/,
]

const skippedFolders = new Set(['node_modules', 'bin', 'obj'])

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
  const self = 'scripts/sample-modules.test.mjs'
  const all = (path) => path !== self
  const candidates = [
    ...['samples', 'scripts', 'Dockerfile', 'compose.yaml', 'charts', 'tests/e2e'].flatMap((path) => files(dir, path, all)),
    ...files(dir, 'tests', (path) => path.endsWith('.csproj')),
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
  const dir = mkdtempSync(join(tmpdir(), 'axis-sample-modules-'))
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

describe('the departments sample module', () => {
  test('is not named by the samples, scripts, images, end-to-end tests, test projects or docs', () => {
    const hits = scan(root)
    assert.deepEqual(hits, [], `The departments sample module is removed:\n${hits.join('\n')}`)
  })

  test('is found when a sample application declares it', () => {
    const hits = scanTree((write) => {
      write('samples/apps/x/application.json', '{ "modules": [{ "id": "departments", "version": "^1.0.0" }] }')
    })
    assert.equal(hits.length, 1)
    assert.match(hits[0], /^samples\/apps\/x\/application\.json:1:/)
  })

  test('is found in a test project that copies it, whichever way the path is written', () => {
    const hits = scanTree((write) => {
      write('tests/A/A.csproj', '<None Include="..\\..\\samples\\modules\\departments\\**\\*" />')
    })
    assert.equal(hits.length, 1)
    assert.match(hits[0], /^tests\/A\/A\.csproj:1:/)
  })

  test('is found in a doc example of its entity or its rule', () => {
    const hits = scanTree((write) => {
      write('docs/reference/x.md', 'See `departments/Department` and `departments:IsManager`.\n')
    })
    assert.equal(hits.length, 1)
  })

  test('is not found in the decisions, in test code or under another name', () => {
    const hits = scanTree((write) => {
      write('docs/decisions/d1.md', 'samples/modules/departments and departments/Department')
      write('tests/A/A.cs', 'var module = "departments/Department"; // samples/modules/departments')
      write('docs/reference/x.md', 'The organization/Departments data source and the pages/departments.json file.')
      write('samples/modules/node_modules/x/y.md', 'samples/modules/departments')
    })
    assert.deepEqual(hits, [])
  })
})
