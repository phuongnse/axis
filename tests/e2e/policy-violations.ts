import type { BrowserContext } from '@playwright/test'

const watched = new WeakSet<BrowserContext>()

/**
 * Records every Content Security Policy violation that any page of `context` reports, as a line in `violations`.
 * The page reports through the `securitypolicyviolation` event, which a script in the page listens for, so it
 * also sees violations in frames. A context that is already watched is left alone.
 */
export async function watchPolicyViolations(context: BrowserContext, violations: string[]): Promise<void> {
  if (watched.has(context)) {
    return
  }
  watched.add(context)
  await context.exposeBinding('__axisPolicyViolation', (_source, text: string) => {
    violations.push(text)
  })
  await context.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (event) => {
      const report = (window as unknown as { __axisPolicyViolation: (text: string) => void }).__axisPolicyViolation
      report(`${event.violatedDirective} blocked ${event.blockedURI || 'inline'} on ${event.documentURI}`)
    })
  })
}
