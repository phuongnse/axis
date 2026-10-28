import { expect, type Locator, type Page } from '@playwright/test';

/** Matches a form field's label, with or without the required mark `*` that follows it. */
export function fieldLabel(label: string): RegExp {
  return new RegExp(`^\\s*${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*\\*?\\s*$`);
}

/** A read-only form value, found by its field name. */
export function fieldValue(scope: Page | Locator, name: string): Locator {
  return scope.getByTestId(`field-value-${name}`);
}

/** A read-only or computed child collection cell, found by its collection, zero-based row and child field. */
export function cellValue(scope: Page | Locator, collection: string, row: number, field: string): Locator {
  return scope.getByTestId(`cell-${collection}-${row}-${field}`);
}

/** The value a read-only element shows: an input's value, an inner input's value, or the element's text. */
export function shownValue(value: Locator): Promise<string> {
  return value.evaluate((element) => {
    const input = element instanceof HTMLInputElement ? element : element.querySelector('input');
    return input ? input.value : (element.textContent ?? '').trim();
  });
}

export async function expectShown(value: Locator, expected: string | RegExp, options?: { timeout?: number }) {
  if (typeof expected === 'string') {
    await expect.poll(() => shownValue(value), options).toBe(expected);
  } else {
    await expect.poll(() => shownValue(value), options).toMatch(expected);
  }
}
