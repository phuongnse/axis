import { expect, test } from '../test'
import type { APIRequestContext, Locator, Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { visibleDialog } from '../dialogs'
import { sessionFile } from '../sessions'

// The SPA turns its animations off when the browser asks for reduced motion, and the config asks for it in every
// project (D97). requester holds the delete grant on notes.
test.use({ storageState: sessionFile('requester') })

const notesPath = '/api/apps/E2eApp/entities/Note/records'
const deletesPage = '/e2e/notedeletes?pageSize=100&sort=-title'

async function createNote(request: APIRequestContext, title: string) {
  const response = await request.post(notesPath, { data: { values: { title } }, headers: csrfHeaders })
  expect(response.status()).toBe(201)
}

/** Opens the delete confirmation of a new note and returns the dialog, as soon as it is visible. */
async function openDeleteDialog(page: Page, request: APIRequestContext): Promise<Locator> {
  const title = `zzz Motion ${Date.now()}-${test.info().retry}-${test.info().parallelIndex}`
  await createNote(request, title)
  await page.goto(deletesPage)
  await page
    .getByRole('row', { name: new RegExp(title) })
    .getByRole('button', { name: 'Delete', exact: true })
    .click()
  return visibleDialog(page)
}

/**
 * Evaluates at once, with no polling: whether the dialog and every element around it is fully opaque, and no finite
 * animation runs or is paused in the dialog.
 */
function isFullyShown(dialog: Locator): Promise<boolean> {
  return dialog.evaluate((element) => {
    for (let node: Element | null = element; node; node = node.parentElement) {
      if (getComputedStyle(node).opacity !== '1') return false
    }
    return !element.getAnimations({ subtree: true }).some((animation) => {
      const pending = animation.playState === 'running' || animation.playState === 'paused'
      return pending && Number.isFinite(animation.effect?.getComputedTiming().iterations)
    })
  })
}

test('a site dialog is fully shown as soon as it is visible', async ({ page, request }) => {
  const dialog = await openDeleteDialog(page, request)

  expect(await isFullyShown(dialog)).toBe(true)

  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(dialog).toBeHidden()
})

test.describe('when the browser asks for motion', () => {
  test.use({ reducedMotion: 'no-preference' })

  test('the same check finds the dialog still opening, so it catches motion', async ({ page, request }) => {
    const cdp = await page.context().newCDPSession(page)
    await cdp.send('Animation.enable')
    await cdp.send('Animation.setPlaybackRate', { playbackRate: 0.1 })
    const dialog = await openDeleteDialog(page, request)

    expect(await isFullyShown(dialog)).toBe(false)
  })
})
