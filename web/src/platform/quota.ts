/**
 * The title of a `403` whose problem `reason` is `quota-exceeded`, or `null` for any other answer. It reads a clone of the body.
 */
export async function quotaRefusalTitle(response: Response): Promise<string | null> {
  if (response.status !== 403 || typeof response.clone !== 'function') {
    return null
  }
  try {
    const problem = (await response.clone().json()) as { reason?: unknown; title?: unknown }
    return problem.reason === 'quota-exceeded' && typeof problem.title === 'string' && problem.title !== ''
      ? problem.title
      : null
  } catch {
    return null
  }
}

/** A write refused past a tenant quota. `message` is the problem's title, which the SPA shows as is. */
export class QuotaRefusedError extends Error {}
