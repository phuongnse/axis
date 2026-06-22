import { expect, test } from '@playwright/test';

test.describe('design system catalog', () => {
  test('DSCAT-001 catalog route exposes stable visual QA targets', async ({ page }) => {
    await page.goto('/design-system');

    await expect(page.getByRole('heading', { name: 'Axis design system' })).toBeVisible();
    await expect(page.locator('[data-visual-target="tokens"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="primitive-button"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="primitive-action-link"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="primitive-form"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="theme-coverage"]')).toBeVisible();
  });

  test('DSCAT-002 catalog route exposes mobile visual QA targets', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 900 });
    await page.goto('/design-system');

    await expect(page.getByRole('heading', { name: 'Axis design system' })).toBeVisible();
    await expect(page.locator('[data-visual-target="catalog-header"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="primitive-form"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="state-language"]')).toBeVisible();
  });
});
