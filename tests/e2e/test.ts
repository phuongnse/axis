import { test as base, type Browser } from '@playwright/test'
import { watchPolicyViolations } from './policy-violations'

export { expect } from '@playwright/test'

/**
 * The `test` every spec uses. It fails a test whose pages report a Content Security Policy violation, so the SPA,
 * Studio and every widget are shown to run under the policy the server sets. A spec that opens its own browser
 * context is watched too, because the fixture wraps `browser.newContext` for the length of the test.
 */
export const test = base.extend<{ policyViolations: string[] }>({
  policyViolations: [
    async ({ browser }, use) => {
      const violations: string[] = []
      const browserWithContexts: Pick<Browser, 'newContext'> = browser
      const newContext = browser.newContext
      browserWithContexts.newContext = async (...args: Parameters<Browser['newContext']>) => {
        const context = await newContext.apply(browser, args)
        await watchPolicyViolations(context, violations)
        return context
      }
      try {
        await use(violations)
      } finally {
        browserWithContexts.newContext = newContext
      }
      if (violations.length > 0) {
        throw new Error(`The pages reported Content Security Policy violations:\n${violations.join('\n')}`)
      }
    },
    { auto: true },
  ],
  context: async ({ context, policyViolations }, use) => {
    await watchPolicyViolations(context, policyViolations)
    await use(context)
  },
})
