import path from 'node:path';
import { describe, expect, it } from 'vitest';

// Read config as text because evaluating Vite plugins in JSDOM can trip esbuild invariants.
describe('Vite Configuration structure', () => {
  it('should have properly structured config based on the source file', async () => {
    const fs = await import('node:fs');
    const content = fs.readFileSync(path.resolve(__dirname, '../vite.config.ts'), 'utf-8');

    expect(content).toContain("import { TanStackRouterVite } from '@tanstack/router-plugin/vite'");
    expect(content).toContain("import react from '@vitejs/plugin-react'");

    expect(content).toContain('plugins: [');
    expect(content).toContain('TanStackRouterVite()');
    expect(content).toContain('react()');

    expect(content).toContain('alias: {');
    expect(content).toContain("'@': path.resolve(__dirname, './src')");
  });
});
