import { expect, test } from '../test'
import { psql } from '../postgres'
import { required } from '../server-settings'
import { chooseLanguage } from '../shell'

const controlPlaneURL = required('AXIS_E2E_CONTROL_PLANE_URL', 'the address of the control plane')

test.describe('the sign-up page', () => {
  test.beforeAll(() => {
    psql(
      'axis_directory',
      `INSERT INTO directory.regions (code, tenant_domain, api_url) VALUES ('vn', 'vn.example.com', 'https://api.vn.example.com') ON CONFLICT DO NOTHING; INSERT INTO directory.cells (id, region_code, internal_url, cap) VALUES ('vn-1', 'vn', 'https://vn-1.internal', 100) ON CONFLICT DO NOTHING; INSERT INTO directory.tenants (id, region_code, cell_id) VALUES ('cong-ty-an-binh', 'vn', 'vn-1') ON CONFLICT DO NOTHING`,
    )
  })

  test.describe('in a Vietnamese browser', () => {
    test.use({ locale: 'vi-VN' })

    test('shows the page in Vietnamese, and the language menu switches it to English', async ({ page }) => {
      await page.goto(`${controlPlaneURL}/sign-up`)
      await expect(page.getByRole('heading', { name: 'Đăng ký', exact: true })).toBeVisible()
      await expect(page.locator('html')).toHaveAttribute('lang', 'vi')

      await chooseLanguage(page, 'English', 'Ngôn ngữ và giao diện', page)
      await expect(page.getByRole('heading', { name: 'Sign up', exact: true })).toBeVisible()
      await expect(page.locator('html')).toHaveAttribute('lang', 'en')
    })
  })

  test('a business name that matches a tenant in the directory is marked taken, while the field keeps focus', async ({
    page,
  }) => {
    await page.goto(`${controlPlaneURL}/sign-up`)
    const businessName = page.getByRole('textbox', { name: 'Business name', exact: true })
    await businessName.fill('Công ty An Bình')

    await expect(page.getByRole('textbox', { name: 'Subdomain', exact: true })).toHaveValue('cong-ty-an-binh')
    await expect(page.getByText('This subdomain is taken. Choose another.', { exact: true })).toBeVisible()
    await expect(businessName).toBeFocused()
  })

  test('a business name whose subdomain no tenant has is marked free', async ({ page }) => {
    await page.goto(`${controlPlaneURL}/sign-up`)
    await page.getByRole('textbox', { name: 'Business name', exact: true }).fill('Công ty Bình Minh')

    await expect(page.getByRole('textbox', { name: 'Subdomain', exact: true })).toHaveValue('cong-ty-binh-minh')
    await expect(page.getByText('This subdomain is free.', { exact: true })).toBeVisible()
  })
})
