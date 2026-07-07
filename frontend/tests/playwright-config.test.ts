import { afterEach, describe, expect, it, vi } from 'vitest';

type WebServerConfig = {
  command: string;
  url: string;
};

type PlaywrightConfigShape = {
  use?: {
    baseURL?: string;
  };
  webServer?: WebServerConfig | WebServerConfig[];
};

const e2eEnvKeys = [
  'E2E_BASE_URL',
  'E2E_SKIP_WEB_SERVER',
  'E2E_WEB_SERVER_HOST',
  'E2E_WEB_SERVER_PORT',
] as const;

const originalEnv = new Map(e2eEnvKeys.map((key) => [key, process.env[key]]));

function resetE2EEnv() {
  for (const key of e2eEnvKeys) {
    const originalValue = originalEnv.get(key);

    if (originalValue === undefined) {
      delete process.env[key];
    } else {
      process.env[key] = originalValue;
    }
  }
}

async function loadDefaultConfig() {
  vi.resetModules();
  const module = await import('../playwright.config.ts?default');

  return module.default as PlaywrightConfigShape;
}

async function loadComposeConfig() {
  vi.resetModules();
  const module = await import('../playwright.config.ts?compose');

  return module.default as PlaywrightConfigShape;
}

function getSingleWebServer(config: PlaywrightConfigShape) {
  expect(Array.isArray(config.webServer)).toBe(false);
  expect(config.webServer).toBeDefined();

  return config.webServer as WebServerConfig;
}

describe('Playwright configuration', () => {
  afterEach(() => {
    resetE2EEnv();
  });

  it('starts host e2e on an isolated strict local web server by default', async () => {
    resetE2EEnv();

    const config = await loadDefaultConfig();
    const webServer = getSingleWebServer(config);

    expect(config.use?.baseURL).toBe('http://127.0.0.1:4173');
    expect(webServer.url).toBe('http://127.0.0.1:4173');
    expect(webServer.command).toBe('npm run dev -- --host 127.0.0.1 --port 4173 --strictPort');
  });

  it('uses the compose e2e target without starting a host web server', async () => {
    resetE2EEnv();
    process.env.E2E_BASE_URL = 'https://web:3000';
    process.env.E2E_SKIP_WEB_SERVER = '1';

    const config = await loadComposeConfig();

    expect(config.use?.baseURL).toBe('https://web:3000');
    expect(config.webServer).toBeUndefined();
  });
});
