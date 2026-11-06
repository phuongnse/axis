import { spawnSync } from 'node:child_process'

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
