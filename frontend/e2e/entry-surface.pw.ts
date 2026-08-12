import { expect, type Locator, type Page, test } from '@playwright/test';
import { expectCanonicalTestLanguage } from './canonical-test-language';

async function mockGuestEntry(page: Page, language: 'en' | 'vi' = 'en'): Promise<void> {
  await page.addInitScript((initialLanguage) => {
    (window as Window & { __AXIS_DISABLE_DEVTOOLS__?: boolean }).__AXIS_DISABLE_DEVTOOLS__ = true;
    localStorage.setItem('axis.language', initialLanguage);
    localStorage.setItem('axis.theme', 'light');
  }, language);

  await page.route('**/api/auth/session', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ authenticated: false, csrfToken: 'entry-surface-csrf-token' }),
    });
  });
  await page.route('**/api/legal/versions', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ termsVersion: '2026-01', privacyVersion: '2026-01' }),
    });
  });
}

function observeUnexpectedRuntimeErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  await expect
    .poll(async () =>
      page.evaluate(() => ({
        bodyFits: document.body.scrollWidth <= window.innerWidth + 1,
        documentFits: document.documentElement.scrollWidth <= window.innerWidth + 1,
      })),
    )
    .toEqual({ bodyFits: true, documentFits: true });
}

async function expectEntrySurfaceFits(page: Page): Promise<void> {
  const surface = page.locator('[data-slot="entry-surface"]');
  const overflow = await surface.evaluate((element) => ({
    clientWidth: element.clientWidth,
    offenders: Array.from(element.querySelectorAll<HTMLElement>('*'))
      .map((child) => ({
        clientWidth: child.clientWidth,
        scrollWidth: child.scrollWidth,
        slot: child.dataset.slot ?? child.tagName,
      }))
      .filter(
        ({ clientWidth, scrollWidth, slot }) =>
          slot !== 'checkbox' && scrollWidth > clientWidth + 1,
      ),
    scrollWidth: element.scrollWidth,
  }));

  expect(overflow.scrollWidth, JSON.stringify(overflow.offenders)).toBeLessThanOrEqual(
    overflow.clientWidth + 1,
  );
  expect(overflow.offenders, JSON.stringify(overflow.offenders)).toEqual([]);
}

async function expectEntryTargetGeometry(page: Page, minimumHeight: 32 | 44): Promise<void> {
  const targets = page
    .locator('[data-slot="entry-layout"]')
    .locator(
      '[data-slot="input"], [data-slot="button"], [data-slot="entry-consent-label"], [data-slot="entry-utilities"] button',
    );
  const measurements = await targets.evaluateAll((elements) =>
    elements.map((element) => ({
      height: element.getBoundingClientRect().height,
      name:
        element.getAttribute('aria-label') ??
        element.getAttribute('name') ??
        element.textContent?.trim() ??
        '',
      width: element.getBoundingClientRect().width,
    })),
  );

  expect(measurements.length).toBeGreaterThanOrEqual(7);
  expect(
    measurements.every(({ height }) => height >= minimumHeight - 1),
    `all Entry controls meet the ${minimumHeight}px height: ${JSON.stringify(measurements)}`,
  ).toBe(true);
  expect(
    measurements
      .filter(({ name }) => name === 'Preferences' || name === 'Create account')
      .every(({ width }) => width >= minimumHeight - 1),
    `Entry actions meet the ${minimumHeight}px width: ${JSON.stringify(measurements)}`,
  ).toBe(true);
}

async function expectEntryConsentFirstLineAlignment(page: Page): Promise<void> {
  const alignment = await page.locator('[data-slot="entry-consent-label"]').evaluate((label) => {
    const checkbox = label.parentElement?.querySelector<HTMLElement>('[data-slot="checkbox"]');
    const copy = label.firstElementChild;
    if (!(copy instanceof HTMLElement) || !checkbox) return null;

    const checkboxBounds = checkbox.getBoundingClientRect();
    const copyBounds = copy.getBoundingClientRect();
    const copyLineHeight = Number.parseFloat(getComputedStyle(copy).lineHeight);
    return {
      checkboxCenter: checkboxBounds.top + checkboxBounds.height / 2,
      copyFirstLineCenter: copyBounds.top + copyLineHeight / 2,
    };
  });

  expect(alignment).not.toBeNull();
  expect(
    Math.abs((alignment?.checkboxCenter ?? 0) - (alignment?.copyFirstLineCenter ?? 0)),
    `consent checkbox aligns with the first copy line: ${JSON.stringify(alignment)}`,
  ).toBeLessThanOrEqual(1);
}

async function expectVisibleFocusIndicator(target: Locator): Promise<void> {
  const focusStyle = await target.evaluate((element) => {
    const style = getComputedStyle(element);
    return {
      boxShadow: style.boxShadow,
      outlineStyle: style.outlineStyle,
      outlineWidth: Number.parseFloat(style.outlineWidth),
    };
  });
  expect(
    focusStyle.boxShadow !== 'none' ||
      (focusStyle.outlineStyle !== 'none' && focusStyle.outlineWidth > 0),
  ).toBe(true);
}

async function expectVisibleTextContrast(
  root: Locator,
  minimumTextNodeCount: number,
): Promise<void> {
  await expect
    .poll(async () => {
      const results = await root.evaluate((root) => {
        const canvas = document.createElement('canvas');
        canvas.width = 1;
        canvas.height = 1;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        if (!context) throw new Error('Expected a 2D canvas context');

        const rgba = (color: string): [number, number, number, number] => {
          context.clearRect(0, 0, 1, 1);
          context.fillStyle = color;
          context.fillRect(0, 0, 1, 1);
          const value = context.getImageData(0, 0, 1, 1).data;
          return [value[0], value[1], value[2], value[3] / 255];
        };
        const composite = (
          foreground: [number, number, number, number],
          background: [number, number, number, number],
        ): [number, number, number, number] => {
          const alpha = foreground[3] + background[3] * (1 - foreground[3]);
          if (alpha === 0) return [0, 0, 0, 0];
          return [
            (foreground[0] * foreground[3] + background[0] * background[3] * (1 - foreground[3])) /
              alpha,
            (foreground[1] * foreground[3] + background[1] * background[3] * (1 - foreground[3])) /
              alpha,
            (foreground[2] * foreground[3] + background[2] * background[3] * (1 - foreground[3])) /
              alpha,
            alpha,
          ];
        };
        const luminance = ([red, green, blue]: [number, number, number, number]) => {
          const linear = [red, green, blue].map((channel) => {
            const value = channel / 255;
            return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
          });
          return linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722;
        };

        const textByElement = new Map<HTMLElement, string[]>();
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
          const text = node.textContent?.trim();
          const element = node.parentElement;
          if (!text || !element) continue;
          const bounds = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          if (
            bounds.width <= 1 ||
            bounds.height <= 1 ||
            style.display === 'none' ||
            style.visibility === 'hidden'
          ) {
            continue;
          }
          textByElement.set(element, [...(textByElement.get(element) ?? []), text]);
        }

        return [...textByElement].map(([element, text]) => {
          const layers: [number, number, number, number][] = [];
          for (let node: Element | null = element; node; node = node.parentElement) {
            layers.push(rgba(getComputedStyle(node).backgroundColor));
          }
          let background: [number, number, number, number] = [255, 255, 255, 1];
          for (const layer of layers.reverse()) background = composite(layer, background);
          const foreground = composite(rgba(getComputedStyle(element).color), background);
          const foregroundLuminance = luminance(foreground);
          const backgroundLuminance = luminance(background);

          return {
            ratio:
              (Math.max(foregroundLuminance, backgroundLuminance) + 0.05) /
              (Math.min(foregroundLuminance, backgroundLuminance) + 0.05),
            text: text.join(' '),
          };
        });
      });

      return {
        failures: results.filter(({ ratio }) => ratio < 4.5),
        hasCoverage: results.length >= minimumTextNodeCount,
      };
    })
    .toEqual({ failures: [], hasCoverage: true });
}

async function expectEntrySurfaceScreenshot(
  page: Page,
  name: string,
  { canonicalLanguage = true }: { canonicalLanguage?: boolean } = {},
): Promise<void> {
  if (canonicalLanguage) await expectCanonicalTestLanguage(page);
  const layout = page.locator('[data-slot="entry-layout"]');
  const surface = page.locator('[data-slot="entry-surface"]');
  await expect(layout).toBeVisible();
  await expect(surface).toHaveAttribute('data-axis-surface-contract', 'entry-surface');
  await expect(surface).toHaveAttribute('data-axis-surface-id', 'registration');
  await layout.evaluate((element) =>
    Promise.all(element.getAnimations({ subtree: true }).map((animation) => animation.finished)),
  );
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  await page.mouse.move(1, 1);
  await expect(page).toHaveScreenshot(`${name}.png`, {
    animations: 'disabled',
    caret: 'hide',
    fullPage: true,
    scale: 'css',
  });
}

async function selectPreference(page: Page, name: 'Dark' | 'Light'): Promise<void> {
  await page.getByRole('button', { name: 'Preferences' }).click();
  await page.getByRole('button', { name, exact: true }).click();
  await page.keyboard.press('Escape');
}

test.describe('Entry Surface foundation', () => {
  test('AT-004 Entry Surface visual contract covers canonical EN light and dark desktop and compact', async ({
    page,
  }) => {
    const runtimeErrors = observeUnexpectedRuntimeErrors(page);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await mockGuestEntry(page);

    await page.setViewportSize({ width: 1280, height: 1100 });
    await page.goto('/register', { waitUntil: 'domcontentloaded' });
    await expect(page).toHaveTitle('Axis Platform');
    await expect(page.getByRole('heading', { level: 1, name: 'Create account' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Create account' })).toBeEnabled();
    await expectNoHorizontalOverflow(page);
    await expectEntrySurfaceFits(page);
    await expectEntryTargetGeometry(page, 32);
    await expectEntryConsentFirstLineAlignment(page);
    await expectVisibleTextContrast(page.locator('[data-slot="entry-layout"]'), 16);

    const fullName = page.getByLabel('Full name');
    await fullName.focus();
    await expectVisibleFocusIndicator(fullName);
    await expect(fullName).toBeInViewport();
    const ariaTree = await page.locator('[data-slot="entry-layout"]').ariaSnapshot();
    for (const semanticEntry of [
      'main',
      'heading "Create account"',
      'textbox "Full name"',
      'textbox "Email address"',
      'button "Create account"',
      'link "Sign in"',
    ]) {
      expect(ariaTree).toContain(semanticEntry);
    }
    await expectEntrySurfaceScreenshot(page, 'entry-surface-light-desktop-en');

    await selectPreference(page, 'Dark');
    await expect(page.locator('html')).toHaveClass(/dark/);
    await expectVisibleTextContrast(page.locator('[data-slot="entry-layout"]'), 16);
    await expectEntrySurfaceScreenshot(page, 'entry-surface-dark-desktop-en');

    await page.setViewportSize({ width: 390, height: 844 });
    await expectNoHorizontalOverflow(page);
    await expectEntrySurfaceFits(page);
    await expectEntryTargetGeometry(page, 44);
    await expectEntryConsentFirstLineAlignment(page);
    await expect
      .poll(() =>
        page.evaluate(() => document.documentElement.scrollHeight > window.innerHeight + 1),
      )
      .toBe(true);
    await expectEntrySurfaceScreenshot(page, 'entry-surface-dark-compact-en');

    await selectPreference(page, 'Light');
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    await expectEntrySurfaceScreenshot(page, 'entry-surface-light-compact-en');
    expect(runtimeErrors).toEqual([]);
  });

  test('AT-004 Entry Surface reflows localized form content at the 320 CSS pixel boundary', async ({
    page,
  }) => {
    const runtimeErrors = observeUnexpectedRuntimeErrors(page);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await mockGuestEntry(page, 'vi');
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto('/register', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await expect(page.getByRole('heading', { level: 1, name: 'Tạo tài khoản' })).toBeVisible();
    await page.evaluate(() => {
      for (const element of document.querySelectorAll<HTMLElement>(
        '[data-slot="entry-layout"], [data-slot="entry-layout"] *',
      )) {
        element.style.setProperty('letter-spacing', '0.12em', 'important');
        element.style.setProperty('line-height', '1.5', 'important');
        element.style.setProperty('word-spacing', '0.16em', 'important');
      }
    });
    await expectNoHorizontalOverflow(page);
    await expectEntrySurfaceFits(page);
    await expectEntryTargetGeometry(page, 44);
    await expectEntryConsentFirstLineAlignment(page);
    await expectVisibleTextContrast(page.locator('[data-slot="entry-layout"]'), 16);
    const termsCheckbox = page.getByRole('checkbox', { name: /điều khoản dịch vụ/i });
    await termsCheckbox.focus();
    await expectVisibleFocusIndicator(termsCheckbox);
    await expectEntrySurfaceScreenshot(page, 'entry-surface-light-compact-vi-reflow', {
      canonicalLanguage: false,
    });
    expect(runtimeErrors).toEqual([]);
  });
});
