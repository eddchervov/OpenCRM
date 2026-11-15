import { fileURLToPath } from 'node:url'
import { readFileSync } from 'node:fs'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'

const root = fileURLToPath(new URL('../', import.meta.url))
const plateSettings = JSON.parse(readFileSync(new URL('../../OpenCRM.WebApp/appsettings.json', import.meta.url), 'utf8')).VehicleRegistrationPlates
const plateFormats = {
  maxLength: plateSettings.MaxLength,
  letterPattern: plateSettings.LetterPattern,
  characterMappings: plateSettings.CharacterMappings,
  formats: plateSettings.Formats.map(format => ({
    code: format.Code, kinds: format.Kinds, masks: format.Masks, pattern: format.Pattern, example: format.Example
  }))
}
const server = await createServer({
  configFile: false,
  root,
  plugins: [
    {
      name: 'maintenance-card-preview-imports',
      enforce: 'pre',
      configureServer(server) {
        server.middlewares.use((request, response, next) => {
          if (request.url === '/api/v1/vehicles/registration-plate-formats') {
            response.setHeader('Content-Type', 'application/json; charset=utf-8')
            response.end(JSON.stringify(plateFormats))
            return
          }
          const prefix = '/api/v1/organizations/00000000-0000-0000-0000-000000000001/data-export/'
          if (request.url === prefix + '00000000-0000-0000-0000-000000000001/file') {
            response.setHeader('Content-Type', 'application/zip')
            response.setHeader('Content-Disposition', 'attachment; filename="opencrm-qa-preview.zip"')
            response.end(Buffer.from([80, 75, 5, 6, ...Array(18).fill(0)]))
            return
          }
          if (request.url === prefix + '00000000-0000-0000-0000-000000000002/file') {
            response.statusCode = 429
            response.setHeader('Content-Type', 'application/json')
            response.end(JSON.stringify({ message: 'Сейчас скачиваются другие архивы. Повторите скачивание позже.' }))
            return
          }
          next()
        })
      },
      transform(source, id) {
        const scenario = id.replaceAll('\\', '/').match(/\/Scenarios\/([^/]+)\/src\//)?.[1]
        let transformed = source
        if (scenario) transformed = transformed.replace(/(['"])@\//g, `$1/Scenarios/${scenario}/src/`)
        transformed = transformed
          .replaceAll('@opencrm/workspace/components/', '/packages/workspace/src/components/')
          .replaceAll('@opencrm/workspace/api/', '/packages/workspace/src/api/')
          .replaceAll('@opencrm/workspace/styles.css', '/packages/workspace/src/styles.css')
          .replaceAll('@opencrm/workspace/quick-booking.css', '/packages/workspace/src/components/QuickBookingForm.css')
          .replaceAll('@opencrm/ui/styles.css', '/packages/ui/src/styles.css')
          .replaceAll('@opencrm/workspace', '/packages/workspace/src/index.ts')
          .replaceAll('@opencrm/ui', '/packages/ui/src/index.ts')
        transformed = transformed.replace(/(['"])(\/packages\/workspace\/src\/components\/[^'"]+)(['"])/g, (_, quote, path, endQuote) =>
          `${quote}${/\.[a-z]+$/i.test(path) ? path : `${path}.vue`}${endQuote}`)
        transformed = transformed.replace(/(['"])(\/packages\/workspace\/src\/api\/[^'"]+)(['"])/g, (_, quote, path, endQuote) =>
          `${quote}${/\.[a-z]+$/i.test(path) ? path : `${path}.ts`}${endQuote}`)
        return transformed === source ? null : transformed
      }
    },
    vue()
  ],
  resolve: {
    alias: [
      { find: '@opencrm/ui/styles.css', replacement: fileURLToPath(new URL('../packages/ui/src/styles.css', import.meta.url)) },
      { find: '@opencrm/workspace/components', replacement: fileURLToPath(new URL('../packages/workspace/src/components', import.meta.url)) },
      { find: '@opencrm/workspace/api', replacement: fileURLToPath(new URL('../packages/workspace/src/api', import.meta.url)) },
      { find: '@opencrm/workspace', replacement: fileURLToPath(new URL('../packages/workspace/src/index.ts', import.meta.url)) },
      { find: '@opencrm/ui', replacement: fileURLToPath(new URL('../packages/ui/src/index.ts', import.meta.url)) }
    ]
  },
  server: {
    host: '127.0.0.1',
    port: 5179,
    strictPort: true,
    watch: { ignored: ['**/bin/**', '**/obj/**'] }
  }
})

await server.listen()
console.log('Карточка обслуживания: http://127.0.0.1:5179/tests/maintenance-card-preview.html#maintenance/1')
