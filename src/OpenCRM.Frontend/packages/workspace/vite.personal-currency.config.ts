import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolveFrontendOutputDir } from '../../build/frontendOutput'

export default defineConfig({
  define: { 'process.env.NODE_ENV': '"production"' },
  plugins: [vue()],
  build: {
    outDir: resolveFrontendOutputDir(fileURLToPath(new URL('../../../OpenCRM.WebApp/wwwroot/app', import.meta.url)), 'app'),
    emptyOutDir: false,
    lib: {
      entry: fileURLToPath(new URL('./src/personalCurrencySettings.ts', import.meta.url)),
      name: 'OpenCRMPersonalCurrency',
      formats: ['iife'],
      fileName: () => 'personal-currency.bundle.js',
      cssFileName: 'personal-currency'
    }
  }
})
