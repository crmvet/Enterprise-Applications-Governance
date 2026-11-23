// Vite config for the local page harness.
// No React plugin: esbuild compiles TSX with the automatic JSX runtime, so no Babel packages are needed.
// The server and the preview listen on 127.0.0.1 only.
import { defineConfig } from 'vite';

export default defineConfig({
  base: './',
  esbuild: { jsx: 'automatic' },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      // Fluent packages start with a 'use client' directive for server frameworks. It has no meaning in this bundle.
      onwarn(warning, warn) {
        if (warning.code === 'MODULE_LEVEL_DIRECTIVE') return;
        warn(warning);
      },
    },
  },
  server: { host: '127.0.0.1', port: 4318, strictPort: true, open: false },
  preview: { host: '127.0.0.1', port: 4318, strictPort: true, open: false },
});
