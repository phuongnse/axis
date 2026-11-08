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

  test('the agent host has startup, liveness and readiness probes on its health port and mounts no secret', () => {
    const agent = find(renderOk(), 'Deployment', 'axis-agent')
    for (const [probe, path] of [
      ['startupProbe', 'live'],
      ['livenessProbe', 'live'],
      ['readinessProbe', 'ready'],
    ]) {
      assert.match(agent, new RegExp(`${probe}:\\n\\s+httpGet:\\n\\s+path: /health/${path}\\n\\s+port: health\\n`))
    }
    assert.match(agent, /- name: health\n\s+containerPort: 5209/)
    assert.doesNotMatch(agent, /scheme:/)
    assert.match(agent, /- name: Health__Urls\n\s+value: http:\/\/0\.0\.0\.0:5209\n/)
    assert.doesNotMatch(agent, /projected:|secretName:|\/run\/secrets|ConnectionStrings__|Platform__SecretKey|serviceAccountName/)
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

  test('outside a cell the publisher is one Recreate pod with no port, probe or Service and only its two secrets', () => {
    const output = renderOk(
      '--set',
      'secrets.scenarioServerConnection.name=',
      '--set',
      'cell.id=',
      '--set',
      'cell.certificate.enabled=false',
      '--set',
      'cell.pgbouncer.enabled=false',
      '--set',
      'studio.folders=null',
      '--set',
      'studio.persistence=null',
    )
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
    assert.doesNotMatch(publisher, /\/studio|Studio__|mountPath: \/templates|Templates__/)
    assert.doesNotMatch(publisher, /IdentityProvider__|axis-identity|- name: cell-backup-key|axis-cell-backup-key|axis-admin-db/)
    assert.doesNotMatch(publisher, /- name: (studio|templates)\n/)
    for (const service of documents(output).filter((d) => /^kind: Service$/m.test(d))) {
      assert.doesNotMatch(service, /component: publisher/)
    }
  })

  test('outside a cell the publisher mounts the Studio volume read-write and gets the folders and the platform connection when studio.folders is set', () => {
    const publisher = find(
      renderOk(
        '--set',
        'secrets.scenarioServerConnection.name=',
        '--set',
        'cell.id=',
        '--set',
        'cell.certificate.enabled=false',
        '--set',
        'cell.pgbouncer.enabled=false',
      ),
      'Deployment',
      'axis-publisher',
    )
    assert.match(publisher, /- name: studio\n\s+mountPath: \/studio\n(?!\s+readOnly)/)
    assert.match(publisher, /- name: studio\n\s+persistentVolumeClaim:\n\s+claimName: axis-studio\n/)
    assert.match(publisher, /- name: Studio__Folders__0\n\s+value: "\/studio\/purchase-requests"/)
    assert.match(publisher, /- key: "ConnectionStrings__Platform"\n\s+path: ConnectionStrings__Platform\n/)
    assert.doesNotMatch(publisher, /Studio__ApplicationsPath|Studio__AuthorRole|Studio__PublisherRole|Studio__Tests/)
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
    for (const image of ['axis-server', 'axis-worker', 'axis-agent', 'axis-admin']) {
      assert.match(output, new RegExp(`image: "registry\\.example\\.com/axis/${image}:1\\.2\\.3"`))
    }
  })

  test('no Secret object and no secret setting in any env entry', () => {
    const output = renderOk()
    assert.doesNotMatch(output, /^kind: Secret$/m)
    assert.doesNotMatch(output, /^\s*stringData:/m)
    // A ConfigMap holds data. Every other kind may not.
    for (const document of documents(output).filter((d) => !/^kind: ConfigMap$/m.test(d))) {
      assert.doesNotMatch(document, /^\s*data:/m)
    }
    assert.doesNotMatch(
      output,
      /- name: (ConnectionStrings__|Platform__SecretKey|Admin__ConnectionString|AXIS_OIDC_CLIENT_SECRET|KC_DB_URL|KC_DB_USERNAME|KC_DB_PASSWORD|KC_BOOTSTRAP_ADMIN_CLIENT_SECRET|IdentityProvider__AdminClientSecret|Cell__BackupStorage__AccessKeyId|Cell__BackupStorage__SecretAccessKey|IdentityProvider__DatabaseConnectionString)/,
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
      'cell.id=',
      '--set',
      'cell.certificate.enabled=false',
      '--set',
      'cell.pgbouncer.enabled=false',
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
    // The restore-check CronJob always has a throwaway Studio folder, which is not the server's volume.
    const rest = documents(output).filter((document) => !/^  name: axis-cell-restore-check$/m.test(document))
    assert.doesNotMatch(rest.join('\n'), /\/studio|Studio__|- name: studio\n/)
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

  test('the identity secret files and certificate are mounted in the identity pod, and the publisher and the restore-check CronJob get the client secret as a file', () => {
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
      ['Job', 'axis-migrate'],
    ]) {
      assert.doesNotMatch(find(output, kind, name), /identity-tls|axis-identity|KC_DB_|IdentityProvider__AdminClientSecret/)
    }
    // In a cell the publisher gets the client secret as a file, and nothing else of the identity provider's.
    assert.doesNotMatch(find(output, 'Deployment', 'axis-publisher'), /identity-tls|KC_DB_/)
    assert.doesNotMatch(find(output, 'CronJob', 'axis-cell-backup'), /identity-tls|name: "axis-identity"\n|KC_DB_|IdentityProvider__AdminClientSecret/)
    const restoreCheck = find(output, 'CronJob', 'axis-cell-restore-check')
    assert.match(restoreCheck, /- key: "IdentityProvider__AdminClientSecret"\n\s+path: IdentityProvider__AdminClientSecret\n/)
    assert.doesNotMatch(restoreCheck, /identity-tls|KC_DB_/)
    assert.doesNotMatch(output, /- name: IdentityProvider__AdminClientSecret\n/)
    // The publisher alone runs the create job, so it alone gets the admin connection.
    for (const document of documents(output).filter((d) => /^kind: (Deployment|Job)$/m.test(d) && !/^  name: axis-publisher$/m.test(d))) {
      assert.doesNotMatch(document, /Admin__ConnectionString|axis-admin-db/)
    }
  })

  test('the identity provider trusts forwarded headers only from the listed proxies and networks', () => {
    const identity = find(renderOk(), 'Deployment', 'axis-identity')
    assert.match(identity, /- name: KC_PROXY_HEADERS\n\s+value: xforwarded\n/)
    assert.match(identity, /- name: KC_PROXY_TRUSTED_ADDRESSES\n\s+value: "10\.0\.0\.10,10\.0\.0\.0\/8"\n/)

    const none = find(renderOk('--set', 'forwardedHeaders.knownProxies=null', '--set', 'forwardedHeaders.knownNetworks=null'), 'Deployment', 'axis-identity')
    assert.doesNotMatch(none, /KC_PROXY_/)
  })

  test('the cell values render Cell__ variables on the server, the worker and the publisher, and not on the migrate Job', () => {
    const output = renderOk()
    for (const name of ['axis-server', 'axis-worker', 'axis-publisher']) {
      const host = find(output, 'Deployment', name)
      assert.match(host, /- name: Cell__Id\n\s+value: "vn-1"\n/, name)
      assert.match(host, /- name: Cell__Region\n\s+value: "vn"\n/, name)
      assert.match(host, /- name: Cell__TenantCap\n\s+value: "500"\n/, name)
      assert.match(host, /- name: Cell__ConsoleAllowedNetworks__0\n\s+value: "192\.168\.0\.0\/16"\n/, name)
    }
    assert.doesNotMatch(find(output, 'Job', 'axis-migrate'), /Cell__/)
  })

  test('empty cell values render no Cell__ variable anywhere', () => {
    const output = renderOk(
      ...[
        'id',
        'region',
        'tenantDomain',
        'internalDomain',
        'tenantCap',
        'consoleHost',
        'internalUrl',
        'operatorRealm',
        'operatorRole',
        'backupKeyFile',
      ].flatMap((key) => ['--set', `cell.${key}=`]),
      '--set',
      'cell.consoleAllowedNetworks=null',
      '--set',
      'cell.certificate.enabled=false',
      '--set',
      'cell.pgbouncer.enabled=false',
    )
    assert.doesNotMatch(output, /Cell__/)
  })

  test('the Certificate names the wildcard of the tenant domain, its Secret and the Issuer', () => {
    const certificate = find(renderOk(), 'Certificate', 'axis-tenant-subdomains')
    assert.match(certificate, /apiVersion: cert-manager\.io\/v1\n/)
    assert.match(certificate, /dnsNames:\n\s+- "\*\.vn\.example\.com"\n/)
    assert.match(certificate, /secretName: "axis-tenants-tls"\n/)
    assert.match(certificate, /issuerRef:\n\s+name: axis-tenant-subdomains\n\s+kind: Issuer\n\s+group: cert-manager\.io\n/)
  })

  test('the Issuer has one DNS-01 solver whose token is a Secret reference', () => {
    const issuer = find(renderOk(), 'Issuer', 'axis-tenant-subdomains')
    assert.match(issuer, /apiVersion: cert-manager\.io\/v1\n/)
    assert.match(issuer, /server: "https:\/\/acme-v02\.api\.letsencrypt\.org\/directory"\n/)
    assert.match(issuer, /email: "ops@example\.com"\n/)
    assert.match(issuer, /privateKeySecretRef:\n\s+name: axis-tenant-subdomains-acme-account\n/)
    assert.match(issuer, /dns01:\n\s+cloudflare:\n\s+apiTokenSecretRef:\n\s+name: "axis-dns"\n\s+key: "api-token"\n/)
    assert.equal(issuer.match(/- dns01:/g).length, 1)

    const digitalocean = find(renderOk('--set', 'cell.certificate.issuer.dns01.provider=digitalocean'), 'Issuer', 'axis-tenant-subdomains')
    assert.match(digitalocean, /dns01:\n\s+digitalocean:\n\s+tokenSecretRef:\n\s+name: "axis-dns"\n\s+key: "api-token"\n/)
    assert.doesNotMatch(digitalocean, /cloudflare/)
  })

  test('the Issuer omits the email when it is empty', () => {
    const issuer = find(renderOk('--set', 'cell.certificate.issuer.email='), 'Issuer', 'axis-tenant-subdomains')
    assert.doesNotMatch(issuer, /email:/)
  })

  test('the host Issuer has one HTTP-01 solver, with the ingress class when set and an empty ingress otherwise', () => {
    const issuer = find(renderOk(), 'Issuer', 'axis-tenant-hosts')
    assert.match(issuer, /apiVersion: cert-manager\.io\/v1\n/)
    assert.match(issuer, /server: "https:\/\/acme-v02\.api\.letsencrypt\.org\/directory"\n/)
    assert.match(issuer, /email: "ops@example\.com"\n/)
    assert.match(issuer, /privateKeySecretRef:\n\s+name: axis-tenant-hosts-acme-account\n/)
    assert.match(issuer, /- http01:\n\s+ingress:\n\s+ingressClassName: "nginx"\n/)
    assert.equal(issuer.match(/- http01:/g).length, 1)
    assert.doesNotMatch(issuer, /dns01/)

    const unclassed = find(renderOk('--set', 'cell.certificate.http01.ingressClassName='), 'Issuer', 'axis-tenant-hosts')
    assert.match(unclassed, /- http01:\n\s+ingress: \{\}\n/)
    assert.doesNotMatch(unclassed, /ingressClassName/)
    assert.doesNotMatch(find(renderOk('--set', 'cell.certificate.issuer.email='), 'Issuer', 'axis-tenant-hosts'), /email:/)
  })

  test('the publisher may only create certificates.cert-manager.io in the release namespace', () => {
    const output = renderOk('--namespace', 'axis')
    const role = find(output, 'Role', 'axis-publisher-certificates')
    assert.match(role, /namespace: axis\n/)
    assert.match(role, /rules:\n\s+- apiGroups:\n\s+- cert-manager\.io\n\s+resources:\n\s+- certificates\n\s+verbs:\n\s+- create\n/)
    assert.equal(role.match(/- apiGroups:/g).length, 1)
    assert.equal(role.match(/verbs:/g).length, 1)
    const binding = find(output, 'RoleBinding', 'axis-publisher-certificates')
    assert.match(binding, /namespace: axis\n/)
    assert.match(binding, /roleRef:\n\s+apiGroup: rbac\.authorization\.k8s\.io\n\s+kind: Role\n\s+name: axis-publisher-certificates\n/)
    assert.match(binding, /subjects:\n\s+- kind: ServiceAccount\n\s+name: axis-publisher\n\s+namespace: axis\n/)
    assert.doesNotMatch(output, /^kind: (ClusterRole|ClusterRoleBinding)$/m)
  })

  test('with the certificate on, the publisher runs as its own service account with the token and the issuer name, and no other pod does', () => {
    const output = renderOk()
    const publisher = find(output, 'Deployment', 'axis-publisher')
    assert.match(publisher, /serviceAccountName: axis-publisher\n\s+automountServiceAccountToken: true\n/)
    assert.match(publisher, /- name: Cell__HostCertificateIssuer\n\s+value: "axis-tenant-hosts"\n/)
    assert.match(find(output, 'ServiceAccount', 'axis-publisher'), /automountServiceAccountToken: false\n/)
    for (const name of ['axis-server', 'axis-worker', 'axis-identity']) {
      const document = find(output, 'Deployment', name)
      assert.doesNotMatch(document, /serviceAccountName|automountServiceAccountToken: true|Cell__HostCertificateIssuer/, name)
    }
  })

  test('an inline token under cell.certificate.issuer fails rendering and never appears in the output', () => {
    for (const path of ['cell.certificate.issuer.token', 'cell.certificate.issuer.dns01.token']) {
      const result = render('--set', `${path}=leak`)
      assert.notEqual(result.status, 0, path)
      assert.match(result.stderr, /token/, path)
      assert.doesNotMatch(result.stdout, /leak/, path)
    }
  })

  test('a DNS provider that the chart does not know fails rendering', () => {
    const result = render('--set', 'cell.certificate.issuer.dns01.provider=route53')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /provider/)
  })

  test('with the certificate off, no Issuer, Certificate, ServiceAccount, Role or RoleBinding renders and the publisher has no token or issuer name', () => {
    const output = renderOk('--set', 'cell.certificate.enabled=false')
    assert.doesNotMatch(output, /^kind: (Issuer|Certificate|ServiceAccount|Role|RoleBinding)$/m)
    const publisher = find(output, 'Deployment', 'axis-publisher')
    assert.match(publisher, /automountServiceAccountToken: false\n/)
    assert.doesNotMatch(publisher, /serviceAccountName|automountServiceAccountToken: true|Cell__HostCertificateIssuer/)
    for (const name of ['axis-server', 'axis-worker']) {
      assert.doesNotMatch(find(output, 'Deployment', name), /Cell__HostCertificateIssuer/, name)
    }
  })

  test('with the certificate on, an empty cell.tenantDomain fails and names it', () => {
    const result = render('--set', 'cell.tenantDomain=')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /cell\.tenantDomain/)
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
    ['cell.certificate.issuer.dns01.tokenSecret.name', 'cell.certificate.issuer.dns01.tokenSecret.name='],
    ['cell.certificate.secretName', 'cell.certificate.secretName='],
    ['cell.certificate.issuer.server', 'cell.certificate.issuer.server='],
    ['secrets.pgbouncerAuth.name', 'secrets.pgbouncerAuth.name='],
    ['secrets.pgbouncerCertificate.name', 'secrets.pgbouncerCertificate.name='],
    ['cell.pgbouncer.databaseHost', 'cell.pgbouncer.databaseHost='],
    ['cell.pgbouncer.authUser', 'cell.pgbouncer.authUser='],
    ['backup.timeZone', 'backup.timeZone='],
    ['backup.serviceUrl', 'backup.serviceUrl='],
    ['backup.region', 'backup.region='],
    ['secrets.backupStorageAccessKeyId.name', 'secrets.backupStorageAccessKeyId.name='],
    ['secrets.backupStorageSecretAccessKey.name', 'secrets.backupStorageSecretAccessKey.name='],
    ['secrets.cellBackupKey.name', 'secrets.cellBackupKey.name='],
    ['secrets.identityCredentialsConnection.name', 'secrets.identityCredentialsConnection.name='],
    ['secrets.adminConnection.name', 'secrets.adminConnection.name='],
    ['templates.volume', 'templates.volume=null'],
    ['studio.persistence', 'studio.persistence=null'],
    ['cell.internalDomain', 'cell.internalDomain='],
  ]) {
    test(`a missing ${value} fails and names it`, () => {
      const result = render('--set', set)
      assert.notEqual(result.status, 0)
      assert.match(result.stderr, new RegExp(value.replaceAll('.', '\\.')))
    })
  }

  test('in a cell the publisher gets what the create job reads', () => {
    const publisher = find(renderOk(), 'Deployment', 'axis-publisher')
    assert.match(publisher, /- secret:\n\s+name: "axis-admin-db"\n\s+items:\n\s+- key: "Admin__ConnectionString"\n\s+path: Admin__ConnectionString\n/)
    assert.match(publisher, /- secret:\n\s+name: "axis-identity"\n\s+items:\n\s+- key: "IdentityProvider__AdminClientSecret"\n\s+path: IdentityProvider__AdminClientSecret\n/)
    assert.doesNotMatch(publisher, /- name: (Admin__ConnectionString|IdentityProvider__AdminClientSecret)\n/)
    assert.equal(publisher.match(/- name: Cell__BackupKeyFile\n/g).length, 1)
    assert.match(publisher, /- name: Cell__BackupKeyFile\n\s+value: "\/cell-backup-key\/key"\n/)
    assert.match(publisher, /- name: cell-backup-key\n\s+mountPath: \/cell-backup-key\n\s+readOnly: true\n/)
    assert.match(publisher, /secretName: "axis-cell-backup-key"\n\s+defaultMode: 292\n\s+items:\n\s+- key: "cell-backup-key"\n\s+path: key\n/)
    assert.match(publisher, /- name: IdentityProvider__Url\n\s+value: "https:\/\/id\.example\.com\/"\n/)
    assert.match(publisher, /- name: IdentityProvider__AdminClientId\n\s+value: axis-admin\n/)
    assert.match(publisher, /- name: Cell__TenantDomain\n\s+value: "vn\.example\.com"\n/)
    assert.match(publisher, /- name: Studio__ApplicationsPath\n\s+value: \/studio\n/)
    assert.match(publisher, /path: ConnectionStrings__Platform\n/)
    assert.match(publisher, /- name: studio\n\s+mountPath: \/studio\n(?!\s+readOnly)/)
    assert.match(publisher, /- name: studio\n\s+persistentVolumeClaim:\n\s+claimName: axis-studio\n/)
    assert.match(publisher, /- name: Templates__Path\n\s+value: \/templates\n/)
    assert.match(publisher, /- name: templates\n\s+mountPath: \/templates\n\s+readOnly: true\n/)
    assert.match(publisher, /- name: templates\n\s+persistentVolumeClaim:\n\s+claimName: axis-templates\n/)
  })

  test("the installation doc's publisher row of 'Which pod gets which secret' lists the secrets the chart gives the publisher", () => {
    const keys = [...readFileSync(new URL('../deploy/helm/axis/values.yaml', import.meta.url), 'utf8').matchAll(/^  ([a-z][A-Za-z]+):\n    name:/gm)].map((match) => match[1])
    assert.ok(keys.length > 10, 'no secrets in values.yaml')
    const publisher = find(renderOk(...keys.flatMap((key) => ['--set', `secrets.${key}.name=named-${key}`])), 'Deployment', 'axis-publisher')
    const rendered = [...new Set([...publisher.matchAll(/(?:secretName|name): "named-([A-Za-z]+)"/g)].map((match) => match[1]))].sort()

    const doc = readFileSync(new URL('../docs/reference/installation.md', import.meta.url), 'utf8')
    const row = doc.split('\n').find((line) => line.startsWith('| Publisher | '))
    assert.ok(row, 'no Publisher row in the secrets table')
    const listed = [...new Set([...row.matchAll(/`([a-z][A-Za-z]+)`/g)].map((match) => match[1]).filter((name) => keys.includes(name)))].sort()
    assert.ok(rendered.length > 0, 'the publisher mounts no secret')
    assert.deepEqual(listed, rendered)
  })

  test('in a cell the server and the publisher mount the same Studio volume and use /studio as the applications folder', () => {
    const output = renderOk('--set', 'studio.folders=null')
    for (const name of ['axis-server', 'axis-publisher']) {
      const host = find(output, 'Deployment', name)
      assert.match(host, /- name: Studio__ApplicationsPath\n\s+value: \/studio\n/, name)
      assert.match(host, /- name: studio\n\s+mountPath: \/studio\n(?!\s+readOnly)/, name)
      assert.match(host, /- name: studio\n\s+persistentVolumeClaim:\n\s+claimName: axis-studio\n/, name)
    }
    assert.doesNotMatch(find(output, 'Deployment', 'axis-server'), /Studio__Folders__/)
  })

  test('in a cell the worker and the migrate Job get none of the create job settings', () => {
    const output = renderOk()
    for (const [kind, name] of [
      ['Deployment', 'axis-worker'],
      ['Job', 'axis-migrate'],
    ]) {
      assert.doesNotMatch(
        find(output, kind, name),
        /IdentityProvider__|Studio__ApplicationsPath|Templates__Path|mountPath: \/(studio|templates)|mountPath: \/cell-backup-key|axis-cell-backup-key|Admin__ConnectionString/,
        name,
      )
    }
  })

  test('the server in a cell gets no templates volume', () => {
    assert.doesNotMatch(find(renderOk(), 'Deployment', 'axis-server'), /Templates__Path|mountPath: \/templates|axis-templates/)
  })

  test('with PgBouncer on, the server and the worker send runtime connections to its Service, and no other pod does', () => {
    const output = renderOk()
    for (const name of ['axis-server', 'axis-worker']) {
      const host = find(output, 'Deployment', name)
      assert.match(host, /- name: Cell__RuntimeDatabaseHost\n\s+value: "axis-pgbouncer"\n/, name)
      assert.match(host, /- name: Cell__RuntimeDatabasePort\n\s+value: "6432"\n/, name)
    }
    for (const [kind, name] of [
      ['Deployment', 'axis-publisher'],
      ['Job', 'axis-migrate'],
      ['Deployment', 'axis-identity'],
    ]) {
      assert.doesNotMatch(find(output, kind, name), /Cell__RuntimeDatabase/, name)
    }
  })

  test('the PgBouncer ConfigMap pools in transaction mode, with the pool sizes, a wildcard database and TLS on both legs', () => {
    const config = find(renderOk(), 'ConfigMap', 'axis-pgbouncer')
    for (const line of [
      '* = host=db.internal.example.com port=5432',
      'pool_mode = transaction',
      'default_pool_size = 3',
      'max_db_connections = 10',
      'max_client_conn = 10000',
      'auth_user = axis_pgbouncer',
      'auth_dbname = postgres',
      'auth_query = SELECT uname, phash FROM pgbouncer.user_lookup($1)',
      'client_tls_sslmode = require',
      'client_tls_cert_file = /pgbouncer-tls/tls.crt',
      'client_tls_key_file = /pgbouncer-tls/tls.key',
      'server_tls_sslmode = verify-full',
      'server_tls_ca_file = /db-ca/root.crt',
    ]) {
      assert.match(config, new RegExp(`^    ${line.replaceAll(/[$*.()]/g, '\\$&')}$`, 'm'), line)
    }
  })

  test('the PgBouncer pool sizes and database port are values', () => {
    const config = find(
      renderOk(
        '--set', 'cell.pgbouncer.defaultPoolSize=4',
        '--set', 'cell.pgbouncer.maxDbConnections=12',
        '--set', 'cell.pgbouncer.maxClientConn=500',
        '--set', 'cell.pgbouncer.databasePort=6000',
      ),
      'ConfigMap',
      'axis-pgbouncer',
    )
    assert.match(config, /default_pool_size = 4\n/)
    assert.match(config, /max_db_connections = 12\n/)
    assert.match(config, /max_client_conn = 500\n/)
    assert.match(config, /port=6000\n/)
  })

  test('the PgBouncer Deployment is one non-root pod with a read-only root file system and its two Secrets', () => {
    const deployment = find(renderOk(), 'Deployment', 'axis-pgbouncer')
    assert.match(deployment, /replicas: 1\n/)
    assert.match(deployment, /image: "edoburu\/pgbouncer:v1\.24\.1-p1"/)
    assert.match(deployment, /runAsNonRoot: true\n/)
    assert.match(deployment, /runAsUser: 70\n\s+runAsGroup: 70\n/)
    assert.match(deployment, /readOnlyRootFilesystem: true\n/)
    assert.match(deployment, /command:\n\s+- \/usr\/bin\/pgbouncer\n\s+- \/etc\/pgbouncer\/pgbouncer\.ini\n/)
    assert.match(deployment, /- name: pgbouncer\n\s+containerPort: 6432\n/)
    assert.match(deployment, /readinessProbe:\n\s+tcpSocket:\n\s+port: pgbouncer\n/)
    assert.match(deployment, /livenessProbe:\n\s+tcpSocket:\n\s+port: pgbouncer\n/)
    assert.match(deployment, /checksum\/config: [0-9a-f]{64}\n/)
    assert.match(deployment, /- configMap:\n\s+name: axis-pgbouncer\n\s+items:\n\s+- key: pgbouncer\.ini\n\s+path: pgbouncer\.ini\n/)
    assert.match(deployment, /- secret:\n\s+name: "axis-pgbouncer-auth"\n\s+items:\n\s+- key: "userlist\.txt"\n\s+path: userlist\.txt\n/)
    assert.match(deployment, /- name: pgbouncer-tls\n\s+secret:\n\s+secretName: "axis-pgbouncer-tls"\n(\s+#.*\n)?\s+defaultMode: 292\n/)
    assert.match(deployment, /- name: pgbouncer-tls\n\s+mountPath: \/pgbouncer-tls\n\s+readOnly: true\n/)
    assert.match(deployment, /- name: db-ca\n\s+mountPath: \/db-ca\n\s+readOnly: true\n/)
  })

  test('the PgBouncer Service is port 6432 named pgbouncer', () => {
    const service = find(renderOk(), 'Service', 'axis-pgbouncer')
    assert.match(service, /^  type: ClusterIP$/m)
    assert.match(service, /ports:\n\s+- name: pgbouncer\n\s+port: 6432\n\s+targetPort: pgbouncer\n/)
    assert.match(service, /app\.kubernetes\.io\/component: pgbouncer\n\s+ports:/)
  })

  test('the PgBouncer database authority is used only when set', () => {
    const output = renderOk('--set', 'cell.pgbouncer.databaseAuthority.volume=null')
    assert.doesNotMatch(find(output, 'ConfigMap', 'axis-pgbouncer'), /server_tls_ca_file/)
    assert.match(find(output, 'ConfigMap', 'axis-pgbouncer'), /server_tls_sslmode = verify-full/)
    assert.doesNotMatch(find(output, 'Deployment', 'axis-pgbouncer'), /db-ca/)
  })

  test('with PgBouncer off, no PgBouncer object and no Cell__RuntimeDatabase variable renders', () => {
    const output = renderOk('--set', 'cell.pgbouncer.enabled=false')
    assert.doesNotMatch(output, /^  name: axis-pgbouncer$/m)
    assert.doesNotMatch(output, /pgbouncer/i)
    assert.doesNotMatch(output, /Cell__RuntimeDatabase/)
  })

  test('PgBouncer is off by default', () => {
    const values = spawnSync(process.env.HELM ?? 'helm', ['show', 'values', chart], { encoding: 'utf8' })
    assert.equal(values.status, 0, values.stderr)
    assert.match(values.stdout, /^  pgbouncer:\n    enabled: false\n/m)
  })

  test('PgBouncer on without cell.id fails and names it', () => {
    const result = render('--set', 'cell.id=')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /cell\.id/)
  })

  test('a value inside a PgBouncer Secret reference is rejected and never rendered', () => {
    for (const path of ['secrets.pgbouncerAuth.value', 'secrets.pgbouncerCertificate.value']) {
      const result = render('--set', `${path}=leak`)
      assert.notEqual(result.status, 0, path)
      assert.match(result.stderr, /value/, path)
      assert.doesNotMatch(result.stdout, /leak/, path)
    }
  })

  test('an unknown cell.pgbouncer value is rejected', () => {
    const result = render('--set', 'cell.pgbouncer.password=leak')
    assert.notEqual(result.status, 0)
    assert.doesNotMatch(result.stdout, /leak/)
  })

  test('in a cell with a bucket, a CronJob runs cell backup at 01:00 in the time zone, one at a time and with no retry', () => {
    const job = find(renderOk(), 'CronJob', 'axis-cell-backup')
    assert.match(job, /schedule: "0 1 \* \* \*"\n/)
    assert.match(job, /timeZone: "Asia\/Ho_Chi_Minh"\n/)
    assert.match(job, /concurrencyPolicy: Forbid\n/)
    assert.match(job, /backoffLimit: 0\n/)
    assert.match(job, /activeDeadlineSeconds: 22500\n/)
    assert.match(job, /restartPolicy: Never\n/)
    assert.match(job, /image: "axis-admin:[^"]+"/)
    assert.match(job, /args:\n\s+- cell\n\s+- backup\n/)
    assert.match(job, /- name: Cell__Id\n\s+value: "vn-1"\n/)
    assert.match(job, /- name: Cell__BackupStorage__Bucket\n\s+value: "axis-backups-vn"\n/)
    assert.match(job, /- name: Cell__BackupStorage__ServiceUrl\n\s+value: "https:\/\/s3\.vn\.example\.com"\n/)
    assert.match(job, /- name: Cell__BackupStorage__Region\n\s+value: "vn-hcm"\n/)
    assert.equal(job.match(/- name: Cell__BackupKeyFile\n/g).length, 1)
    assert.match(job, /- name: Cell__BackupKeyFile\n\s+value: "\/cell-backup-key\/key"\n/)
  })

  test('the storage credentials are files from Secret references in the CronJob, and never env entries', () => {
    const output = renderOk()
    const job = find(output, 'CronJob', 'axis-cell-backup')
    for (const file of ['Cell__BackupStorage__AccessKeyId', 'Cell__BackupStorage__SecretAccessKey']) {
      assert.match(job, new RegExp(`- secret:\\n\\s+name: "axis-backup-storage"\\n\\s+items:\\n\\s+- key: "${file}"\\n\\s+path: ${file}\\n`))
    }
    assert.match(job, /path: ConnectionStrings__PlatformSchema\n/)
    assert.match(job, /path: Platform__SecretKey\n/)
    assert.doesNotMatch(job, /path: ConnectionStrings__Platform\n/)
    assert.match(job, /- name: cell-backup-key\n\s+mountPath: \/cell-backup-key\n\s+readOnly: true\n/)
    assert.match(job, /secretName: "axis-cell-backup-key"\n\s+defaultMode: 292\n\s+items:\n\s+- key: "cell-backup-key"\n\s+path: key\n/)
    assert.doesNotMatch(output, /- name: Cell__BackupStorage__(AccessKeyId|SecretAccessKey)/)
  })

  test('the identity provider read-only connection is a file from a Secret reference in the backup CronJob alone, and never an env entry', () => {
    const output = renderOk()
    const job = find(output, 'CronJob', 'axis-cell-backup')
    assert.match(
      job,
      /- secret:\n\s+name: "axis-identity-reader"\n\s+items:\n\s+- key: "IdentityProvider__DatabaseConnectionString"\n\s+path: IdentityProvider__DatabaseConnectionString\n/,
    )
    for (const document of documents(output).filter((d) => !/^  name: axis-cell-backup$/m.test(d))) {
      assert.doesNotMatch(document, /IdentityProvider__DatabaseConnectionString|axis-identity-reader/)
    }
    assert.doesNotMatch(output, /- name: IdentityProvider__DatabaseConnectionString/)
  })

  test('in a cell with a bucket, a CronJob runs cell restore-check on Sundays at 03:00 in the time zone, one at a time and with no retry', () => {
    const output = renderOk()
    const job = find(output, 'CronJob', 'axis-cell-restore-check')
    assert.match(job, /schedule: "0 3 \* \* 0"\n/)
    assert.match(job, /timeZone: "Asia\/Ho_Chi_Minh"\n/)
    assert.match(job, /concurrencyPolicy: Forbid\n/)
    assert.match(job, /backoffLimit: 0\n/)
    assert.match(job, /activeDeadlineSeconds: 21600\n/)
    assert.match(job, /restartPolicy: Never\n/)
    assert.match(job, /image: "axis-admin:[^"]+"/)
    assert.match(job, /args:\n\s+- cell\n\s+- restore-check\n/)
    assert.match(job, /- name: Cell__Id\n\s+value: "vn-1"\n/)
    assert.match(job, /- name: Cell__InternalDomain\n\s+value: "internal\.example\.com"\n/)
    assert.match(job, /- name: IdentityProvider__Url\n\s+value: "https:\/\/id\.example\.com\/"\n/)
    assert.match(job, /- name: IdentityProvider__AdminClientId\n\s+value: axis-admin\n/)
    assert.match(job, /- name: Studio__ApplicationsPath\n\s+value: \/studio\n/)
    assert.match(job, /- name: studio\n\s+mountPath: \/studio\n/)
    assert.match(job, /- name: studio\n\s+emptyDir: \{\}\n/)
    assert.equal(job.match(/- name: tmp\n\s+emptyDir/g).length, 1)
    assert.equal(job.match(/- name: Cell__BackupKeyFile\n/g).length, 1)
    assert.match(job, /- name: Cell__BackupKeyFile\n\s+value: "\/cell-backup-key\/key"\n/)
    assert.match(job, /- name: Cell__BackupStorage__Bucket\n\s+value: "axis-backups-vn"\n/)
    assert.match(job, /- name: cell-backup-key\n\s+mountPath: \/cell-backup-key\n\s+readOnly: true\n/)
    assert.match(job, /secretName: "axis-cell-backup-key"\n\s+defaultMode: 292\n\s+items:\n\s+- key: "cell-backup-key"\n\s+path: key\n/)
  })

  test('the restore-check CronJob gets the admin, client secret, storage and scenario secrets as files, and never env entries', () => {
    const job = find(renderOk(), 'CronJob', 'axis-cell-restore-check')
    for (const [secret, file] of [
      ['axis-admin-db', 'Admin__ConnectionString'],
      ['axis-identity', 'IdentityProvider__AdminClientSecret'],
      ['axis-backup-storage', 'Cell__BackupStorage__AccessKeyId'],
      ['axis-backup-storage', 'Cell__BackupStorage__SecretAccessKey'],
      ['axis-platform', 'ConnectionStrings__PlatformSchema'],
      ['axis-platform', 'Platform__SecretKey'],
      ['axis-scenario', 'Scenarios__ServerConnectionString'],
    ]) {
      assert.match(job, new RegExp(`- secret:\\n\\s+name: "${secret}"\\n\\s+items:\\n\\s+- key: "${file}"\\n\\s+path: ${file}\\n`), file)
      assert.doesNotMatch(job, new RegExp(`- name: ${file}\\n`), file)
    }
    assert.doesNotMatch(job, /path: ConnectionStrings__Platform\n/)

    const without = find(renderOk('--set', 'secrets.scenarioServerConnection.name='), 'CronJob', 'axis-cell-restore-check')
    assert.doesNotMatch(without, /Scenarios__|axis-scenario/)
  })

  test('no other pod gets the backup storage, and no pod but the publisher gets the cell backup key', () => {
    const output = renderOk()
    for (const document of documents(output).filter((d) => /^kind: (Deployment|Job)$/m.test(d) && !/^  name: axis-publisher$/m.test(d))) {
      assert.doesNotMatch(document, /Cell__BackupStorage__|axis-cell-backup-key|axis-backup-storage|\/cell-backup-key\//)
    }
    // The publisher in a cell gets the cell backup key, but never the backup storage.
    assert.doesNotMatch(find(output, 'Deployment', 'axis-publisher'), /Cell__BackupStorage__|axis-backup-storage/)
  })

  test('a value inside a backup storage Secret reference, or an unknown backup value, is rejected and never rendered', () => {
    for (const path of ['secrets.backupStorageAccessKeyId.value', 'secrets.backupStorageSecretAccessKey.value', 'backup.accessKeyId']) {
      const result = render('--set', `${path}=leak`)
      assert.notEqual(result.status, 0, path)
      assert.doesNotMatch(result.stdout, /leak/, path)
    }
  })

  test('outside a cell, or without a bucket, no CronJob renders', () => {
    const withoutBucket = renderOk('--set', 'backup.bucket=')
    assert.doesNotMatch(withoutBucket, /^kind: CronJob$/m)
    assert.doesNotMatch(withoutBucket, /axis-cell-restore-check/)
    const outside = renderOk(
      '--set',
      'cell.id=',
      '--set',
      'cell.certificate.enabled=false',
      '--set',
      'cell.pgbouncer.enabled=false',
    )
    assert.doesNotMatch(outside, /^kind: CronJob$/m)
    assert.doesNotMatch(outside, /Cell__BackupStorage__|axis-cell-restore-check/)
  })

  test('in a cell with a bucket, an empty time zone fails and names the value', () => {
    const result = render('--set', 'backup.timeZone=')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /backup\.timeZone/)
  })
})
