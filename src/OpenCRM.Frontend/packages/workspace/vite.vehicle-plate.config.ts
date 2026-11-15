import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import { resolveFrontendOutputDir } from '../../build/frontendOutput'

export default defineConfig({
  build: {
    outDir: resolveFrontendOutputDir(fileURLToPath(new URL('../../../OpenCRM.WebApp/wwwroot/app', import.meta.url)), 'app'),
    emptyOutDir: false,
    lib: {
      entry: fileURLToPath(new URL('./src/legacyVehicleRegistrationPlate.ts', import.meta.url)),
      name: 'OpenCRMVehiclePlate',
      formats: ['iife'],
      fileName: () => 'vehicle-registration-plate.bundle.js'
    }
  }
})
