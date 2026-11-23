// Vite config: one source, two builds.
//   npm run build       the harness: the four pages on fictional fixture JSON, into dist/harness (screenshots, design
//                       review). Entry src/main.tsx. Relative base, so dist/harness opens from any folder.
//   npm run build:app   the web app: the same pages on the product's REST API, into dist/app, served under /app/ on the
//                       same origin as the API. Entry src/app/main.tsx. The fixture client is not in this build, and
//                       neither is config.json: the host writes it from its own settings.
//   npm run dev:app     the web app on the Vite dev server at http://127.0.0.1:4319/app/, with public/config.json
//                       (development sign-in) and /api proxied to the local server on http://127.0.0.1:5080.
//
// No React plugin: esbuild compiles TSX with the automatic JSX runtime, so no Babel packages are needed.
// Tailwind CSS 4 builds the static style sheet from src/styles/globals.css at build time (no styles written at run time).
// Every server listens on 127.0.0.1 only.
import tailwindcss from '@tailwindcss/vite';
import { defineConfig, type Plugin } from 'vite';

// The local server's address for the dev loop (src/adapters/azure, "Run it locally").
const LOCAL_API = 'http://127.0.0.1:5080';

// index.html names the harness entry. The web app build points it at the web app's entry instead, before Vite reads
// the page, so both builds share one page shell.
function entryFor(app: boolean): Plugin {
  return {
    name: 'product-entry',
    transformIndexHtml: {
      order: 'pre',
      handler(html) {
        if (!app) return html;
        if (!html.includes('./src/main.tsx')) throw new Error('index.html no longer names ./src/main.tsx.');
        // A data: icon would need img-src data: in the host's Content-Security-Policy, so the web app ships its icon as
        // a same-origin file (Vite copies it into assets/ with a content hash).
        return html
          .replace('./src/main.tsx', './src/app/main.tsx')
          .replace('<title>Product page harness</title>', '<title>Product</title>')
          .replace('<link rel="icon" href="data:," />', '<link rel="icon" type="image/svg+xml" href="./src/app/favicon.svg" />');
      },
    },
  };
}

export default defineConfig(({ command, mode }) => {
  const app = mode === 'app';
  return {
    base: app ? '/app/' : './',
    // public/ holds the dev loop's config.json only. It is served by the dev server and never copied into a build.
    publicDir: app && command === 'serve' ? 'public' : false,
    plugins: [entryFor(app), tailwindcss()],
    esbuild: { jsx: 'automatic' },
    build: {
      outDir: app ? 'dist/app' : 'dist/harness',
      emptyOutDir: true,
      sourcemap: false,
      chunkSizeWarningLimit: 1500,
      rollupOptions: {
        // Radix packages start with a 'use client' directive for server frameworks. It has no meaning in this bundle.
        onwarn(warning, warn) {
          if (warning.code === 'MODULE_LEVEL_DIRECTIVE') return;
          warn(warning);
        },
      },
    },
    server: app
      ? {
        host: '127.0.0.1',
        port: 4319,
        strictPort: true,
        open: false,
        // Same origin for the page, so no CORS at all: Vite's default would answer the browser preflight of another
        // localhost origin, which could then send the development identity header through the proxy and read the answer.
        cors: false,
        // changeOrigin false keeps the loopback Host header and xfwd false adds no X-Forwarded-* header: the local
        // server's development identity takes only such requests.
        proxy: { '/api': { target: LOCAL_API, changeOrigin: false, xfwd: false } },
      }
      : { host: '127.0.0.1', port: 4318, strictPort: true, open: false },
    preview: { host: '127.0.0.1', port: 4318, strictPort: true, open: false },
  };
});
