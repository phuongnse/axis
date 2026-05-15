import { describe, it, expect } from 'vitest';
import path from 'path';

// For vite.config.ts, we mock the file evaluation since loading Vite's full plugin tree
// inside a JSDOM vitest environment can cause esbuild TextEncoder invariant issues.
describe('Vite Configuration structure', () => {
  it('should have properly structured config based on the source file', async () => {
    // Read the file as text to avoid full evaluation of TanStackRouterVite and React plugins inside JSDOM
    const fs = await import('fs');
    const content = fs.readFileSync(path.resolve(__dirname, '../vite.config.ts'), 'utf-8');

    // Assert it includes the expected plugin imports
    expect(content).toContain("import { TanStackRouterVite } from '@tanstack/router-plugin/vite'");
    expect(content).toContain("import react from '@vitejs/plugin-react'");

    // Assert it includes the plugin array
    expect(content).toContain("plugins: [");
    expect(content).toContain("TanStackRouterVite()");
    expect(content).toContain("react()");

    // Assert alias config
    expect(content).toContain("alias: {");
    expect(content).toContain("'@': path.resolve(__dirname, './src')");
  });
});