import { afterEach, describe, expect, it } from 'vitest';
import { shouldRenderDevtools } from '../src/lib/devtools';

describe('shouldRenderDevtools', () => {
  afterEach(() => {
    delete window.__AXIS_DISABLE_DEVTOOLS__;
  });

  it('returns false when visual capture disables debug tooling', () => {
    window.__AXIS_DISABLE_DEVTOOLS__ = true;

    expect(shouldRenderDevtools()).toBe(false);
  });

  it('follows the Vite development mode when debug tooling is not disabled', () => {
    expect(shouldRenderDevtools()).toBe(import.meta.env.DEV);
  });
});
