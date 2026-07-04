import { expect, test } from '@playwright/test';

test.describe('select site theme', () => {
  test('AT-001 visitor selects a supported theme and keeps it after reload', async ({ page }) => {
    await page.goto('/sign-in');

    await expect(page.locator('html')).toHaveAttribute('data-theme-mode', 'system');
    await page.getByLabel('Email address').fill('theme@example.com');
    await page.getByRole('button', { name: 'Preferences' }).click();
    await page.getByRole('button', { name: 'Dark' }).click();
    const selectedThemeOption = page.getByRole('button', { name: 'Dark' });
    await expect(selectedThemeOption).toHaveAttribute('aria-pressed', 'true');
    expect(
      await selectedThemeOption.evaluate((node) => getComputedStyle(node).backgroundColor),
    ).not.toBe('rgba(0, 0, 0, 0)');

    await expect(page.getByLabel('Email address')).toHaveValue('theme@example.com');
    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(page.locator('html')).toHaveAttribute('data-theme-mode', 'dark');
    expect(await page.evaluate(() => localStorage.getItem('axis.theme'))).toBe('dark');
    expect(await page.evaluate(() => document.documentElement.style.colorScheme)).toBe('dark');

    await page.reload();

    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(page.locator('html')).toHaveAttribute('data-theme-mode', 'dark');
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem('axis.theme'))).toBe('dark');
  });
});
