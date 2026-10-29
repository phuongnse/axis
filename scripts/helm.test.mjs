import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
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
  test('both hosts have liveness and readiness probes, the server over HTTPS and the worker on its own address', () => {
    const output = renderOk()
    const server = find(output, 'Deployment', 'axis-server')
    const worker = find(output, 'Deployment', 'axis-worker')

    for (const [document, port, scheme] of [
      [server, 'https', '\\s+scheme: HTTPS'],
      [worker, 'health', ''],
    ]) {
      for (const [probe, path] of [
        ['startupProbe', 'live'],
        ['livenessProbe', 'live'],
        ['readinessProbe', 'ready'],
      ]) {
        assert.match(document, new RegExp(`${probe}:\\n\\s+httpGet:\\n\\s+path: /health/${path}\\n\\s+port: ${port}\\n${scheme}`))
      }
    }
    assert.match(server, /- name: https\n\s+containerPort: 8443/)
    assert.doesNotMatch(server, /containerPort: 8080|port: http\n/)
    assert.match(worker, /- name: health\n\s+containerPort: 5208/)
    assert.doesNotMatch(worker, /scheme:/)
    assert.match(worker, /- name: Health__Urls\n\s+value: http:\/\/0\.0\.0\.0:5208\n/)
    assert.doesNotMatch(server, /Health__Urls/)
  })

  test('the server listens on HTTPS only, with the certificate of its TLS Secret, and no other host does', () => {
    const output = renderOk()
    const server = find(output, 'Deployment', 'axis-server')
    assert.match(server, /- name: Kestrel__Endpoints__Https__Url\n\s+value: https:\/\/\+:8443\n/)
    assert.match(server, /- name: Kestrel__Endpoints__Https__Certificate__Path\n\s+value: \/server-tls\/tls\.crt\n/)
    assert.match(server, /- name: Kestrel__Endpoints__Https__Certificate__KeyPath\n\s+value: \/server-tls\/tls\.key\n/)
    assert.doesNotMatch(server, /Kestrel__Endpoints__Http__|ASPNETCORE_URLS|ASPNETCORE_HTTP_PORTS/)
    assert.match(server, /- name: server-tls\n\s+mountPath: \/server-tls\n\s+readOnly: true\n/)
    assert.match(
      server,
      /- name: server-tls\n\s+secret:\n\s+secretName: "axis-server-tls"\n(\s+#.*\n)?\s+defaultMode: 292\n\s+items:\n\s+- key: tls\.crt\n\s+path: tls\.crt\n\s+- key: tls\.key\n\s+path: tls\.key\n/,
    )
    for (const [kind, name] of [
      ['Deployment', 'axis-worker'],
      ['Deployment', 'axis-publisher'],
      ['Job', 'axis-migrate'],
    ]) {
      assert.doesNotMatch(find(output, kind, name), /server-tls|Kestrel__|axis-server-tls/)
    }
  })

  test('the server Service is port 443 named https and targets the https port', () => {
    const service = find(renderOk(), 'Service', 'axis-server')
    assert.match(service, /ports:\n\s+- name: https\n\s+port: 443\n\s+targetPort: https\n/)
    assert.doesNotMatch(service, /name: http\n/)
  })

  test('a missing server certificate reference fails and names it', () => {
    const result = render('--set', 'secrets.serverCertificate.name=')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /secrets\.serverCertificate\.name/)
  })

  test('a pre-upgrade Job runs migrate with the admin image', () => {
    const job = find(renderOk(), 'Job', 'axis-migrate')
    assert.match(job, /helm\.sh\/hook: pre-upgrade\n/)
    assert.match(job, /image: "axis-admin:[^"]+"/)
    assert.match(job, /args:\n\s+- migrate\n/)
    assert.match(job, /backoffLimit: 0\n/)
  })

  test('the publisher is one Recreate pod with no port, probe or Service and only its two secrets', () => {
    const output = renderOk('--set', 'secrets.scenarioServerConnection.name=')
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

  test('the scenario connection is mounted in the publisher only, and only when it is named', () => {
    const output = renderOk()
    const publisher = find(output, 'Deployment', 'axis-publisher')
    assert.match(publisher, /name: "axis-scenario"\n\s+items:\n\s+- key: "Scenarios__ServerConnectionString"\n\s+path: Scenarios__ServerConnectionString\n/)
    for (const [kind, name] of [
      ['Deployment', 'axis-server'],
      ['Deployment', 'axis-worker'],
      ['Job', 'axis-migrate'],
    ]) {
      assert.doesNotMatch(find(output, kind, name), /Scenarios__ServerConnectionString|axis-scenario/)
    }
    assert.doesNotMatch(renderOk('--set', 'secrets.scenarioServerConnection.name='), /Scenarios__ServerConnectionString/)
  })

  test('studio.tests renders Studio__Tests on the server only', () => {
    const output = renderOk()
    assert.match(find(output, 'Deployment', 'axis-server'), /- name: Studio__Tests\n\s+value: "true"/)
    for (const [kind, name] of [
      ['Deployment', 'axis-worker'],
      ['Deployment', 'axis-publisher'],
      ['Job', 'axis-migrate'],
    ]) {
      assert.doesNotMatch(find(output, kind, name), /Studio__Tests/)
    }
    assert.doesNotMatch(renderOk('--set', 'studio.tests=false'), /Studio__Tests/)
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
      /- name: (ConnectionStrings__|Platform__SecretKey|Admin__ConnectionString|AXIS_OIDC_CLIENT_SECRET|KC_DB_URL|KC_DB_USERNAME|KC_DB_PASSWORD|KC_BOOTSTRAP_ADMIN_CLIENT_SECRET|IdentityProvider__AdminClientSecret)/,
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
      '--set',
      'studio.tests=false',
    )
    assert.doesNotMatch(output, /\/studio|Studio__|- name: studio\n/)
  })

  test('the chart renders no Ingress and the server Service is ClusterIP', () => {
    const output = renderOk()
    assert.doesNotMatch(output, /^kind: Ingress$/m)
    assert.match(find(output, 'Service', 'axis-server'), /^  type: ClusterIP$/m)
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

  test('the identity Deployment runs the pinned Keycloak with start, the theme and the password list read-only', () => {
    const fixture = readFileSync(new URL('../tests/Axis.Integration.Tests/KeycloakFixture.cs', import.meta.url), 'utf8')
    const pinned = fixture.match(/public const string Image = "([^"]+)";/)?.[1]
    assert.ok(pinned, 'KeycloakFixture.Image not found')

    const identity = find(renderOk(), 'Deployment', 'axis-identity')
    assert.match(identity, /replicas: 1\n/)
    assert.match(identity, new RegExp(`image: "${pinned.replaceAll('.', '\\.')}"`))
    assert.match(identity, /args:\n\s+- start\n/)
    assert.match(identity, /exec \/opt\/keycloak\/bin\/kc\.sh "\$@"/)
    assert.match(identity, /- name: theme\n\s+mountPath: \/opt\/keycloak\/themes\n\s+readOnly: true\n/)
    assert.match(identity, /- name: theme\n\s+persistentVolumeClaim:\n\s+claimName: axis-keycloak-theme\n/)
    assert.match(identity, /- name: password-blacklists\n\s+mountPath: \/opt\/keycloak\/data\/password-blacklists\n\s+readOnly: true\n/)
    assert.match(identity, /- name: password-blacklists\n\s+configMap:\n\s+name: axis-password-blacklists\n/)
    assert.match(identity, /- name: KC_HOSTNAME\n\s+value: "https:\/\/id\.example\.com"\n/)
    assert.match(identity, /- name: KC_HTTPS_CERTIFICATE_FILE\n\s+value: \/identity-tls\/tls\.crt\n/)
    assert.match(identity, /- name: KC_HTTPS_CERTIFICATE_KEY_FILE\n\s+value: \/identity-tls\/tls\.key\n/)
    assert.doesNotMatch(identity, /KC_HTTP_ENABLED/)
  })

  test('the identity Deployment has startup, liveness and readiness probes over HTTPS on its management port', () => {
    const output = renderOk()
    const identity = find(output, 'Deployment', 'axis-identity')
    for (const [probe, path] of [
      ['startupProbe', 'started'],
      ['livenessProbe', 'live'],
      ['readinessProbe', 'ready'],
    ]) {
      assert.match(identity, new RegExp(`${probe}:\\n\\s+httpGet:\\n\\s+path: /health/${path}\\n\\s+port: management\\n\\s+scheme: HTTPS\\n`))
    }
    assert.match(identity, /- name: management\n\s+containerPort: 9000\n/)
    assert.match(identity, /- name: KC_HEALTH_ENABLED\n\s+value: "true"\n/)

    const service = find(output, 'Service', 'axis-identity')
    assert.match(service, /ports:\n\s+- name: https\n\s+port: 443\n\s+targetPort: https\n/)
    assert.doesNotMatch(service, /port: 9000|targetPort: management/)
  })

  test('the identity secret files and certificate are mounted in the identity pod only, and in no env entry', () => {
    const output = renderOk()
    const identity = find(output, 'Deployment', 'axis-identity')
    for (const file of ['KC_DB_URL', 'KC_DB_USERNAME', 'KC_DB_PASSWORD', 'IdentityProvider__AdminClientSecret']) {
      assert.match(identity, new RegExp(`- key: "${file}"\\n\\s+path: ${file}\\n`))
    }
    assert.match(identity, /name: "axis-identity"\n/)
    assert.match(identity, /- name: identity-tls\n\s+secret:\n\s+secretName: "axis-identity-tls"\n/)
    assert.match(identity, /- name: identity-tls\n\s+mountPath: \/identity-tls\n\s+readOnly: true\n/)
    assert.doesNotMatch(identity, /ConnectionStrings__|Platform__SecretKey|Scenarios__/)
    assert.doesNotMatch(identity, /- name: (KC_DB_URL|KC_DB_USERNAME|KC_DB_PASSWORD|KC_BOOTSTRAP_ADMIN_CLIENT_SECRET)\n/)
    for (const [kind, name] of [
      ['Deployment', 'axis-server'],
      ['Deployment', 'axis-worker'],
      ['Deployment', 'axis-publisher'],
      ['Job', 'axis-migrate'],
    ]) {
      assert.doesNotMatch(find(output, kind, name), /identity-tls|axis-identity|KC_DB_|IdentityProvider__AdminClientSecret/)
    }
  })

  test('the identity provider trusts forwarded headers only from the listed proxies and networks', () => {
    const identity = find(renderOk(), 'Deployment', 'axis-identity')
    assert.match(identity, /- name: KC_PROXY_HEADERS\n\s+value: xforwarded\n/)
    assert.match(identity, /- name: KC_PROXY_TRUSTED_ADDRESSES\n\s+value: "10\.0\.0\.10,10\.0\.0\.0\/8"\n/)

    const none = find(renderOk('--set', 'forwardedHeaders.knownProxies=null', '--set', 'forwardedHeaders.knownNetworks=null'), 'Deployment', 'axis-identity')
    assert.doesNotMatch(none, /KC_PROXY_/)
  })

  test('the database authority volume mounts only when set', () => {
    const identity = find(renderOk(), 'Deployment', 'axis-identity')
    assert.match(identity, /- name: db-ca\n\s+mountPath: \/db-ca\n\s+readOnly: true\n/)
    const without = find(renderOk('--set', 'identity.databaseAuthority.volume=null'), 'Deployment', 'axis-identity')
    assert.doesNotMatch(without, /db-ca/)
  })

  for (const [value, set] of [
    ['identity.hostname', 'identity.hostname='],
    ['secrets.identityCertificate.name', 'secrets.identityCertificate.name='],
    ['identity.theme.volume', 'identity.theme.volume=null'],
    ['identity.passwordBlacklists.volume', 'identity.passwordBlacklists.volume=null'],
    ['secrets.identityDatabaseUrl.name', 'secrets.identityDatabaseUrl.name='],
    ['secrets.identityAdminClientSecret.name', 'secrets.identityAdminClientSecret.name='],
  ]) {
    test(`a missing ${value} fails and names it`, () => {
      const result = render('--set', set)
      assert.notEqual(result.status, 0)
      assert.match(result.stderr, new RegExp(value.replaceAll('.', '\\.')))
    })
  }
})
