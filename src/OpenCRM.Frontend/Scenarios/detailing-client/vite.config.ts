import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolveFrontendOutputDir } from '../../build/frontendOutput'

function toSafeAssetName(name = 'asset') {
  return name.replace(/[^a-z0-9._-]/gi, '-').toLowerCase()
}

function resolveAssetFileName(assetInfo: { name?: string }) {
  const name = assetInfo.name ?? 'asset'
  const extensionIndex = name.lastIndexOf('.')
  const extension = extensionIndex >= 0 ? name.slice(extensionIndex).toLowerCase() : ''
  const baseName = extensionIndex >= 0 ? name.slice(0, extensionIndex) : name

  return `assets/${toSafeAssetName(baseName)}-[hash]${extension}`
}

export default defineConfig({
  plugins: [vue()],
  base: '/clients/detailing/',
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@opencrm/ui/styles.css': fileURLToPath(new URL('../../packages/ui/src/styles.css', import.meta.url)),
      '@opencrm/ui/projectBrand': fileURLToPath(new URL('../../packages/ui/src/projectBrand.ts', import.meta.url)),
      '@opencrm/ui': fileURLToPath(new URL('../../packages/ui/src/index.ts', import.meta.url))
    }
  },
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:44374',
        changeOrigin: true,
        secure: false
      }
    }
  },
  build: {
    outDir: resolveFrontendOutputDir('../../../OpenCRM.WebApp/wwwroot/clients/detailing', 'clients/detailing'),
    emptyOutDir: true,
    manifest: 'manifest.json',
    sourcemap: false,
    rollupOptions: {
      output: {
        entryFileNames: 'assets/[name]-[hash].js',
        chunkFileNames: chunkInfo => `assets/${toSafeAssetName(chunkInfo.name)}-[hash].js`,
        assetFileNames: resolveAssetFileName,
        hashCharacters: 'hex'
      }
    }
  }
})
