import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Renders deploy/helm/axis-controlplane with `helm template`. scripts/test.sh sets HELM to the pinned binary
// that scripts/setup.sh installs. No YAML parser is available, so the tests split the output
// into documents and match them with regular expressions.
const chart = fileURLToPath(new URL('../deploy/helm/axis-controlplane', import.meta.url))
const testValues = fileURLToPath(new URL('../deploy/helm/axis-controlplane/ci/test-values.yaml', import.meta.url))

function render(...extra) {
  return spawnSync(process.env.HELM ?? 'helm', ['template', 'axis-controlplane', chart, '-f', testValues, ...extra], {
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

describe('the Axis control plane Helm chart', () => {
  test('renders one Deployment and one ClusterIP Service', () => {
    const output = renderOk()
    assert.deepEqual(
      documents(output).map((document) => document.match(/^kind: (\w+)$/m)[1]),
      ['Service', 'Deployment'],
    )
    const service = find(output, 'Service', 'axis-controlplane')
    assert.match(service, /type: ClusterIP/)
    assert.match(service, /- name: https\n\s+port: 443\n\s+targetPort: https\n/)
    assert.match(find(output, 'Deployment', 'axis-controlplane'), /replicas: 2\n/)
  })

  test('has startup, liveness and readiness probes over HTTPS on the https port', () => {
    const deployment = find(renderOk(), 'Deployment', 'axis-controlplane')
    for (const [probe, path] of [
      ['startupProbe', 'live'],
      ['livenessProbe', 'live'],
      ['readinessProbe', 'ready'],
    ]) {
      assert.match(deployment, new RegExp(`${probe}:\\n\\s+httpGet:\\n\\s+path: /health/${path}\\n\\s+port: https\\n\\s+scheme: HTTPS\\n`))
    }
    assert.match(deployment, /- name: https\n\s+containerPort: 8443/)
    assert.doesNotMatch(deployment, /containerPort: 8080|port: http\n/)
  })

  test('listens on HTTPS only, with the certificate of its TLS Secret', () => {
    const deployment = find(renderOk(), 'Deployment', 'axis-controlplane')
    assert.match(deployment, /- name: Kestrel__Endpoints__Https__Url\n\s+value: https:\/\/\+:8443\n/)
    assert.match(deployment, /- name: Kestrel__Endpoints__Https__Certificate__Path\n\s+value: \/controlplane-tls\/tls\.crt\n/)
    assert.match(deployment, /- name: Kestrel__Endpoints__Https__Certificate__KeyPath\n\s+value: \/controlplane-tls\/tls\.key\n/)
    assert.doesNotMatch(deployment, /Kestrel__Endpoints__Http__|ASPNETCORE_URLS|ASPNETCORE_HTTP_PORTS/)
    assert.match(deployment, /- name: controlplane-tls\n\s+mountPath: \/controlplane-tls\n\s+readOnly: true\n/)
    assert.match(
      deployment,
      /- name: controlplane-tls\n\s+secret:\n\s+secretName: "axis-controlplane-tls"\n(\s+#.*\n)?\s+defaultMode: 292\n\s+items:\n\s+- key: tls\.crt\n\s+path: tls\.crt\n\s+- key: tls\.key\n\s+path: tls\.key\n/,
    )
  })

  test('names every secret only through a Kubernetes Secret: a projected file, no Secret object and no value', () => {
    const output = renderOk()
    assert.doesNotMatch(output, /^kind: Secret$/m)
    assert.doesNotMatch(output, /stringData:/)
    assert.doesNotMatch(output, /^data:/m)
    const deployment = find(output, 'Deployment', 'axis-controlplane')
    assert.doesNotMatch(deployment, /- name: ControlPlane__ConnectionString\n/)
    assert.doesNotMatch(deployment, /secretKeyRef|valueFrom/)
    assert.match(
      deployment,
      /- name: secrets\n\s+projected:\n(\s+#.*\n)?\s+defaultMode: 292\n\s+sources:\n\s+- secret:\n\s+name: "axis-controlplane-db"\n\s+items:\n\s+- key: "ControlPlane__ConnectionString"\n\s+path: ControlPlane__ConnectionString\n/,
    )
    assert.match(deployment, /- name: secrets\n\s+mountPath: \/run\/secrets\n\s+readOnly: true\n/)
  })

  test('reads the connection string from the key of the Secret that the values name', () => {
    const deployment = find(
      renderOk('--set', 'secrets.connectionString.name=other-db', '--set', 'secrets.connectionString.key=url'),
      'Deployment',
      'axis-controlplane',
    )
    assert.match(deployment, /name: "other-db"\n\s+items:\n\s+- key: "url"\n\s+path: ControlPlane__ConnectionString\n/)
  })

  for (const [path, message] of [
    ['controlPlane.region', 'controlPlane.region is required'],
    ['controlPlane.tenantDomain', 'controlPlane.tenantDomain is required'],
    ['controlPlane.mailFrom', 'controlPlane.mailFrom is required'],
    ['controlPlane.mail.host', 'controlPlane.mail.host is required'],
    ['secrets.connectionString.name', 'secrets.connectionString.name is required'],
    ['secrets.certificate.name', 'secrets.certificate.name is required'],
  ]) {
    test(`fails to render without ${path}, and names it`, () => {
      const result = render('--set', `${path}=`)
      assert.notEqual(result.status, 0)
      assert.ok(result.stderr.includes(message), result.stderr)
    })
  }

  test('rejects a secret value in the values, and never prints it', () => {
    for (const setting of ['secrets.connectionString.value=leak', 'controlPlane.connectionString=leak', 'secrets.certificate.value=leak', 'secrets.mailPassword.value=leak']) {
      const result = render('--set', setting)
      assert.notEqual(result.status, 0, setting)
      assert.doesNotMatch(result.stdout, /leak/, setting)
    }
  })

  test('sets the region and the tenant domain from the values', () => {
    const deployment = find(renderOk('--set', 'controlPlane.region=eu', '--set', 'controlPlane.tenantDomain=eu.example.org'), 'Deployment', 'axis-controlplane')
    assert.match(deployment, /- name: ControlPlane__Region\n\s+value: "eu"\n/)
    assert.match(deployment, /- name: ControlPlane__TenantDomain\n\s+value: "eu\.example\.org"\n/)
  })

  test('sets the mail settings and mounts the relay password as ControlPlane__Mail__Password', () => {
    const deployment = find(renderOk(), 'Deployment', 'axis-controlplane')
    assert.match(deployment, /- name: ControlPlane__MailFrom\n\s+value: "no-reply@mail\.vn\.example\.com"\n/)
    assert.match(deployment, /- name: ControlPlane__Mail__Host\n\s+value: "smtp\.example\.com"\n/)
    assert.match(deployment, /- name: ControlPlane__Mail__Port\n\s+value: "587"\n/)
    assert.match(deployment, /- name: ControlPlane__Mail__UseTls\n\s+value: "true"\n/)
    assert.match(deployment, /- name: ControlPlane__Mail__UserName\n\s+value: "relay-user"\n/)
    assert.doesNotMatch(deployment, /- name: ControlPlane__Mail__Password\n/)
    assert.match(
      deployment,
      /- secret:\n\s+name: "axis-controlplane-mail"\n\s+items:\n\s+- key: "ControlPlane__Mail__Password"\n\s+path: ControlPlane__Mail__Password\n/,
    )
  })

  test('sets no user name and mounts no password when the values leave them out', () => {
    const deployment = find(renderOk('--set', 'controlPlane.mail.userName=', '--set', 'secrets.mailPassword.name='), 'Deployment', 'axis-controlplane')
    assert.doesNotMatch(deployment, /ControlPlane__Mail__UserName|ControlPlane__Mail__Password/)
  })

  test('applies the image registry, tag and pull Secrets', () => {
    const deployment = find(
      renderOk('--set', 'image.registry=registry.example.com/axis/', '--set', 'image.tag=1.2.3'),
      'Deployment',
      'axis-controlplane',
    )
    assert.match(deployment, /image: "registry\.example\.com\/axis\/axis-controlplane:1\.2\.3"\n/)
    assert.match(deployment, /imagePullSecrets:\n\s+- name: "registry-credentials"\n/)
    assert.match(find(renderOk(), 'Deployment', 'axis-controlplane'), /image: "axis-controlplane:0\.0\.0-dev"\n/)
  })

  test('runs as a non-root user on a read-only root file system without extra privileges', () => {
    const deployment = find(renderOk(), 'Deployment', 'axis-controlplane')
    assert.match(deployment, /runAsNonRoot: true/)
    assert.match(deployment, /readOnlyRootFilesystem: true/)
    assert.match(deployment, /allowPrivilegeEscalation: false/)
    assert.match(deployment, /drop:\n\s+- ALL/)
    assert.match(deployment, /automountServiceAccountToken: false/)
    assert.match(deployment, /- name: tmp\n\s+mountPath: \/tmp\n/)
  })
})
