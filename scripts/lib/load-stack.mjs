// Writes the files that scripts/load.sh needs to start the reference stack from deploy/compose/compose.yaml:
// a certificate authority made for the run, the certificates, the secret files, the SQL that creates the database
// roles, and override.json. Run it as `node scripts/lib/load-stack.mjs <folder>` with a folder that does not exist yet.
// It mirrors the setup of scripts/onprem.test.mjs.
//
// The override file is JSON, which is valid YAML. It adds PostgreSQL over TLS, trusts the authority in every host
// and adds the k6 service under the `load` profile. The authority key stays in ca-private and is never mounted.
import { spawnSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { chmodSync, copyFileSync, mkdirSync, readdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { issuePostgresCertificate, postgresTlsScript, serverCertificateMount, verifyFull } from './postgres-tls.mjs'

const root = fileURLToPath(new URL('../..', import.meta.url))

// The tag of the k6 image, and the hosts of the run.
export const k6Image = 'grafana/k6:1.3.0'
export const tenantHost = 'load.axis.test'
export const identityHost = 'id.axis.test'

const password = () => randomBytes(18).toString('hex')

export const writeLoadStack = (target, { proxyAddress = '172.31.250.10' } = {}) => {
  const folder = resolve(target)
  mkdirSync(folder, { recursive: true })
  chmodSync(folder, 0o755)

  const openssl = (...args) => {
    const result = spawnSync('openssl', args, { cwd: folder, encoding: 'utf8' })
    if (result.status !== 0) throw new Error(`openssl ${args.join(' ')}\n${result.stdout}${result.stderr}`)
  }
  const folderOf = (name) => {
    const path = join(folder, name)
    mkdirSync(path)
    chmodSync(path, 0o755)
    return path
  }
  const writeFile = (path, content) => {
    writeFileSync(path, content)
    chmodSync(path, 0o644)
  }
  const copyFile = (from, to) => {
    copyFileSync(from, to)
    chmodSync(to, 0o644)
  }
  const open = (path) => {
    for (const name of readdirSync(path)) chmodSync(join(path, name), 0o644)
  }

  // The authority. issuePostgresCertificate makes ca-private/ca.key and ca-private/ca.crt, and copies the
  // certificate alone to ca/ca.crt. That folder is what the hosts and k6 trust, so the key is never mounted.
  const { serverDir, trustDir: trust, authority } = issuePostgresCertificate({ folder, openssl, name: 'postgres' })
  const certs = folderOf('certs')

  // Signs a certificate of the authority for the DNS names and writes tls.crt and tls.key into `directory`.
  const issue = (directory, name, names) => {
    const label = join('certs', name)
    writeFile(join(certs, `${name}.ext`), `subjectAltName=${names.map((dns) => `DNS:${dns}`).join(',')}\nbasicConstraints=CA:FALSE\n`)
    openssl('req', '-newkey', 'rsa:2048', '-nodes', '-subj', `/CN=${names[0]}`, '-keyout', join(directory, 'tls.key'), '-out', `${label}.csr`)
    openssl('x509', '-req', '-in', `${label}.csr`, '-CA', authority.certificate, '-CAkey', authority.key, '-CAcreateserial', '-days', '2', '-extfile', `${label}.ext`, '-out', join(directory, 'tls.crt'))
    open(directory)
  }

  const identityTls = folderOf('identity-tls')
  issue(identityTls, 'identity', ['identity'])

  // The identity provider's database authority: root.crt is the authority certificate.
  const identityDbCa = folderOf('db-ca')
  copyFile(authority.certificate, join(identityDbCa, 'root.crt'))

  // The proxy answers for the tenant host and the identity host, and trusts the authority for the servers.
  const tls = folderOf('tls')
  issue(tls, 'proxy', [tenantHost, identityHost])
  copyFile(authority.certificate, join(tls, 'server-ca.crt'))

  const serverTls = folderOf('server-tls')
  issue(serverTls, 'server', ['servers'])

  const secrets = folderOf('secrets')
  const passwords = { schema: password(), platform: password(), admin: password(), postgres: password(), identity: password() }
  const trusted = verifyFull('/run/test-ca/ca.crt')
  const connection = (user, pass) => `Host=postgres;Port=5432;Database=axis_platform;Username=${user};Password=${pass}${trusted}`
  writeFile(join(secrets, 'ConnectionStrings__PlatformSchema'), connection('axis_platform_schema', passwords.schema))
  writeFile(join(secrets, 'ConnectionStrings__Platform'), connection('axis_platform', passwords.platform))
  writeFile(join(secrets, 'Admin__ConnectionString'), `Host=postgres;Port=5432;Database=postgres;Username=axis_admin;Password=${passwords.admin}${trusted}`)
  writeFile(join(secrets, 'Platform__SecretKey'), randomBytes(32).toString('base64'))
  writeFile(join(secrets, 'IdentityProvider__AdminClientSecret'), password())
  writeFile(join(secrets, 'KC_DB_URL'), 'jdbc:postgresql://postgres:5432/axis_identity?sslmode=verify-full&sslrootcert=/db-ca/root.crt')
  writeFile(join(secrets, 'KC_DB_USERNAME'), 'axis_identity')
  writeFile(join(secrets, 'KC_DB_PASSWORD'), passwords.identity)

  // The database roles: the SQL of the installation guide.
  const initSql = join(folder, 'init-roles.sql')
  writeFile(
    initSql,
    [
      `CREATE ROLE axis_platform_schema LOGIN PASSWORD '${passwords.schema}';`,
      `CREATE ROLE axis_platform LOGIN PASSWORD '${passwords.platform}';`,
      `CREATE ROLE axis_admin LOGIN PASSWORD '${passwords.admin}' CREATEDB CREATEROLE;`,
      'CREATE DATABASE axis_platform OWNER axis_platform_schema;',
      `CREATE ROLE axis_identity LOGIN PASSWORD '${passwords.identity}';`,
      'CREATE DATABASE axis_identity OWNER axis_identity;',
      '',
    ].join('\n'),
  )

  for (const name of ['extensions', 'modules', 'studio', 'apps', 'k6']) folderOf(name)

  // Every host reads the authority certificate that its connection strings name.
  const trustVolume = `${trust}:/run/test-ca:ro`
  const refresh = { Tenancy__RefreshInterval: '00:00:02' }
  const authorityFile = { SSL_CERT_FILE: '/run/test-ca/ca.crt' }
  const server = { environment: { ...refresh, ...authorityFile }, volumes: [trustVolume] }
  writeFile(
    join(folder, 'override.json'),
    JSON.stringify({
      services: {
        postgres: {
          image: 'postgres:18-alpine',
          environment: { POSTGRES_USER: 'postgres', POSTGRES_PASSWORD: passwords.postgres },
          entrypoint: ['sh', '-c', postgresTlsScript],
          volumes: [`${initSql}:/docker-entrypoint-initdb.d/init-roles.sql:ro`, `${serverDir}:${serverCertificateMount}:ro`],
        },
        server,
        'server-2': server,
        worker: { environment: refresh, volumes: [trustVolume] },
        admin: { environment: authorityFile, volumes: [trustVolume] },
        // k6 reaches the tenant host through the proxy only, at the proxy's fixed address.
        k6: {
          profiles: ['load'],
          image: k6Image,
          environment: { ...authorityFile, AXIS_LOAD_URL: `https://${tenantHost}`, AXIS_LOAD_USERS: '/load/users.json' },
          volumes: [`${join(root, 'tests/load')}:/scripts:ro`, `${join(folder, 'k6')}:/load:ro`, trustVolume],
          extra_hosts: [`${tenantHost}:${proxyAddress}`],
        },
      },
    }),
  )
  return folder
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const target = process.argv[2]
  if (!target) {
    console.error('Usage: node scripts/lib/load-stack.mjs <folder>')
    process.exit(2)
  }
  writeLoadStack(target, { proxyAddress: process.env.AXIS_PROXY_ADDRESS || undefined })
}
