import { expect, test } from '@playwright/test';

const screenshotOptions = {
  animations: 'disabled',
  maxDiffPixelRatio: 0.01,
} as const;

test.describe('design system catalog', () => {
  test('DSCAT-001 catalog route exposes stable visual QA targets', async ({ page }) => {
    await page.goto('/design-system');

    await expect(page.getByRole('heading', { name: 'Axis design system' })).toBeVisible();
    await expect(page.locator('[data-visual-target="tokens"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="primitive-button"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="primitive-icon-button"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="primitive-action-link"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="primitive-form"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="theme-coverage"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="feedback"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="structure-data"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="layout"]')).toBeVisible();

    await expect(page.locator('[data-visual-target="primitive-button"]')).toHaveScreenshot(
      'primitive-button-desktop.png',
      screenshotOptions,
    );
    await expect(page.locator('[data-visual-target="primitive-form"]')).toHaveScreenshot(
      'primitive-form-desktop.png',
      screenshotOptions,
    );
    await expect(page.locator('[data-visual-target="feedback"]')).toHaveScreenshot(
      'feedback-desktop.png',
      screenshotOptions,
    );
    await expect(page.locator('[data-visual-target="structure-data"]')).toHaveScreenshot(
      'structure-data-desktop.png',
      screenshotOptions,
    );
    await expect(page.locator('[data-visual-target="layout"]')).toHaveScreenshot(
      'layout-desktop.png',
      screenshotOptions,
    );
  });

  test('DSCAT-002 catalog route exposes mobile visual QA targets', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 900 });
    await page.goto('/design-system');

    await expect(page.getByRole('heading', { name: 'Axis design system' })).toBeVisible();
    await expect(page.locator('[data-visual-target="catalog-header"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="primitive-form"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="feedback"]')).toBeVisible();
    await expect(page.locator('[data-visual-target="state-language"]')).toBeVisible();

    await expect(page.locator('[data-visual-target="primitive-form"]')).toHaveScreenshot(
      'primitive-form-mobile.png',
      screenshotOptions,
    );
    await expect(page.locator('[data-visual-target="feedback"]')).toHaveScreenshot(
      'feedback-mobile.png',
      screenshotOptions,
    );
  });
});
