import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { findKeycloakReferences, isExactKeycloakImage, keycloakPin } from './lib/images.mjs'

const root = fileURLToPath(new URL('..', import.meta.url))

describe('the repository', () => {
  const pin = keycloakPin(root)
  const files = execFileSync('git', ['ls-files'], { cwd: root, encoding: 'utf8' }).split('\n').filter(Boolean)
  const references = findKeycloakReferences(root, files)

  test('pins Keycloak to an exact version', () => {
    assert.ok(isExactKeycloakImage(pin), `${pin} is not an exact version`)
  })

  test('names the pinned image in every Keycloak reference', () => {
    for (const { file, line, image } of references) {
      assert.equal(image, pin, `${file}:${line} names ${image}, not ${pin}`)
    }
  })

  test('finds the references in the files that name Keycloak', () => {
    const found = new Set(references.map((reference) => reference.file))
    for (const file of [
      'tests/Axis.Integration.Tests/KeycloakFixture.cs',
      'scripts/e2e.sh',
      'compose.yaml',
      'deploy/compose/compose.yaml',
      'deploy/helm/axis/values.yaml',
    ]) {
      assert.ok(found.has(file), `${file} has no Keycloak reference found`)
    }
  })
})

describe('findKeycloakReferences', () => {
  let temp

  before(() => {
    temp = mkdtempSync(path.join(tmpdir(), 'keycloak-image-'))
    mkdirSync(path.join(temp, 'tests/Axis.Integration.Tests'), { recursive: true })
    mkdirSync(path.join(temp, 'scripts'))
    writeFileSync(path.join(temp, 'Dockerfile'), 'FROM base AS axis-server\n')
    writeFileSync(
      path.join(temp, 'tests/Axis.Integration.Tests/KeycloakFixture.cs'),
      '    public const string Image = "quay.io/keycloak/keycloak:26.8.1";\n',
    )
    writeFileSync(path.join(temp, 'compose.yaml'), 'services:\n  keycloak:\n    image: quay.io/keycloak/keycloak:9.9.9\n')
    writeFileSync(path.join(temp, 'scripts/x.test.mjs'), "const image = 'quay.io/keycloak/keycloak:1.2'\n")
  })

  after(() => rmSync(temp, { recursive: true, force: true }))

  test('reports the file, line and image of a reference that differs from the pin', () => {
    const files = ['tests/Axis.Integration.Tests/KeycloakFixture.cs', 'compose.yaml', 'missing.txt']
    const pin = keycloakPin(temp)
    const moved = findKeycloakReferences(temp, files).filter((reference) => reference.image !== pin)
    assert.deepEqual(moved, [{ file: 'compose.yaml', line: 3, image: 'quay.io/keycloak/keycloak:9.9.9' }])
  })

  test('skips the scripts test files', () => {
    assert.deepEqual(findKeycloakReferences(temp, ['scripts/x.test.mjs']), [])
  })
})

describe('isExactKeycloakImage', () => {
  test('rejects a moving tag', () => {
    assert.equal(isExactKeycloakImage('quay.io/keycloak/keycloak:26.8'), false)
    assert.equal(isExactKeycloakImage('quay.io/keycloak/keycloak:latest'), false)
  })

  test('accepts an exact version', () => {
    assert.equal(isExactKeycloakImage('quay.io/keycloak/keycloak:26.8.1'), true)
  })
})
