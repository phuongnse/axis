import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, relative, sep } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/lib/install-modules.sh in a temporary tree: a built-in module under modules and a sample
// module under samples/modules, each with a file in a subfolder, and a folder with no module.json.
const root = fileURLToPath(new URL('..', import.meta.url))

function install(setup) {
  const dir = mkdtempSync(join(tmpdir(), 'axis-install-modules-'))
  try {
    const script = join(dir, 'scripts/lib/install-modules.sh')
    mkdirSync(dirname(script), { recursive: true })
    copyFileSync(join(root, 'scripts/lib/install-modules.sh'), script)
    const write = (file, text) => {
      mkdirSync(dirname(join(dir, file)), { recursive: true })
      writeFileSync(join(dir, file), text)
    }
    setup(write)
    const result = spawnSync('bash', ['scripts/lib/install-modules.sh'], { cwd: dir, encoding: 'utf8', timeout: 60_000 })
    const folder = join(dir, 'artifacts/modules')
    const files = existsSync(folder)
      ? Object.fromEntries(
          readdirSync(folder, { recursive: true, withFileTypes: true })
            .filter((entry) => entry.isFile())
            .map((entry) => {
              const file = join(entry.parentPath, entry.name)
              return [relative(folder, file).split(sep).join('/'), readFileSync(file, 'utf8')]
            }),
        )
      : {}
    return { ...result, files }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

describe('scripts/lib/install-modules.sh', () => {
  test('installs the modules under modules and samples/modules at the id and version of their module.json', () => {
    const run = install((write) => {
      write('modules/b/module.json', '{"id":"b","version":"1.0.0"}')
      write('modules/b/rules/x.json', '{"name":"X"}')
      write('samples/modules/a/module.json', '{"id":"a","version":"1.1.0"}')
      write('samples/modules/a/texts/en.json', '{"name":"TextsEn"}')
      write('samples/modules/no-manifest/readme.txt', 'not a module')
    })

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(run.files, {
      'b/1.0.0/module.json': '{"id":"b","version":"1.0.0"}',
      'b/1.0.0/rules/x.json': '{"name":"X"}',
      'a/1.1.0/module.json': '{"id":"a","version":"1.1.0"}',
      'a/1.1.0/texts/en.json': '{"name":"TextsEn"}',
    })
  })

  test('installs the sample modules when there is no modules folder', () => {
    const run = install((write) => write('samples/modules/a/module.json', '{"id":"a","version":"1.1.0"}'))

    assert.equal(run.status, 0, run.stdout + run.stderr)
    assert.deepEqual(Object.keys(run.files), ['a/1.1.0/module.json'])
  })
})
