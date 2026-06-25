import { expect, test } from '@playwright/test';

const apiURL = process.env.E2E_API_URL;

test.describe('local dev smoke', () => {
  test('SMOKE-001 public app shell loads', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('img', { name: 'Axis' })).toBeVisible();
    await expect(page).toHaveURL(/\/register$/);
    await expect(page.getByRole('heading', { name: 'Create account' })).toBeVisible();
  });

  test('SMOKE-002 API health is reachable when configured', async ({ request }) => {
    test.skip(!apiURL, 'Set E2E_API_URL to include API health in the smoke run.');

    const response = await request.get(`${apiURL}/health`);

    expect(response.ok()).toBe(true);
  });
});
