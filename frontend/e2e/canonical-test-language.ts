import { expect, type Page } from '@playwright/test';
import { DEFAULT_LANGUAGE } from '../src/features/preferences/language-store';

export async function expectCanonicalTestLanguage(page: Page): Promise<void> {
  await expect(page.locator('html')).toHaveAttribute('lang', DEFAULT_LANGUAGE);
}
