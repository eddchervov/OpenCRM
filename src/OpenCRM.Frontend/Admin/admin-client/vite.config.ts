import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolveFrontendOutputDir } from '../../build/frontendOutput'
import { resolveSharedChunk } from '../../build/frontendChunks'

export default defineConfig({
  plugins: [vue()],
  base: '/admin-panel/',
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@opencrm/ui/i18n': fileURLToPath(new URL('../../packages/ui/src/i18n.ts', import.meta.url))
    }
  },
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:7142',
        changeOrigin: true,
        secure: false
      }
    }
  },
  build: {
    outDir: resolveFrontendOutputDir('../../../OpenCRM.WebApp/wwwroot/admin-panel', 'admin-panel'),
    emptyOutDir: true,
    manifest: 'manifest.json',
    sourcemap: false,
    rollupOptions: {
      output: {
        manualChunks: resolveSharedChunk,
        entryFileNames: 'assets/index-[hash].js',
        chunkFileNames: chunkInfo => `assets/${chunkInfo.name.replace(/[^a-z0-9._-]/gi, '-').toLowerCase()}-[hash].js`,
        assetFileNames: assetInfo => {
          const name = assetInfo.name ?? 'asset'
          const extensionIndex = name.lastIndexOf('.')
          const extension = extensionIndex >= 0 ? name.slice(extensionIndex).toLowerCase() : ''
          const baseName = extensionIndex >= 0 ? name.slice(0, extensionIndex) : name

          return `assets/${baseName.replace(/[^a-z0-9._-]/gi, '-').toLowerCase()}-[hash]${extension}`
        },
        hashCharacters: 'hex'
      }
    }
  }
})
