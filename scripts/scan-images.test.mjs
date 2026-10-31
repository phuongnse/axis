import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { findImages } from './lib/images.mjs'

// Runs scripts/scan-images.sh with stub `docker` and `trivy` commands. The stub Trivy prints recorded
// reports for real advisories, so the tests need no Docker, no network and no change when an
// advisory changes.
const root = fileURLToPath(new URL('..', import.meta.url))
const script = fileURLToPath(new URL('./scan-images.sh', import.meta.url))
const fixtures = fileURLToPath(new URL('./fixtures/scan-images/', import.meta.url))

let stubs
let dockerLog
let trivyLog

before(() => {
  stubs = mkdtempSync(path.join(tmpdir(), 'scan-images-'))
  const docker = path.join(stubs, 'docker')
  writeFileSync(
    docker,
    [
      '#!/usr/bin/env bash',
      'echo "$*" >> "$DOCKER_LOG"',
      'if [ "$1" = "${DOCKER_FAIL:-}" ]; then exit 1; fi',
      'exit 0',
    ].join('\n'),
  )
  // Prints TRIVY_FIXTURE_<name>, where <name> is the image's last path part before the tag with
  // dashes as underscores, such as axis_server or keycloak, else TRIVY_FIXTURE.
  const trivy = path.join(stubs, 'trivy')
  writeFileSync(
    trivy,
    [
      '#!/usr/bin/env bash',
      'reference="${@: -1}"',
      'echo "$reference" >> "$TRIVY_LOG"',
      'name="${reference##*/}"',
      'name="${name%%:*}"',
      'fixture="TRIVY_FIXTURE_${name//-/_}"',
      'cat "${!fixture:-$TRIVY_FIXTURE}"',
      'exit "${TRIVY_EXIT:-0}"',
    ].join('\n'),
  )
  chmodSync(docker, 0o755)
  chmodSync(trivy, 0o755)
})

after(() => rmSync(stubs, { recursive: true, force: true }))

beforeEach(() => {
  dockerLog = path.join(stubs, `docker-${Date.now()}-${Math.random()}.log`)
  trivyLog = path.join(stubs, `trivy-${Date.now()}-${Math.random()}.log`)
  writeFileSync(dockerLog, '')
  writeFileSync(trivyLog, '')
})

// The accepted-list fixtures count their days back from this date. The tests never read the real
// accepted list, whose entries change with each scan and would not fit this date.
const today = '2026-10-28'

function scan({ trivy = 'trivy-clean.json', accepted = 'accepted-empty.json', ...env } = {}) {
  return spawnSync('bash', [script], {
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${stubs}${path.delimiter}${process.env.PATH}`,
      TRIVY: path.join(stubs, 'trivy'),
      TRIVY_FIXTURE: `${fixtures}${trivy}`,
      DOCKER_LOG: dockerLog,
      TRIVY_LOG: trivyLog,
      AXIS_RETRY_DELAY_SECONDS: '0',
      SCAN_TODAY: today,
      SCAN_IMAGES_ACCEPTED: `${fixtures}${accepted}`,
      ...env,
    },
  })
}

const logLines = (file) => readFileSync(file, 'utf8').split('\n').filter(Boolean)

describe('scripts/scan-images.sh', () => {
  test('passes when no image has a finding', () => {
    const result = scan()
    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.match(result.stdout, /No known vulnerabilities/)
  })

  test('scans every axis-* Dockerfile stage and the identity provider image that KeycloakFixture pins', () => {
    const dockerfile = readFileSync(path.join(root, 'Dockerfile'), 'utf8')
    const stages = [...dockerfile.matchAll(/^FROM\s+\S+\s+AS\s+(axis-[\w-]+)\s*$/gim)].map((match) => match[1])
    const fixture = readFileSync(path.join(root, 'tests/Axis.Integration.Tests/KeycloakFixture.cs'), 'utf8')
    const keycloak = fixture.match(/public const string Image = "([^"]+)";/)[1]
    for (const target of ['axis-server', 'axis-worker', 'axis-admin']) assert.ok(stages.includes(target), target)
    assert.match(keycloak, /keycloak/)

    const result = scan()
    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.deepEqual(logLines(trivyLog), [...stages.map((stage) => `${stage}:scan`), keycloak])
    const docker = logLines(dockerLog)
    for (const stage of stages) {
      assert.ok(
        docker.includes(`build --target ${stage} --build-arg AXIS_VERSION=0.0.0-scan -t ${stage}:scan .`),
        docker.join('\n'),
      )
    }
    assert.ok(docker.includes(`pull ${keycloak}`), docker.join('\n'))
  })

  test('fails on a high finding and prints the image, the package and the vulnerability id', () => {
    const result = scan({ TRIVY_FIXTURE_axis_worker: `${fixtures}trivy-high.json` })
    assert.equal(result.status, 1)
    assert.match(result.stdout, /axis-worker:scan\s+high\s+libc6 2\.36-9\+deb12u1\s+CVE-2023-4911/)
    assert.match(result.stdout, /1 known vulnerabilities, 1 high or critical/, 'a repeated finding counts once')
  })

  test('fails on a critical finding in the identity provider image', () => {
    const result = scan({ TRIVY_FIXTURE_keycloak: `${fixtures}trivy-critical.json` })
    assert.equal(result.status, 1)
    assert.match(result.stdout, /quay\.io\/keycloak\/keycloak:\S+\s+critical\s+zlib1g\s\S+\s+CVE-2023-45853/)
  })

  test('prints medium and low findings but passes', () => {
    const result = scan({ trivy: 'trivy-medium-low.json' })
    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.match(result.stdout, /medium\s+libtinfo6 6\.4-4\s+CVE-2023-50495/)
    assert.match(result.stdout, /low\s+apt 2\.6\.1\s+CVE-2011-3374/)
    assert.match(result.stdout, /0 high or critical/)
  })

  test('passes a high finding that was accepted 20 days ago and prints its deadline', () => {
    const result = scan({ trivy: 'trivy-high.json', accepted: 'accepted-high-20-days.json' })
    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.match(result.stdout, /CVE-2023-4911.*accepted until 2026-11-07/)
  })

  test('fails a high finding that was accepted 31 days ago and prints the missed deadline', () => {
    const result = scan({ trivy: 'trivy-high.json', accepted: 'accepted-high-31-days.json' })
    assert.equal(result.status, 1)
    assert.match(result.stdout, /CVE-2023-4911.*past its time frame since 2026-10-27/)
  })

  test('passes a critical finding accepted 3 days ago and fails one accepted 8 days ago', () => {
    const within = scan({ trivy: 'trivy-critical.json', accepted: 'accepted-critical-3-days.json' })
    assert.equal(within.status, 0, within.stdout + within.stderr)
    assert.match(within.stdout, /CVE-2023-45853.*accepted until 2026-11-01/)
    const past = scan({ trivy: 'trivy-critical.json', accepted: 'accepted-critical-8-days.json' })
    assert.equal(past.status, 1)
    assert.match(past.stdout, /CVE-2023-45853.*past its time frame since 2026-10-27/)
  })

  test('does not let an accepted entry cover another package', () => {
    const result = scan({ trivy: 'trivy-high.json', accepted: 'accepted-other-package.json' })
    assert.equal(result.status, 1)
    assert.doesNotMatch(result.stdout, /accepted until/)
  })

  for (const [file, message] of [
    ['accepted-no-package.json', /accepted list: CVE-2023-4911: has no package/],
    ['accepted-missing-date.json', /accepted list: CVE-2023-4911 libc6: found must be a date/],
    ['accepted-invalid-date.json', /accepted list: CVE-2023-4911 libc6: found must be a date/],
    ['accepted-future-date.json', /accepted list: CVE-2023-4911 libc6: found is after today/],
  ]) {
    test(`fails on the bad entry of ${file} and names it`, () => {
      const result = scan({ trivy: 'trivy-high.json', accepted: file })
      assert.equal(result.status, 1)
      assert.match(result.stderr, message)
    })
  }

  test('fails when Trivy prints output that is not JSON after its retries', () => {
    const result = scan({ TRIVY_FIXTURE_axis_admin: `${fixtures}not-json.txt` })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Scan failed, axis-admin:scan: trivy did not print JSON \(after 3 attempts\)/)
    assert.equal(logLines(trivyLog).filter((line) => line === 'axis-admin:scan').length, 3)
  })

  test('fails when Trivy exits with an error and still scans the other images', () => {
    const result = scan({ TRIVY_EXIT: '1' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Scan failed, axis-server:scan: trivy exited with status 1/)
    assert.match(result.stderr, /Scan failed, quay\.io\/keycloak/)
  })

  test('fails when an image does not build', () => {
    const result = scan({ DOCKER_FAIL: 'build' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Scan failed, axis-server:scan: docker build --target axis-server failed/)
    assert.deepEqual(logLines(trivyLog), [findImages(root).identity], 'only the pulled image is scanned')
  })
})

describe('findImages', () => {
  let folder

  beforeEach(() => {
    folder = mkdtempSync(path.join(tmpdir(), 'find-images-'))
    mkdirSync(path.join(folder, 'tests/Axis.Integration.Tests'), { recursive: true })
    writeFileSync(
      path.join(folder, 'Dockerfile'),
      [
        'FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker',
        'FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS axis-server',
        'FROM mcr.microsoft.com/dotnet/runtime:10.0 AS axis-extra',
        'FROM mcr.microsoft.com/dotnet/aspnet:10.0',
      ].join('\n'),
    )
  })

  test('finds every axis-* stage and the pinned image', () => {
    writeFileSync(
      path.join(folder, 'tests/Axis.Integration.Tests/KeycloakFixture.cs'),
      '    public const string Image = "quay.io/keycloak/keycloak:1.2";\n',
    )
    assert.deepEqual(findImages(folder), {
      targets: ['axis-server', 'axis-extra'],
      identity: 'quay.io/keycloak/keycloak:1.2',
    })
    rmSync(folder, { recursive: true, force: true })
  })

  test('throws an error that names KeycloakFixture.cs when it has no Image constant', () => {
    writeFileSync(path.join(folder, 'tests/Axis.Integration.Tests/KeycloakFixture.cs'), 'class KeycloakFixture {}\n')
    assert.throws(() => findImages(folder), /KeycloakFixture\.cs has no Image constant/)
    rmSync(folder, { recursive: true, force: true })
  })
})
