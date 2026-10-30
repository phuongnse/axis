import { expect, test } from '../test'

const vietnamese = 'Nguyễn Thị Hương'

test('the SPA draws its text in IBM Plex Sans from its own origin, Vietnamese included', async ({ page, baseURL }) => {
  const origin = new URL(baseURL!).origin
  const fontRequests: { url: string; status: number | undefined }[] = []
  page.on('response', (response) => {
    if (response.request().resourceType() === 'font') {
      fontRequests.push({ url: response.url(), status: response.status() })
    }
  })

  await page.goto('/?signedOut')
  const welcome = page.getByRole('main').getByText('You have signed out', { exact: true })
  await expect(welcome).toBeVisible()

  const family = await welcome.evaluate((element) => getComputedStyle(element).fontFamily)
  expect(family).toMatch(/^"?IBM Plex Sans"?,/)

  await page.evaluate(() => document.fonts.ready)
  await expect
    .poll(() => fontRequests.some((request) => request.url.startsWith(`${origin}/`) && request.url.includes('ibm-plex-sans')))
    .toBe(true)
  expect(fontRequests.filter((request) => request.url.includes('ibm-plex-sans')).every((request) => request.status === 200)).toBe(true)

  // check() is true when no face matches, so load the faces for the text and look at them.
  const faces = await page.evaluate(async (text) => {
    const loaded = await document.fonts.load('16px "IBM Plex Sans"', text)
    return loaded.map((face) => ({ family: face.family, status: face.status, unicodeRange: face.unicodeRange }))
  }, vietnamese)
  expect(
    faces.some(
      (face) => /IBM Plex Sans/.test(face.family) && face.status === 'loaded' && face.unicodeRange.includes('U+1EA0-1EF9'),
    ),
  ).toBe(true)
  expect(await page.evaluate((text) => document.fonts.check('16px "IBM Plex Sans"', text), vietnamese)).toBe(true)

  expect(fontRequests.length).toBeGreaterThan(0)
  expect(fontRequests.filter((request) => !request.url.startsWith(`${origin}/`))).toEqual([])
})

test('the license texts of the fonts are served with the SPA', async ({ request }) => {
  for (const name of ['OFL-IBM-Plex-Sans.txt', 'OFL-IBM-Plex-Mono.txt']) {
    const response = await request.get(`/fonts/${name}`)
    expect(response.status()).toBe(200)
    expect(await response.text()).toContain('SIL OPEN FONT LICENSE Version 1.1')
  }
})
