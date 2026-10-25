import { mergeConfig, type Plugin } from 'vite'
import config from './vite.config.ts'

// React Router loads route modules with a dynamic import in its framework mode, which the previews never
// use. A classic script that a page loads must hold no import, so the bundle rejects that call instead.
function dropRouteModuleImport(): Plugin {
  return {
    name: 'drop-route-module-import',
    enforce: 'pre',
    transform(code, id) {
      if (!id.includes('react-router')) {
        return null
      }
      const replaced = code.replace(
        /import\(\s*(?:\/\*[^]*?\*\/\s*)*route\.module\s*\)/g,
        "Promise.reject(new Error('The bundle holds no route modules.'))",
      )
      return replaced === code ? null : { code: replaced, map: null }
    },
  }
}

// A script that a page inlines must not hold `</script` or `<!--`. Escaping them in a string or a
// regular expression keeps its meaning.
function escapeScriptEndings(): Plugin {
  return {
    name: 'escape-script-endings',
    enforce: 'post',
    generateBundle(_, bundle) {
      for (const chunk of Object.values(bundle)) {
        if (chunk.type === 'chunk') {
          chunk.code = chunk.code.replace(/<\/script/gi, '\\x3C/script').replace(/<!--/g, '\\x3C!--')
        }
      }
    },
  }
}

// Builds the design-system bundle: one classic script that sets `window.Axis`, never into the server's
// web root. `vite build` with the main config builds only index.html, so the previews stay out of the
// production bundle. scripts/design-system.sh checks the result.
export default mergeConfig(config, {
  // Library mode does not replace this, and React and Ant Design read it.
  define: { 'process.env.NODE_ENV': JSON.stringify('production') },
  plugins: [dropRouteModuleImport(), escapeScriptEndings()],
  build: {
    outDir: '../artifacts/design-system',
    emptyOutDir: true,
    copyPublicDir: false,
    lib: {
      entry: 'src/designSystem/bundle.ts',
      name: 'Axis',
      formats: ['iife'],
      fileName: () => 'axis-design-system.js',
    },
    rollupOptions: { output: { exports: 'default' } },
    // The main config names a drag and drop chunk, and a single script cannot have one.
    rolldownOptions: { output: { codeSplitting: false } },
  },
})
