import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Renders deploy/helm/axis with `helm template`. scripts/test.sh sets HELM to the pinned binary
// that scripts/setup.sh installs. No YAML parser is available, so the tests split the output
// into documents and match them with regular expressions.
const chart = fileURLToPath(new URL('../deploy/helm/axis', import.meta.url))
const testValues = fileURLToPath(new URL('../deploy/helm/axis/ci/test-values.yaml', import.meta.url))

function render(...extra) {
  return spawnSync(process.env.HELM ?? 'helm', ['template', 'axis', chart, '-f', testValues, ...extra], {
    encoding: 'utf8',
  })
}

function renderOk(...extra) {
  const result = render(...extra)
  assert.equal(result.status, 0, result.stderr)
  return result.stdout
}

function documents(output) {
  return output.split(/^---$/m).filter((document) => /^kind:/m.test(document))
}

function find(output, kind, name) {
  const found = documents(output).find(
    (document) => new RegExp(`^kind: ${kind}$`, 'm').test(document) && new RegExp(`^  name: ${name}$`, 'm').test(document),
  )
  assert.ok(found, `no ${kind} named ${name}`)
  return found
}

describe('the Axis Helm chart', () => {
  test('both hosts have liveness and readiness probes, the worker on its own address', () => {
    const output = renderOk()
    const server = find(output, 'Deployment', 'axis-server')
    const worker = find(output, 'Deployment', 'axis-worker')

    for (const [document, port] of [
      [server, 'http'],
      [worker, 'health'],
    ]) {
      assert.match(document, new RegExp(`livenessProbe:\\n\\s+httpGet:\\n\\s+path: /health/live\\n\\s+port: ${port}\\n`))
      assert.match(document, new RegExp(`readinessProbe:\\n\\s+httpGet:\\n\\s+path: /health/ready\\n\\s+port: ${port}\\n`))
    }
    assert.match(server, /- name: http\n\s+containerPort: 8080/)
    assert.match(worker, /- name: health\n\s+containerPort: 5208/)
    assert.match(worker, /- name: Health__Urls\n\s+value: http:\/\/0\.0\.0\.0:5208\n/)
    assert.doesNotMatch(server, /Health__Urls/)
  })

  test('a pre-upgrade Job runs migrate with the admin image', () => {
    const job = find(renderOk(), 'Job', 'axis-migrate')
    assert.match(job, /helm\.sh\/hook: pre-upgrade\n/)
    assert.match(job, /image: "axis-admin:[^"]+"/)
    assert.match(job, /args:\n\s+- migrate\n/)
    assert.match(job, /backoffLimit: 0\n/)
  })

  test('the publisher is one Recreate pod with no port, probe or Service and only its two secrets', () => {
    const output = renderOk()
    const publisher = find(output, 'Deployment', 'axis-publisher')
    assert.match(publisher, /replicas: 1\n/)
    assert.match(publisher, /type: Recreate\n/)
    assert.match(publisher, /terminationGracePeriodSeconds: 360\n/)
    assert.match(publisher, /image: "axis-admin:[^"]+"/)
    assert.match(publisher, /args:\n\s+- publisher\n/)
    assert.match(publisher, /path: ConnectionStrings__PlatformSchema\n/)
    assert.match(publisher, /path: Platform__SecretKey\n/)
    assert.doesNotMatch(publisher, /ports:|containerPort/)
    assert.doesNotMatch(publisher, /Probe:/)
    assert.doesNotMatch(publisher, /path: ConnectionStrings__Platform\n/)
    assert.doesNotMatch(publisher, /Admin__ConnectionString/)
    assert.doesNotMatch(publisher, /\/studio|Studio__/)
    for (const service of documents(output).filter((d) => /^kind: Service$/m.test(d))) {
      assert.doesNotMatch(service, /component: publisher/)
    }
  })

  test('the publisher grace period is a value', () => {
    const publisher = find(renderOk('--set', 'publisher.terminationGracePeriodSeconds=420'), 'Deployment', 'axis-publisher')
    assert.match(publisher, /terminationGracePeriodSeconds: 420\n/)
  })

  test('the image tag and registry apply to every host', () => {
    const output = renderOk('--set', 'image.registry=registry.example.com/axis', '--set', 'image.tag=1.2.3')
    for (const image of ['axis-server', 'axis-worker', 'axis-admin']) {
      assert.match(output, new RegExp(`image: "registry\\.example\\.com/axis/${image}:1\\.2\\.3"`))
    }
  })

  test('no Secret object and no secret setting in any env entry', () => {
    const output = renderOk()
    assert.doesNotMatch(output, /^kind: Secret$/m)
    assert.doesNotMatch(output, /^\s*(stringData|data):/m)
    assert.doesNotMatch(
      output,
      /- name: (ConnectionStrings__|Platform__SecretKey|Admin__ConnectionString|AXIS_OIDC_CLIENT_SECRET)/,
    )

    const worker = find(output, 'Deployment', 'axis-worker')
    assert.doesNotMatch(worker, /ConnectionStrings__PlatformSchema/)
    assert.match(worker, /path: ConnectionStrings__Platform\n/)
    assert.match(worker, /path: Platform__SecretKey\n/)

    const server = find(output, 'Deployment', 'axis-server')
    assert.match(server, /path: ConnectionStrings__PlatformSchema\n/)

    const job = find(output, 'Job', 'axis-migrate')
    assert.match(job, /path: ConnectionStrings__PlatformSchema\n/)
    assert.match(job, /path: Platform__SecretKey\n/)
    assert.doesNotMatch(job, /path: ConnectionStrings__Platform\n/)
    assert.doesNotMatch(job, /Admin__ConnectionString/)
  })

  test('a missing installation key reference fails and names it', () => {
    const result = render('--set', 'secrets.platformSecretKey.name=')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /secrets\.platformSecretKey\.name/)
    assert.match(result.stderr, /Platform__SecretKey/)
  })

  test('a value inside a secret reference is rejected', () => {
    const result = render('--set', 'secrets.platformSecretKey.value=leak')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /value/)
    assert.doesNotMatch(result.stdout, /leak/)
  })

  test('the extension and module volumes mount only when set', () => {
    const output = renderOk('--set', 'extensions.volume=null', '--set', 'modules.volume=null')
    for (const document of documents(output).filter((d) => /^kind: Deployment$/m.test(d))) {
      assert.doesNotMatch(document, /mountPath: \/(extensions|modules)\n/)
      assert.doesNotMatch(document, /- name: (extensions|modules)\n/)
    }
    const server = find(renderOk(), 'Deployment', 'axis-server')
    assert.match(server, /mountPath: \/extensions\n\s+readOnly: true/)
    assert.match(server, /mountPath: \/modules\n\s+readOnly: true/)
  })

  test('several server replicas share one Studio volume', () => {
    const output = renderOk()
    const server = find(output, 'Deployment', 'axis-server')
    assert.match(server, /replicas: 2\n/)
    assert.match(server, /- name: studio\n\s+persistentVolumeClaim:\n\s+claimName: axis-studio\n/)
    assert.match(server, /- name: studio\n\s+mountPath: \/studio\n(?!\s+readOnly)/)
    assert.match(server, /- name: Studio__Folders__0\n\s+value: "\/studio\/purchase-requests"/)
    assert.match(server, /- name: Studio__AuthorRole\n\s+value: "author"/)
    assert.match(server, /- name: Studio__PublisherRole\n\s+value: "publisher"/)
    for (const [kind, name] of [
      ['Deployment', 'axis-worker'],
      ['Deployment', 'axis-publisher'],
      ['Job', 'axis-migrate'],
    ]) {
      const document = find(output, kind, name)
      assert.doesNotMatch(document, /\/studio/)
      assert.doesNotMatch(document, /Studio__/)
    }
  })

  test('Studio folders without a volume fail and name studio.persistence', () => {
    const result = render('--set', 'studio.persistence=null')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /studio\.persistence/)
  })

  test('without Studio folders the server mounts no Studio volume', () => {
    const output = renderOk(
      '--set',
      'studio.folders=null',
      '--set',
      'studio.persistence=null',
      '--set',
      'studio.authorRole=',
      '--set',
      'studio.publisherRole=',
    )
    assert.doesNotMatch(output, /\/studio|Studio__|- name: studio\n/)
  })

  test('forwarded headers render on the server only', () => {
    const output = renderOk()
    const server = find(output, 'Deployment', 'axis-server')
    assert.match(server, /- name: ForwardedHeaders__KnownProxies__0\n\s+value: "10\.0\.0\.10"/)
    assert.match(server, /- name: ForwardedHeaders__KnownNetworks__0\n\s+value: "10\.0\.0\.0\/8"/)
    for (const [kind, name] of [
      ['Deployment', 'axis-worker'],
      ['Deployment', 'axis-publisher'],
      ['Job', 'axis-migrate'],
    ]) {
      assert.doesNotMatch(find(output, kind, name), /ForwardedHeaders__/)
    }
  })
})
