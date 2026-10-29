import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { cardGroups, cardName } from '../web/src/designSystem/cardOrder.ts'
import { checkDesignSystemBundle } from './check-design-system.mjs'

const script = fileURLToPath(new URL('./check-design-system.mjs', import.meta.url))
const components = cardGroups.flatMap((group) => group.previews).map((preview) => ({ name: cardName(preview) }))
const headerLine = (fields) => `/* @ds-bundle: ${JSON.stringify(fields)} */\n`
const header = headerLine({ format: 4, namespace: 'Axis', components })
const valid = header + 'var Axis = (function () { return { table: function () {} } })()\n'

let root

function write(file, content) {
  writeFileSync(path.join(root, file), content)
}

beforeEach(() => {
  root = mkdtempSync(path.join(tmpdir(), 'check-design-system-'))
})

afterEach(() => rmSync(root, { recursive: true, force: true }))

describe('checkDesignSystemBundle', () => {
  test('passes a folder that holds one classic script', () => {
    write('axis-design-system.js', valid)

    const { bytes, problems } = checkDesignSystemBundle(root)

    assert.deepEqual(problems, [])
    assert.equal(bytes, Buffer.byteLength(valid))
  })

  test('fails a bundle with no @ds-bundle line first', () => {
    write('axis-design-system.js', valid.slice(header.length))

    const { problems } = checkDesignSystemBundle(root)

    assert.equal(problems.length, 1)
    assert.match(problems[0], /must start with a line like \/\* @ds-bundle/)
  })

  test('fails a @ds-bundle line that comes second', () => {
    write('axis-design-system.js', `var Axis = 1\n${header}`)

    const { problems } = checkDesignSystemBundle(root)

    assert.equal(problems.length, 1)
    assert.match(problems[0], /must start with a line/)
  })

  test('fails a @ds-bundle line that is not JSON', () => {
    write('axis-design-system.js', `/* @ds-bundle: {width: 992} */\nvar Axis = 1\n`)

    const { problems } = checkDesignSystemBundle(root)

    assert.equal(problems.length, 1)
    assert.match(problems[0], /is not JSON/)
  })

  for (const format of [undefined, 3]) {
    test(`fails a @ds-bundle line with format ${format}`, () => {
      write('axis-design-system.js', headerLine({ format, namespace: 'Axis', components }) + 'var Axis = 1\n')

      const { problems } = checkDesignSystemBundle(root)

      assert.deepEqual(problems, ['The @ds-bundle line of axis-design-system.js needs format 4.'])
    })
  }

  test('fails a @ds-bundle line with another namespace', () => {
    write('axis-design-system.js', headerLine({ format: 4, namespace: 'Other', components }) + 'var Axis = 1\n')

    const { problems } = checkDesignSystemBundle(root)

    assert.deepEqual(problems, ['The @ds-bundle line of axis-design-system.js needs the namespace "Axis".'])
  })

  test('fails a @ds-bundle line with no components', () => {
    write('axis-design-system.js', headerLine({ format: 4, namespace: 'Axis', components: [] }) + 'var Axis = 1\n')

    const { problems } = checkDesignSystemBundle(root)

    assert.equal(problems.length, 1)
    assert.match(problems[0], /needs components/)
  })

  test('fails a card that the bundle leaves out', () => {
    const withoutTable = components.filter(({ name }) => name !== 'Table')
    write('axis-design-system.js', headerLine({ format: 4, namespace: 'Axis', components: withoutTable }) + 'var Axis = 1\n')

    const { problems } = checkDesignSystemBundle(root)

    assert.deepEqual(problems, ['The @ds-bundle line of axis-design-system.js lacks a card for: Table.'])
  })

  test('fails a card that the bundle names twice', () => {
    const withTableTwice = [...components, { name: 'Table' }]
    write('axis-design-system.js', headerLine({ format: 4, namespace: 'Axis', components: withTableTwice }) + 'var Axis = 1\n')

    const { problems } = checkDesignSystemBundle(root)

    assert.deepEqual(problems, ['The @ds-bundle line of axis-design-system.js lists a card twice: Table.'])
  })

  test('fails a card name that matches no preview', () => {
    const withUnknown = [...components, { name: 'Nope' }]
    write('axis-design-system.js', headerLine({ format: 4, namespace: 'Axis', components: withUnknown }) + 'var Axis = 1\n')

    const { problems } = checkDesignSystemBundle(root)

    assert.deepEqual(problems, [
      'The @ds-bundle line of axis-design-system.js names a card that matches no preview: Nope.',
    ])
  })

  test('fails an HTML file next to the script', () => {
    write('axis-design-system.js', valid)
    write('index.html', '<!doctype html>')

    const { problems } = checkDesignSystemBundle(root)

    assert.equal(problems.length, 1)
    assert.match(problems[0], /must hold only axis-design-system\.js.*index\.html/)
  })

  test('fails a folder with no script', () => {
    const { problems } = checkDesignSystemBundle(root)

    assert.equal(problems.length, 1)
    assert.match(problems[0], /no files/)
  })

  test('fails an import declaration', () => {
    write('axis-design-system.js', header + "import x from 'y'\nvar Axis = x\n")

    const { problems } = checkDesignSystemBundle(root)

    assert.equal(problems.length, 1)
    assert.match(problems[0], /not a classic script/)
  })

  test('fails import.meta', () => {
    write('axis-design-system.js', header + 'var Axis = import.meta.url\n')

    const { problems } = checkDesignSystemBundle(root)

    assert.match(problems.join('\n'), /not a classic script/)
  })

  test('fails a dynamic import', () => {
    write('axis-design-system.js', header + "var Axis = import('./a.js')\n")

    const { problems } = checkDesignSystemBundle(root)

    assert.deepEqual(problems, ['axis-design-system.js has a dynamic import().'])
  })

  test('fails a literal </script', () => {
    write('axis-design-system.js', header + "var Axis = '</script>'\n")

    const { problems } = checkDesignSystemBundle(root)

    assert.deepEqual(problems, ['axis-design-system.js has a literal </script.'])
  })

  test('fails a literal <!--', () => {
    write('axis-design-system.js', header + "var Axis = '<!--'\n")

    const { problems } = checkDesignSystemBundle(root)

    assert.deepEqual(problems, ['axis-design-system.js has a literal <!--.'])
  })

  test('fails a script over 6 MB', () => {
    write('axis-design-system.js', header + `var Axis = '${'a'.repeat(6 * 1024 * 1024)}'\n`)

    const { problems } = checkDesignSystemBundle(root)

    assert.equal(problems.length, 1)
    assert.match(problems[0], /over the 6\.00 MB cap/)
  })
})

describe('check-design-system.mjs', () => {
  const run = (...args) => spawnSync(process.execPath, [script, ...args], { encoding: 'utf8' })

  test('prints the size and exits 0 for a valid folder', () => {
    write('axis-design-system.js', valid)

    const result = run(root)

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /Design-system bundle: 0\.00 MB, cap 6\.00 MB\./)
  })

  test('exits 1 and names the problem for an invalid folder', () => {
    write('axis-design-system.js', header + "var Axis = '<!--'\n")

    const result = run(root)

    assert.equal(result.status, 1)
    assert.match(result.stderr, /literal <!--/)
  })
})
