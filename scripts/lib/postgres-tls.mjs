// Shared by the smoke tests: a PostgreSQL that serves TLS with a certificate from a test authority, and the
// connection string settings that make a host check it. The production hosts refuse a database connection
// string without SSL Mode=VerifyFull, so every smoke test that starts them needs this.
import { chmodSync, copyFileSync, mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

// Where the container finds the certificate and the key. They are copied before PostgreSQL starts, because
// PostgreSQL refuses a key that other users can read, and a bind mount keeps the owner and the mode of the host.
export const serverCertificateMount = '/run/postgres-tls'

// The script that starts PostgreSQL with TLS, run with `sh -c`. docker-entrypoint.sh runs the initialization
// scripts on a server that listens only on the Unix socket, then starts the server with the same settings.
export const postgresTlsScript = [
  `install -o postgres -g postgres -m 600 ${serverCertificateMount}/server.key /tmp/server.key`,
  `install -o postgres -g postgres -m 644 ${serverCertificateMount}/server.crt /tmp/server.crt`,
  'exec docker-entrypoint.sh postgres -c ssl=on -c ssl_cert_file=/tmp/server.crt -c ssl_key_file=/tmp/server.key',
].join(' && ')

/**
 * Writes server.crt and server.key for the DNS name `name` into their own folder under `folder`. Mount that folder
 * read-only at serverCertificateMount.
 *
 * `openssl` runs the openssl command with its arguments and fails on a bad exit. `ca` is the certificate and key of
 * an existing authority, as `{ certificate, key }` paths. Without it, the call makes an authority in `ca-private`
 * and copies its certificate alone to `ca`, so a container can trust it without the key. It returns `serverDir`,
 * the folder of the server certificate, `trustDir`, the folder with the authority certificate alone (set only when
 * it made the authority), and `authority`.
 */
export const issuePostgresCertificate = ({ folder, openssl, name, ca }) => {
  let authority = ca
  let trustDir
  if (!authority) {
    const privateDir = join(folder, 'ca-private')
    mkdirSync(privateDir)
    authority = { certificate: join(privateDir, 'ca.crt'), key: join(privateDir, 'ca.key') }
    openssl(
      'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '2',
      '-subj', '/CN=Axis smoke test CA',
      '-keyout', authority.key, '-out', authority.certificate,
      '-addext', 'basicConstraints=critical,CA:TRUE', '-addext', 'keyUsage=critical,keyCertSign,cRLSign',
    )
    trustDir = join(folder, 'ca')
    mkdirSync(trustDir)
    chmodSync(trustDir, 0o755)
    copyFileSync(authority.certificate, join(trustDir, 'ca.crt'))
    chmodSync(join(trustDir, 'ca.crt'), 0o644)
  }

  const serverDir = join(folder, 'postgres-tls')
  mkdirSync(serverDir)
  chmodSync(serverDir, 0o755)
  const extensions = join(folder, 'postgres-tls.ext')
  writeFileSync(extensions, `subjectAltName=DNS:${name}\nbasicConstraints=CA:FALSE\n`)
  const request = join(folder, 'postgres-tls.csr')
  openssl('req', '-newkey', 'rsa:2048', '-nodes', '-subj', `/CN=${name}`, '-keyout', join(serverDir, 'server.key'), '-out', request)
  openssl(
    'x509', '-req', '-in', request, '-CA', authority.certificate, '-CAkey', authority.key, '-CAcreateserial',
    '-days', '2', '-extfile', extensions, '-out', join(serverDir, 'server.crt'),
  )
  for (const file of ['server.crt', 'server.key']) chmodSync(join(serverDir, file), 0o644)
  return { serverDir, trustDir, authority }
}

// The settings to append to a connection string so that a host checks the server certificate against the
// authority certificate at `rootCertificate`, a path inside the container.
export const verifyFull = (rootCertificate) => `;SSL Mode=VerifyFull;Root Certificate=${rootCertificate}`
