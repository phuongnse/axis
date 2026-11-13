// Smoke test for the production images axis-server, axis-worker, axis-agent, axis-admin, axis-controlplane and
// axis-package-host-dotnet, -node and -python, which scripts/smoke.sh builds: checks what they hold, and runs the hosts
// read-only with their settings in /run/secrets files. The test PostgreSQL serves TLS with a certificate from an authority
// made for the run, and the connection strings set SSL Mode=VerifyFull, which the production hosts require. Each package
// host image builds the test package of its language from a read-only source, see
// docs/reference/extension-packages.md#building-a-package. Requires Docker and openssl. Run from scripts/smoke.sh.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import {
  chmodSync,
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { listedImage } from './lib/docker-images.mjs'
import { smokeImages } from './lib/smoke-images.mjs'
import { issuePostgresCertificate, postgresTlsScript, serverCertificateMount, verifyFull } from './lib/postgres-tls.mjs'

const root = fileURLToPath(new URL('..', import.meta.url))
const timeout = 20 * 60 * 1000
const packageHosts = ['axis-package-host-dotnet', 'axis-package-host-node', 'axis-package-host-python']
const targets = ['axis-server', 'axis-worker', 'axis-agent', 'axis-admin', 'axis-controlplane', ...packageHosts]
const network = `axis-images-${process.pid}`
const postgres = `axis-images-postgres-${process.pid}`
const secretKey = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA='

let version
let folder
let trustDir
const containers = []

const image = (target) => `${target}:${version}`

const docker = (args, options = {}) => spawnSync('docker', args, { cwd: root, encoding: 'utf8', ...options })

const run = (args) => {
  const result = docker(args)
  assert.equal(result.status, 0, `docker ${args.join(' ')}\n${result.stdout}${result.stderr}`)
  return result.stdout.trim()
}

// writeSecrets: a folder the non-root user in the container can read, with one file per setting.
const writeSecrets = (name, settings) => {
  const dir = join(folder, name)
  mkdirSync(dir)
  chmodSync(dir, 0o755)
  for (const [key, value] of Object.entries(settings)) {
    writeFileSync(join(dir, key), value)
    chmodSync(join(dir, key), 0o644)
  }
  return dir
}

before(
  async () => {
    version = smokeImages(targets)
    folder = mkdtempSync(join(tmpdir(), 'axis-images-'))

    const certificates = issuePostgresCertificate({
      folder,
      name: postgres,
      openssl: (...args) => {
        const result = spawnSync('openssl', args, { encoding: 'utf8' })
        assert.equal(result.status, 0, `openssl ${args.join(' ')}\n${result.stdout}${result.stderr}`)
      },
    })
    trustDir = certificates.trustDir

    run(['network', 'create', network])
    run([
      'run',
      '-d',
      '--name',
      postgres,
      '--network',
      network,
      '--entrypoint',
      'sh',
      '-e',
      'POSTGRES_USER=axis',
      '-e',
      'POSTGRES_DB=axis',
      '-e',
      'POSTGRES_PASSWORD=images-test',
      '-v',
      `${join(root, 'samples/postgres/init-roles.sql')}:/docker-entrypoint-initdb.d/init-roles.sql:ro`,
      '-v',
      `${certificates.serverDir}:${serverCertificateMount}:ro`,
      listedImage(root, 'postgres'),
      '-c',
      postgresTlsScript,
    ])
    // Check over TCP: the initialization server listens only on the Unix socket, so the check succeeds
    // only after the init scripts have run.
    let ready = false
    let running = true
    for (let i = 0; i < 60 && !ready && running; i++) {
      if (i > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
      ready = docker(['exec', postgres, 'pg_isready', '-h', '127.0.0.1', '-U', 'axis', '-d', 'axis']).status === 0
      if (!ready) running = docker(['inspect', '-f', '{{.State.Running}}', postgres]).stdout.trim() === 'true'
    }
    if (!ready) {
      const logs = docker(['logs', postgres])
      assert.fail(
        `PostgreSQL ${running ? 'was not ready after 60 seconds' : 'stopped'}\n${logs.stdout}${logs.stderr}`,
      )
    }
  },
  { timeout },
)

after(() => {
  for (const container of [...containers, postgres]) docker(['rm', '-f', container], { stdio: 'ignore' })
  docker(['network', 'rm', network], { stdio: 'ignore' })
  if (folder) rmSync(folder, { recursive: true, force: true })
})

test('no image holds samples, development settings or source control metadata', { timeout }, () => {
  for (const target of targets) {
    const id = run(['create', image(target)])
    let paths
    try {
      const exported = spawnSync('sh', ['-c', `docker export ${id} | tar -t`], {
        encoding: 'utf8',
        maxBuffer: 1024 * 1024 * 1024,
      })
      assert.equal(exported.status, 0, exported.stderr)
      paths = exported.stdout.split('\n').filter(Boolean)
    } finally {
      docker(['rm', id], { stdio: 'ignore' })
    }

    assert.deepEqual(
      paths.filter((path) => path.startsWith('samples/')),
      [],
      `${target} holds a sample`,
    )
    assert.deepEqual(
      paths.filter((path) => path.endsWith('appsettings.Development.json')),
      [],
      `${target} holds development settings`,
    )
    assert.deepEqual(
      paths.filter((path) => /(^|\/)\.(git|svn)(\/|$)/.test(path)),
      [],
      `${target} holds source control metadata`,
    )
    // The control plane is no host of the platform and the agent host loads no package or module: neither has a mount
    // point for them. A package host image writes packages and loads no module, so it has only /extensions.
    if (target !== 'axis-controlplane' && target !== 'axis-agent') {
      assert.deepEqual(
        paths.filter((path) => /^(extensions|modules)\/./.test(path)),
        [],
        `${target} holds a package or module`,
      )
      assert.ok(paths.includes('extensions/'), `${target} has no /extensions`)
      assert.equal(paths.includes('modules/'), !packageHosts.includes(target), `${target} has the wrong /modules`)
    }
    if (target === 'axis-server') {
      assert.ok(paths.includes('app/wwwroot/index.html'), 'the server holds no SPA')
      // The server loads the git library at start, to keep each application folder's revisions.
      assert.ok(
        paths.some((path) => /^app\/runtimes\/linux-[^/]+\/native\/libgit2-[^/]+\.so$/.test(path)),
        'the server holds no libgit2',
      )
    }
    if (target === 'axis-controlplane') {
      assert.ok(paths.includes('app/wwwroot/sign-up.html'), 'the control plane holds no sign-up page')
      assert.ok(paths.includes('app/admin/Axis.ControlPlane.Admin.dll'), 'the control plane holds no operator commands')
    }

    const config = JSON.parse(run(['image', 'inspect', image(target)]))[0].Config
    assert.ok(config.User && config.User !== '0' && config.User !== 'root', `${target} runs as ${config.User}`)
    assert.equal(config.Labels['org.opencontainers.image.version'], version)
  }
})

// startReadOnly: starts a host read-only with its settings in a /run/secrets folder and returns its health URL.
const startReadOnly = (target, secrets, port, extra) => {
  const id = run([
    'run',
    '-d',
    '--read-only',
    '--network',
    network,
    '-p',
    `127.0.0.1::${port}`,
    '-v',
    `${secrets}:/run/secrets:ro`,
    '-v',
    `${trustDir}:/run/test-ca:ro`,
    '-e',
    'Tenancy__Source=directory',
    '-e',
    'Extensions__Path=/extensions',
    '-e',
    'Modules__Path=/modules',
    ...extra,
    image(target),
  ])
  containers.push(id)
  const mapped = run(['port', id, `${port}/tcp`]).split('\n')[0]
  return { id, url: `http://127.0.0.1:${mapped.split(':').pop()}/health/live` }
}

const connection = (user) => `Host=${postgres};Database=axis;Username=${user};Password=axis${verifyFull('/run/test-ca/ca.crt')}`

const waitLive = async ({ id, url }) => {
  let status = 0
  for (let i = 0; i < 120 && status !== 200; i++) {
    if (i > 0) await new Promise((resolve) => setTimeout(resolve, 1000))
    if (run(['inspect', '-f', '{{.State.Running}}', id]) !== 'true') break
    status = await fetch(url).then(
      (response) => response.status,
      () => 0,
    )
  }
  if (status !== 200) docker(['logs', id], { stdio: 'inherit' })
  assert.equal(status, 200, `${url} did not answer 200`)
  assert.notEqual(run(['exec', id, 'id', '-u']), '0')
}

test('the server starts read-only with its settings in /run/secrets', { timeout }, async () => {
  const secrets = writeSecrets('server', {
    ConnectionStrings__Platform: connection('axis_runtime'),
    ConnectionStrings__PlatformSchema: connection('axis_schema'),
    Platform__SecretKey: secretKey,
  })
  await waitLive(startReadOnly('axis-server', secrets, 8080, []))
})

test('the worker starts read-only with its settings in /run/secrets', { timeout }, async () => {
  const secrets = writeSecrets('worker', {
    ConnectionStrings__Platform: connection('axis_runtime'),
    Platform__SecretKey: secretKey,
  })
  await waitLive(startReadOnly('axis-worker', secrets, 5208, ['-e', 'Health__Urls=http://0.0.0.0:5208']))
})

test('the agent host starts read-only and answers /health/live', { timeout }, async () => {
  await waitLive(startReadOnly('axis-agent', writeSecrets('agent', {}), 5209, ['-e', 'Health__Urls=http://0.0.0.0:5209']))
})

test('the agent host starts read-only with AI authoring on and its settings in /run/secrets', { timeout }, async () => {
  const secrets = writeSecrets('agent-ai', {
    ConnectionStrings__Platform: connection('axis_runtime'),
    Platform__SecretKey: secretKey,
  })
  // It is live while the first read of the tenant directory retries.
  await waitLive(
    startReadOnly('axis-agent', secrets, 5209, [
      '-e',
      'Ai__Bindings__main__Kind=anthropic',
      '-e',
      'Ai__Bindings__main__Model=model-1',
      '-e',
      'Ai__DefaultBinding=main',
      '-e',
      'Ai__AllowTenantKeys=true',
      '-e',
      'Health__Urls=http://0.0.0.0:5209',
    ]),
  )
})

test('the control plane starts read-only with its connection string in /run/secrets', { timeout }, async () => {
  const secrets = writeSecrets('controlplane', {
    ControlPlane__ConnectionString: `Host=${postgres};Database=axis;Username=axis;Password=images-test${verifyFull('/run/test-ca/ca.crt')}`,
  })
  const started = startReadOnly('axis-controlplane', secrets, 8080, [
    '-e',
    'ControlPlane__Region=vn',
    '-e',
    'ControlPlane__TenantDomain=vn.example.com',
    '-e',
    'ControlPlane__MailFrom=no-reply@vn.example.com',
    '-e',
    'ControlPlane__Mail__Host=mail.invalid',
  ])
  await waitLive(started)
  // The operator's commands read the same settings and the schema that the service has migrated.
  const list = run(['exec', started.id, 'dotnet', '/app/admin/Axis.ControlPlane.Admin.dll', 'sign-ups', 'list'])
  assert.ok(list.startsWith('id'), 'sign-ups list printed no header')
})

test('the admin tool prints its version and has the PostgreSQL 18 tools', { timeout }, () => {
  assert.equal(run(['run', '--rm', '--read-only', image('axis-admin'), 'version']), version)
  const dump = run(['run', '--rm', '--read-only', '--entrypoint', 'pg_dump', image('axis-admin'), '--version'])
  assert.match(dump, /\(PostgreSQL\) 18\./)
  assert.notEqual(run(['run', '--rm', '--entrypoint', 'id', image('axis-admin'), '-u']), '0')
})

// The version that scripts/lib/trivy.sh pins, which each package host image must hold.
const trivyVersion = readFileSync(join(root, 'scripts/lib/trivy.sh'), 'utf8').match(/^TRIVY_VERSION=(\S+)$/m)[1]

// Copies a folder so that the non-root user of a package host image can read it: mkdtemp makes folders with mode 0700.
const copyReadable = (from, to, skip = () => false) => {
  mkdirSync(to, { recursive: true })
  chmodSync(to, 0o755)
  for (const entry of readdirSync(from, { withFileTypes: true })) {
    if (skip(entry.name)) continue
    if (entry.isDirectory()) copyReadable(join(from, entry.name), join(to, entry.name), skip)
    else {
      copyFileSync(join(from, entry.name), join(to, entry.name))
      chmodSync(join(to, entry.name), 0o644)
    }
  }
}

// The sorted paths under a folder, to show that a build leaves its read-only source as it was.
const listing = (dir) =>
  readdirSync(dir, { recursive: true })
    .map((entry) => `${entry} ${statSync(join(dir, entry)).isDirectory() ? 'dir' : statSync(join(dir, entry)).size}`)
    .sort()

describe('the package host images', () => {
  const packageUid = 1654
  const wheel = 'axis_fixture_dependency-1.0.0-py3-none-any.whl'
  let work
  let cache

  const writable = (dir) => {
    mkdirSync(dir)
    chmodSync(dir, 0o777)
    return dir
  }

  // The folders of the non-root user are 0777 so that it can write them. Trivy's vulnerability database is
  // downloaded once, in up to three attempts, because the download is the one step that needs the network and fails at
  // times. The builds then use it with TRIVY_SKIP_DB_UPDATE, as scripts/scan-images.mjs retries its Trivy runs.
  before(
    async () => {
      work = mkdtempSync(join(tmpdir(), 'axis-package-hosts-'))
      chmodSync(work, 0o755)
      cache = writable(join(work, 'trivy-cache'))
      let result
      for (let attempt = 1; attempt <= 3; attempt++) {
        result = docker([
          'run',
          '--rm',
          '--entrypoint',
          'trivy',
          '-e',
          'TRIVY_CACHE_DIR=/trivy-cache',
          '-v',
          `${cache}:/trivy-cache`,
          image('axis-package-host-dotnet'),
          'fs',
          '--download-db-only',
        ])
        if (result.status === 0) break
        if (attempt < 3) await new Promise((resolve) => setTimeout(resolve, attempt * 15_000))
      }
      assert.equal(result.status, 0, `trivy could not download its database\n${result.stdout}${result.stderr}`)
    },
    { timeout },
  )

  // The files of the builds are owned by the user of the image, which the host user cannot delete, so the image's
  // own shell deletes them first.
  after(() => {
    if (!work) return
    docker(
      ['run', '--rm', '--entrypoint', 'sh', '-v', `${work}:/work`, image('axis-package-host-dotnet'), '-c', 'rm -rf /work/*/*'],
      { stdio: 'ignore' },
    )
    rmSync(work, { recursive: true, force: true })
  })

  // Builds the source in the image, with the source read-only, and checks the written package.
  const build = (target, { source, id, marker }) => {
    const extensions = writable(join(work, `${id}-extensions`))
    const unchanged = listing(source)
    const output = run([
      'run',
      '--rm',
      '-e',
      'TRIVY_CACHE_DIR=/trivy-cache',
      '-e',
      'TRIVY_SKIP_DB_UPDATE=true',
      '-v',
      `${cache}:/trivy-cache`,
      '-v',
      `${source}:/source:ro`,
      '-v',
      `${extensions}:/extensions`,
      image(target),
      'build',
      '/source',
    ])
    assert.match(output, new RegExp(`Built ${id} 1\\.0\\.0 in ${id}/1\\.0\\.0/\\.`))
    const manifest = join(extensions, id, '1.0.0', 'axis-package.json')
    assert.ok(existsSync(manifest), `${manifest} was not written`)
    assert.equal(statSync(manifest).uid, packageUid, 'the package is not owned by the image user')
    if (marker) assert.ok(existsSync(join(extensions, id, '1.0.0', marker)), `${marker} was not written`)
    assert.deepEqual(listing(source), unchanged, 'the build changed its source')
  }

  for (const target of packageHosts) {
    test(`${target} runs as UID ${packageUid} and holds Trivy ${trivyVersion}`, { timeout }, () => {
      assert.equal(run(['run', '--rm', '--entrypoint', 'id', image(target), '-u']), String(packageUid))
      assert.match(run(['run', '--rm', '--entrypoint', 'trivy', image(target), '--version']), new RegExp(`^Version: ${trivyVersion.replaceAll('.', '\\.')}$`, 'm'))
    })
  }

  test('axis-package-host-dotnet builds the C# test package from a read-only source', { timeout }, () => {
    const project = 'tests/packages/TestPackage.V1/TestPackage.V1.csproj'
    const built = spawnSync('dotnet', ['build', project, '-c', 'Release'], { cwd: root, encoding: 'utf8' })
    assert.equal(built.status, 0, `${built.stdout}${built.stderr}`)
    const source = join(work, 'dotnet-source')
    copyReadable(join(root, 'tests/packages/TestPackage.V1/bin/Release/net10.0'), source)
    build('axis-package-host-dotnet', { source, id: 'test-package' })
  })

  test('axis-package-host-node builds the TypeScript test package from a read-only source', { timeout }, () => {
    const source = join(work, 'node-source')
    copyReadable(join(root, 'tests/packages/node-test-package'), source, (name) => name === 'node_modules')
    build('axis-package-host-node', {
      source,
      id: 'node-test-package',
      marker: 'node_modules/axis-test-dependency/package.json',
    })
  })

  test('axis-package-host-python builds the Python test package from a read-only source', { timeout }, () => {
    const source = join(work, 'python-source')
    copyReadable(join(root, 'tests/packages/PythonPackage/package'), source)
    const dependency = join(work, 'python-dependency')
    copyReadable(join(root, 'tests/packages/PythonPackage/dependency'), dependency)
    const wheels = writable(join(source, 'wheels'))
    // The wheel is a zip of the dependency, written with the image's own Python as ZipFile.CreateFromDirectory does it
    // in tests/Axis.PackageHost.Tests: each file at its path relative to the folder.
    run([
      'run',
      '--rm',
      '--entrypoint',
      'python3',
      '-v',
      `${dependency}:/dependency:ro`,
      '-v',
      `${wheels}:/wheels`,
      image('axis-package-host-python'),
      '-c',
      [
        'import os, sys, zipfile',
        'with zipfile.ZipFile(sys.argv[1], "w", zipfile.ZIP_DEFLATED) as archive:',
        '    for folder, _, names in os.walk("/dependency"):',
        '        for name in sorted(names):',
        '            path = os.path.join(folder, name)',
        '            archive.write(path, os.path.relpath(path, "/dependency"))',
      ].join('\n'),
      `/wheels/${wheel}`,
    ])
    const hash = createHash('sha256').update(readFileSync(join(wheels, wheel))).digest('hex')
    writeFileSync(join(source, 'requirements.txt'), `./wheels/${wheel} --hash=sha256:${hash}\n`)
    chmodSync(join(source, 'requirements.txt'), 0o644)
    build('axis-package-host-python', { source, id: 'python-test-package', marker: 'axis_fixture_dependency/__init__.py' })
  })
})
