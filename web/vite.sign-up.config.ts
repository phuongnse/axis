import { mergeConfig } from 'vite'
import config from './vite.config.ts'

// Builds the sign-up page into the control plane's web root, which serves it at /sign-up. It keeps the main config's
// font rule and the public folder, whose fonts/ holds the font licence texts that scripts/check-fonts.mjs looks for.
export default mergeConfig(config, {
  build: {
    outDir: '../controlplane/src/Axis.ControlPlane/wwwroot',
    emptyOutDir: true,
    rolldownOptions: {
      input: 'sign-up.html',
    },
  },
})
