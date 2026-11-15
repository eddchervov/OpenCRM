import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  define: {
    'process.env.NODE_ENV': JSON.stringify('production')
  },
  build: {
    outDir: fileURLToPath(new URL('../../../OpenCRM.WebApp/wwwroot/app', import.meta.url)),
    emptyOutDir: false,
    lib: {
      entry: fileURLToPath(new URL('./src/razorDateTimeFields.ts', import.meta.url)),
      name: 'OpenCRMDateTimeFields',
      formats: ['iife'],
      fileName: () => 'date-time-fields.bundle.js',
      cssFileName: 'date-time-fields'
    }
  }
})
