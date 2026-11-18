import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import ts from 'typescript'
import { fileURLToPath } from 'node:url'
import { i18n, translate } from '@opencrm/ui/i18n'
import '../packages/workspace/src/vehicleApiMessages.ts'

const sourcePath = fileURLToPath(new URL('../packages/workspace/src/api/vehicles.ts', import.meta.url))
const source = ts.transpileModule(readFileSync(sourcePath, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const module = { exports: {} }
new Function('require', 'module', 'exports', source)(id => {
  if (id === '@opencrm/ui/i18n') return { translate }
  if (id === '@opencrm/workspace/vehicleApiMessages') return {}
  throw new Error(`Неизвестная зависимость: ${id}`)
}, module, module.exports)

const api = module.exports
const apiCases = [
  ['album', () => api.loadVehicleAlbum('org', 'vehicle', 'album')],
  ['uploadPhoto', () => api.uploadVehiclePhotos('org', 'vehicle', [])],
  ['deletePhoto', () => api.deleteVehiclePhoto('org', 'vehicle', 'photo')],
  ['mainPhoto', () => api.setPrimaryVehiclePhoto('org', 'vehicle', 'photo')],
  ['createAlbum', () => api.createVehicleAlbum('org', 'vehicle', 'Album')],
  ['deleteAlbum', () => api.deleteVehicleAlbum('org', 'vehicle', 'album')],
  ['uploadAlbumPhoto', () => api.uploadVehicleAlbumPhotos('org', 'vehicle', 'album', [])],
  ['deleteAlbumPhoto', () => api.deleteVehicleAlbumPhoto('org', 'vehicle', 'album', 'photo')],
  ['mainAlbumPhoto', () => api.setPrimaryVehicleAlbumPhoto('org', 'vehicle', 'album', 'photo')]
]

test('Vehicle media API uses translated photo and album fallback errors', async () => {
  const originalFetch = globalThis.fetch
  const originalDocument = globalThis.document
  const originalWindow = globalThis.window
  globalThis.document = { documentElement: {}, querySelector: () => null }
  globalThis.window = { location: { origin: 'https://example.invalid' } }
  globalThis.fetch = async () => ({
    ok: false,
    headers: { get: () => 'text/plain' },
    text: async () => '',
    json: async () => null
  })

  try {
    for (const locale of ['ru', 'en', 'zh-Hans']) {
      i18n.global.locale.value = locale
      for (const [key, invoke] of apiCases) {
        await assert.rejects(invoke(), error => {
          assert.equal(error.message, translate(`vehicleApi.${key}`))
          return true
        })
      }
    }
  } finally {
    globalThis.fetch = originalFetch
    if (originalDocument === undefined) delete globalThis.document
    else globalThis.document = originalDocument
    if (originalWindow === undefined) delete globalThis.window
    else globalThis.window = originalWindow
    i18n.global.locale.value = 'ru'
  }
})
