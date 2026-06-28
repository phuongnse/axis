import fs from 'node:fs';
import path from 'node:path';
import { TanStackRouterVite } from '@tanstack/router-plugin/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

const allowedHosts = process.env.VITE_ALLOWED_HOSTS?.split(',')
  .map((host) => host.trim())
  .filter(Boolean);

function readHttpsOptions(): { cert: Buffer; key: Buffer } | undefined {
  const certPath = process.env.VITE_DEV_HTTPS_CERT;
  const keyPath = process.env.VITE_DEV_HTTPS_KEY;

  if (!certPath && !keyPath) return undefined;
  if (!certPath || !keyPath) {
    throw new Error('Both VITE_DEV_HTTPS_CERT and VITE_DEV_HTTPS_KEY are required for HTTPS.');
  }

  return {
    cert: fs.readFileSync(certPath),
    key: fs.readFileSync(keyPath),
  };
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [TanStackRouterVite(), react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 3000,
    https: readHttpsOptions(),
    allowedHosts,
    proxy: {
      '/api': {
        target: process.env.VITE_API_PROXY_TARGET ?? 'https://localhost:7275',
        changeOrigin: true,
      },
      '/connect': {
        target: process.env.VITE_API_PROXY_TARGET ?? 'https://localhost:7275',
        changeOrigin: true,
      },
    },
    watch: process.env.VITE_USE_POLLING ? { usePolling: true, interval: 300 } : undefined,
  },
  test: {
    include: ['tests/**/*.test.{ts,tsx}', 'src/**/*.test.{ts,tsx}'],
  },
});
