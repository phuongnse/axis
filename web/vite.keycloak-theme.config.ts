import { cpSync } from 'node:fs'
import path from 'node:path'
import { mergeConfig, type Plugin } from 'vite'
import config from './vite.config.ts'

const themeFolder = path.resolve(import.meta.dirname, '../artifacts/keycloak-theme/axis/login')

// Copies what Keycloak reads as it is: the theme's properties, its templates and its message bundles.
function copyThemeFiles(): Plugin {
  return {
    name: 'copy-theme-files',
    closeBundle() {
      for (const name of [
        'theme.properties',
        'login.ftl',
        'login-otp.ftl',
        'login-config-totp.ftl',
        'login-reset-password.ftl',
        'login-update-password.ftl',
        'frame.ftl',
        'messages',
      ]) {
        cpSync(path.resolve(import.meta.dirname, 'keycloak-theme', name), path.join(themeFolder, name), {
          recursive: true,
        })
      }
    },
  }
}

// Builds the Keycloak login theme `axis` into artifacts/keycloak-theme, never into the server's web root. The
// script and the style sheet get fixed names because login.ftl names them. The fonts keep their hashed names.
export default mergeConfig(config, {
  base: './',
  plugins: [copyThemeFiles()],
  build: {
    outDir: path.join(themeFolder, 'resources'),
    emptyOutDir: true,
    rolldownOptions: {
      input: 'keycloak-theme/main.tsx',
      output: {
        // The main config names a drag and drop chunk, and a single script cannot have one.
        codeSplitting: false,
        entryFileNames: 'assets/axis-sign-in.js',
        assetFileNames: (info: { names?: string[] }) =>
          info.names?.some((name) => name.endsWith('.css'))
            ? 'assets/axis-sign-in.css'
            : 'assets/[name]-[hash][extname]',
      },
    },
  },
})
