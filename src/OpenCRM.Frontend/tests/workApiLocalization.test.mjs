import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import ts from 'typescript'
import { fileURLToPath } from 'node:url'
import { i18n, translate } from '@opencrm/ui/i18n'
import '../packages/workspace/src/workMessages.ts'

const sourcePath = fileURLToPath(new URL('../packages/workspace/src/api/work.ts', import.meta.url))
const source = ts.transpileModule(readFileSync(sourcePath, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const module = { exports: {} }
new Function('require', 'module', 'exports', source)(id => {
  if (id === '@opencrm/ui/i18n') return { translate }
  if (id === '../workMessages') return {}
  throw new Error(`Неизвестная зависимость: ${id}`)
}, module, module.exports)

const api = module.exports
const work = { maintenanceId: 'order', detailId: 'line', version: 1 }
const report = { id: 'report', maintenanceId: 'order', detailId: 'line' }
const progress = { detailId: 'line', version: 1 }
const stock = { id: 'stock' }
const issue = { id: 'issue' }
const apiCases = [
  ['loadProductionReport', () => api.loadProductionReport('org', 'order')],
  ['loadPhotos', () => api.loadWorkPhotos('org', work)],
  ['uploadPhotos', () => api.uploadWorkPhotos('org', work, [new Blob(['image'])], 1)],
  ['loadReports', () => api.loadWorkReports('org', 'order')],
  ['saveReport', () => api.addWorkReport('org', work, 'comment', 'A user note')],
  ['saveDecision', () => api.decideWorkReport('org', report, 'rejected')],
  ['adjustTime', () => api.adjustWorkTime('org', 'order', 'line', 5, 'A user reason')],
  ['loadMaterials', () => api.loadAvailableOperationMaterials('org', work)],
  ['loadIssuedMaterials', () => api.loadIssuedOperationMaterials('org', work)],
  ['issueMaterial', () => api.issueOperationMaterial('org', work, stock, 1, 'kg')],
  ['returnMaterial', () => api.returnOperationMaterial('org', work, issue, 'A user reason')],
  ['loadProgress', () => api.loadWorkProgress('org', 'order')],
  ['completeWork', () => api.completeWorkForEmployee('org', 'order', progress, 'A user reason', 'command')],
  ['returnWork', () => api.returnWorkForRework('org', 'order', progress, 'A user reason')],
  ['loadAssigned', () => api.loadAssignedWork('org')],
  ['loadAvailable', () => api.loadAvailableWork('org')],
  ['claim', () => api.claimAvailableWork('org', work)],
  ['transition', () => api.transitionAssignedWork('org', work, 'pause', 'A user reason')]
]

test('Work API uses localized fallbacks in all supported languages and preserves server messages', async () => {
  const originalFetch = globalThis.fetch
  const originalDocument = globalThis.document
  globalThis.document = { documentElement: {}, querySelector: () => null }
  globalThis.fetch = async () => ({ ok: false, json: async () => null })

  try {
    for (const locale of ['ru', 'en', 'zh-Hans']) {
      i18n.global.locale.value = locale
      for (const [key, invoke] of apiCases) {
        await assert.rejects(invoke(), error => {
          assert.equal(error.message, translate(`workApi.${key}`))
          return true
        })
      }
    }

    globalThis.fetch = async () => ({ ok: false, json: async () => ({ message: 'Server supplied message' }) })
    i18n.global.locale.value = 'en'
    await assert.rejects(api.loadAssignedWork('org'), { message: 'Server supplied message' })
  } finally {
    globalThis.fetch = originalFetch
    if (originalDocument === undefined) delete globalThis.document
    else globalThis.document = originalDocument
    i18n.global.locale.value = 'ru'
  }
})
