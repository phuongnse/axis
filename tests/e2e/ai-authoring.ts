import type { BrowserContext, Page } from '@playwright/test'
import { startPublisher } from './admin-tool'
import { submitCode, submitPassword } from './keycloak'
import { holdAdvisoryLock } from './postgres'
import { required } from './server-settings'
import { liveTotp } from './totp'

// The sign-in, the Ask AI call, the publisher and the lock that the AI authoring specs share, see the Ask AI agent spec in docs/testing.md.

/** An author's account in a tenant's realm: its name, its password and the Base32 secret of its authenticator. */
export interface AuthoringAccount {
  username: string
  password: string
  secret: string
}

/** Signs `account` in on the tenant's `host` in `context` and leaves the page on the tenant's home. */
export async function signIn(context: BrowserContext, host: string, account: AuthoringAccount): Promise<Page> {
  const page = await context.newPage()
  await page.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
  await submitPassword(page, account.username, account.password)
  await submitCode(page, await liveTotp(account.secret))
  await page.waitForURL(`https://${host}/`)
  return page
}

/** Sends `text` from the Ask AI panel of the Studio of `application` on `host` and waits until the page of the new request opens. */
export async function askAi(page: Page, host: string, application: string, text: string): Promise<void> {
  await page.goto(`https://${host}/studio?app=${application}`)
  await page.getByTestId('studio-ask-ai').click()
  await page.getByRole('textbox', { name: 'Your request', exact: true }).fill(text)
  await page.getByTestId('studio-ask-ai-send').click()
  await page.waitForURL(/view=requests&request=/)
}

/**
 * The spec's own publisher, with the server's Studio settings, the scenario role and the runtime role. The runtime role
 * gives it the write lock that an accept takes, so without it the accept fails with "The draft is not found."
 * It runs alone under the publishers' lock. See the layer test of the Ask AI agent spec. `env` adds settings after these,
 * such as the application package host of the package spec. The AI specs pass none.
 */
export function startSpecPublisher(applications: string, env: Record<string, string> = {}) {
  return startPublisher(
    {
      Studio__ApplicationsPath: applications,
      Studio__AuthorRole: 'author',
      ConnectionStrings__Platform: required(
        'AXIS_E2E_DATABASE',
        'the PostgreSQL connection string of the runtime role',
      ),
      Scenarios__ServerConnectionString: required(
        'AXIS_E2E_SCENARIO_DATABASE',
        'the PostgreSQL connection string of the scenario role',
      ),
      ...env,
    },
    { alone: true },
  )
}

/** The advisory lock of the AI authoring specs, "AXISE2EA", distinct from the publishers' lock and the product's own keys. */
const aiSpecLockKey = 0x4158_4953_4532_4541n

/**
 * Takes the lock that keeps the AI authoring specs apart, exclusively, and returns once it is held. An agent host serves
 * every directory tenant, so if two of these specs ran at once, one spec's host could plan the other spec's request with
 * the wrong scripted answers. Each spec holds the lock from its `beforeAll` until its `afterAll` ends, and releases it last.
 */
export function holdAiSpecLock(): Promise<{ release(): Promise<void> }> {
  return holdAdvisoryLock(aiSpecLockKey, 'exclusive')
}
