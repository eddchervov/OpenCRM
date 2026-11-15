import assert from 'node:assert/strict'
import test from 'node:test'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { parse, compileScript } from '@vue/compiler-sfc'
import * as vue from 'vue'
import { renderToString } from '@vue/server-renderer'
import { getWorkdayProgress, shouldOpenRecord } from '../packages/workspace/src/utils/workdayCard.ts'

const source = readFileSync(new URL('../packages/workspace/src/utils/vehicleBrandLogo.ts', import.meta.url), 'utf8')
const code = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: true }
}).outputText
const module = { exports: {} }
new Function('require', 'module', 'exports', code)(id => id, module, module.exports)
const { getVehicleBrandLogo } = module.exports

test('Карточка скрывает ложный прогресс и сохраняет реальные исторические операции', async () => {
  const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceWorkdayCard.vue', import.meta.url), 'utf8'))
  const compiled = ts.transpileModule(compileScript(descriptor, { id: 'workday-progress', inlineTemplate: true }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const component = { exports: {} }
  new Function('require', 'module', 'exports', compiled)(id => {
    if (id === 'vue') return vue
    if (id === '@opencrm/ui') return {}
    if (id === '../utils/vehicleBrandLogo') return { getVehicleBrandLogo: () => null }
    if (id === '../utils/workdayCard') return { getWorkdayProgress, shouldOpenRecord }
    throw new Error(`Не задана зависимость: ${id}`)
  }, component, component.exports)
  const item = { id: 'qa', vehicleName: 'QA', date: '2026-10-30', services: ['Полировка'], workTotalCount: 1, workCompletedCount: 0 }
  const render = hasTrackedWork => renderToString(vue.createSSRApp(component.exports.default, {
    item: { ...item, hasTrackedWork }, stage: 'handedOver', today: '2026-10-30'
  }))
  assert.doesNotMatch(await render(false), /role="progressbar"/)
  assert.match(await render(true), /Выполнено 0 из 1/)
  assert.match(await render(undefined), /role="progressbar"/)
})

test('Логотип выбирается только по ключу справочника, без распознавания названия машины', () => {
  assert.deepEqual(getVehicleBrandLogo('toyota'), { name: 'Toyota', src: '../assets/vehicle-brands/toyota.svg' })
  assert.equal(getVehicleBrandLogo('BMW X3'), null)
  assert.equal(getVehicleBrandLogo('unknown'), null)
  assert.equal(getVehicleBrandLogo(null), null)
  assert.equal(getVehicleBrandLogo(undefined), null)
  assert.equal(getVehicleBrandLogo(''), null)
  assert.equal(getVehicleBrandLogo('toString'), null)
  assert.equal(getVehicleBrandLogo('__proto__'), null)
})

test('Полоса показывает только завершённые работы и не зависит от этапа заказа', () => {
  assert.deepEqual(getWorkdayProgress(3, 1), { total: 3, completed: 1, percent: 33, isComplete: false })
  assert.deepEqual(getWorkdayProgress(1, 1), { total: 1, completed: 1, percent: 100, isComplete: true })
  assert.deepEqual(getWorkdayProgress(0, 1), { total: 0, completed: 0, percent: 0, isComplete: false })
  assert.deepEqual(getWorkdayProgress(2, 5), { total: 2, completed: 2, percent: 100, isComplete: true })
})

test('Доступность и обычный левый клик нужны для открытия; модификаторы и средняя кнопка сохраняют навигацию ссылки', () => {
  const click = { button: 0, ctrlKey: false, metaKey: false, shiftKey: false, altKey: false }
  assert.equal(shouldOpenRecord(true, click), true)
  assert.equal(shouldOpenRecord(false, click), false)
  for (const modifier of ['ctrlKey', 'metaKey', 'shiftKey', 'altKey']) {
    assert.equal(shouldOpenRecord(true, { ...click, [modifier]: true }), false)
  }
  assert.equal(shouldOpenRecord(true, { ...click, button: 1 }), false)
})
