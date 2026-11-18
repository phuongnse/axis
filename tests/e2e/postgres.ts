import { spawn, spawnSync } from 'node:child_process'

/** The lines that `sql` returns from `database` in the E2E PostgreSQL container. The platform database is `axis`. */
export function psql(database: string, sql: string): string[] {
  const container = process.env.AXIS_E2E_POSTGRES_CONTAINER
  if (!container) {
    throw new Error('AXIS_E2E_POSTGRES_CONTAINER is not set. Run the E2E tests with scripts/e2e.sh.')
  }
  const result = spawnSync('docker', ['exec', container, 'psql', '-U', 'axis', '-d', database, '-tAc', sql], {
    encoding: 'utf8',
  })
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
  return result.stdout.split('\n').filter((line) => line !== '')
}

/**
 * Takes a PostgreSQL advisory lock on `key` in the platform database, in `shared` or `exclusive` mode, and returns once it
 * is held. A shared holder waits for an exclusive one and the other way round, and PostgreSQL queues the waiters, so an
 * exclusive waiter is not starved by shared holders that come later. A psql session in the E2E PostgreSQL container holds
 * the lock, so it ends with the session: `release` closes the session, and a worker that crashes frees the lock too.
 * Rejects with psql's output when the session ends before it holds the lock.
 */
export function holdAdvisoryLock(key: bigint, mode: 'shared' | 'exclusive'): Promise<{ release(): Promise<void> }> {
  const container = process.env.AXIS_E2E_POSTGRES_CONTAINER
  if (!container) {
    throw new Error('AXIS_E2E_POSTGRES_CONTAINER is not set. Run the E2E tests with scripts/e2e.sh.')
  }
  const child = spawn(
    'docker',
    ['exec', '-i', container, 'psql', '-U', 'axis', '-d', 'axis', '-qtA', '-v', 'ON_ERROR_STOP=1'],
    {
      stdio: ['pipe', 'pipe', 'pipe'],
    },
  )
  let output = ''
  const closed = new Promise<number | null>((resolve) => child.once('close', resolve))
  const release = async () => {
    child.stdin.end()
    await closed
  }
  return new Promise((resolve, reject) => {
    const onData = (chunk: string) => {
      output += chunk
      if (output.split('\n').includes('locked')) resolve({ release })
    }
    child.stdout.setEncoding('utf8').on('data', onData)
    child.stderr.setEncoding('utf8').on('data', (chunk: string) => (output += chunk))
    child.once('error', reject)
    void closed.then((status) =>
      reject(new Error(`psql ended with status ${status} before it held the lock. Output:\n${output}`)),
    )
    const lock = mode === 'shared' ? 'pg_advisory_lock_shared' : 'pg_advisory_lock'
    child.stdin.write(`SELECT ${lock}(${key}); SELECT 'locked';\n`)
  })
}
