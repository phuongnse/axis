import { mergeConfig } from 'vite'
import config from './vite.config.ts'

// Builds the design-system preview page, never into the server's web root. `vite build` with the
// main config builds only index.html, so the previews stay out of the production bundle.
export default mergeConfig(config, {
  base: './',
  build: {
    outDir: '../artifacts/design-system-app',
    emptyOutDir: true,
    rollupOptions: { input: 'design-system.html' },
  },
})
