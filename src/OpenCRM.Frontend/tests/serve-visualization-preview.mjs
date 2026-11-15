import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'

const root = fileURLToPath(new URL('../', import.meta.url))
const server = await createServer({
  configFile: false,
  root,
  plugins: [
    {
      name: 'preview-scenario-imports',
      enforce: 'pre',
      resolveId(source, importer) {
        if (!source.startsWith('@/')) return
        const scenario = importer?.replaceAll('\\', '/').match(/\/Scenarios\/([^/]+)\/src\//)?.[1] ?? 'detailing-client'
        const isAdmin = importer?.replaceAll('\\', '/').includes('/Admin/admin-client/')
        const target = fileURLToPath(new URL(isAdmin ? `../Admin/admin-client/src/${source.slice(2)}` : `../Scenarios/${scenario}/src/${source.slice(2)}`, import.meta.url))
        return this.resolve(target, importer, { skipSelf: true })
      }
    },
    vue()
  ],
  resolve: {
    alias: {
      '@opencrm/ui/styles.css': fileURLToPath(new URL('../packages/ui/src/styles.css', import.meta.url)),
      '@opencrm/ui': fileURLToPath(new URL('../packages/ui/src/index.ts', import.meta.url))
    }
  },
  server: {
    host: '127.0.0.1',
    port: 5178,
    strictPort: true,
    watch: { ignored: ['**/bin/**', '**/obj/**'] }
  }
})

await server.listen()
console.log('Локальная демонстрация: http://127.0.0.1:5178/tests/visualization-preview.html#visualization')
