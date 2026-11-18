import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import '../packages/workspace/src/materialMessages.ts'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { Mask } from 'maska'
import { formattingCulture, i18n, translate } from '@opencrm/ui/i18n'
import { inquiryUtcTime as organizationUtcTime } from '../packages/workspace/src/utils/inquiryTime.ts'

const maskModule = { exports: {} }
new Function('module', 'exports', ts.transpileModule(readFileSync(new URL('../packages/workspace/src/materialNumberMask.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS }
}).outputText)(maskModule, maskModule.exports)
const { createMaterialNumberMask } = maskModule.exports

function component(scenario, loadMaterials, actions = {}, timeZoneId = 'Asia/Novosibirsk') {
  const filename = new URL(`../Scenarios/${scenario}-client/src/components/MaterialsSection.vue`, import.meta.url)
  const { descriptor } = parse(readFileSync(filename, 'utf8'), { filename: filename.pathname })
  const script = compileScript(descriptor, { id: `materials-${scenario}-test` })
  const compiled = ts.transpileModule(script.content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  const timers = new Map()
  const watchers = []
  let timerId = 0
  const resolve = id => {
    if (id in regionalTestDependencies) return regionalTestDependencies[id]
    if (id === '@opencrm/workspace/materialMessages') return {}
    if (id === 'vue') return {
      ...vue,
      onMounted() {},
      onBeforeUnmount() {},
      watch(sources, callback) {
        const isMultiple = Array.isArray(sources)
        if (!isMultiple) sources = [sources]
        let previous = sources.map(source => typeof source === 'function' ? source() : source.value)
        watchers.push(() => {
          const values = sources.map(source => typeof source === 'function' ? source() : source.value)
          if (values.every((value, index) => value === previous[index])) return
          const oldValues = previous
          previous = values
          callback(isMultiple ? values : values[0], isMultiple ? oldValues : oldValues[0])
        })
      }
    }
    if (id === '@/api/materials') return { loadMaterials, ...actions }
    if (id.startsWith('@/api/')) return { loadScenarioCatalog: async () => ({ materials: [] }) }
    if (id === '@opencrm/ui') return { ...regionalTestDependencies['@opencrm/ui/i18n'], formatDateOnly: () => '2026-10-21' }
    if (id === '@opencrm/workspace') return { ...regionalTestDependencies['../workspaceCurrency'], organizationUtcTime, createMaterialNumberMask, materialUnitOptions: [{ value: 'шт', label: 'шт' }, { value: 'л', label: 'л' }], isDiscreteMaterialUnit: unit => ['шт', 'уп', 'компл'].includes(unit), normalizeMaterialUnit: value => value }
    return {}
  }
  new Function('require', 'module', 'exports', 'setTimeout', 'clearTimeout', compiled)(
    resolve, module, module.exports,
    callback => { timers.set(++timerId, callback); return timerId },
    id => timers.delete(id)
  )
  const state = module.exports.default.setup({
    context: { organization: { id: 'org-1', timeZoneId }, workspaceAccess: { isReadOnly: false } }
  }, { expose() {} })
  return {
    state,
    flushWatchers() { watchers.forEach(watcher => watcher()) },
    async flushTimers() {
      const pending = [...timers.values()]
      timers.clear()
      pending.forEach(callback => callback())
      await Promise.resolve()
      await Promise.resolve()
    },
    get pendingTimers() { return timers.size }
  }
}

function materialsApi(scenario, fetchImplementation) {
  const filename = new URL(`../Scenarios/${scenario}-client/src/api/materials.ts`, import.meta.url)
  const source = readFileSync(filename, 'utf8')
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  const resolve = id => regionalTestDependencies[id] ?? {}
  new Function('require', 'module', 'exports', 'fetch', 'document', compiled)(
    resolve,
    module,
    module.exports,
    fetchImplementation,
    { querySelector: () => null }
  )

  return module.exports
}

const inventory = (id, total = 37) => ({ stocks: [{ id }], stocksTotalCount: total, allStocksCount: 37 })

test('Даты фильтров и остатков склада переводятся в UTC по часовому поясу организации', () => {
  for (const scenario of ['detailing', 'autoservice']) {
    const { state } = component(scenario, async () => inventory('stock'))
    assert.equal(state.toLocalIso('2026-10-25', '09:00'), '2026-10-25T02:00:00.000Z')
  }
  const { state } = component('detailing', async () => inventory('stock'), {}, 'Europe/Berlin')
  assert.equal(state.toLocalIso('2026-03-29', '02:30'), null)
})
test('маска сохраняет введённое число для явной проверки доступного остатка и точности', () => {
  const options = createMaterialNumberMask(3)
  const mask = new Mask(options)
  const input = value => mask.masked(options.preProcess(value))
  assert.equal(input('1,23456'), '1.23456')
  assert.equal(input('1.234e+84'), '1.23456')
  assert.equal(input('-5'), '1.23456')
  assert.equal(input('9999999999999999999999999999'), '9999999999999999999999999999')
  assert.equal(input('29.5'), '29.5')
  assert.equal(input('30'), '30')
  const whole = createMaterialNumberMask(0)
  assert.equal(whole.preProcess('1.5'), '1.5')
  const cost = createMaterialNumberMask(8)
  assert.equal(new Mask(cost).masked(cost.preProcess('999999998.123456789')), '999999998.123456789')
})
function deferred() {
  let resolve
  const promise = new Promise(complete => { resolve = complete })
  return { promise, resolve }
}

for (const scenario of ['detailing', 'autoservice', 'fleet']) {
  test(`${scenario}: склад показывает копейки и точную цену базовой единицы`, () => {
    const { state } = component(scenario, async () => inventory('stock'))
    Object.assign(state.receiptForm, { quantity: '0.5', unitCost: '19.90' })
    const normalize = value => value.replace(/\s/g, ' ')
    assert.equal(state.receiptTotalCost.value, 9.95)
    assert.equal(normalize(state.formatMoney(state.receiptTotalCost.value)), '9,95 ₽')
    assert.equal(normalize(state.formatMoney(7.96)), '7,96 ₽')
    assert.equal(normalize(state.formatMoney(0.0199, 8)), '0,0199 ₽')
    assert.equal(normalize(state.formatMoney(0.00000125, 8)), '0,00000125 ₽')
    assert.equal(normalize(state.formatMoney(19.9012, 4)), '19,9012 ₽')
  })

  test(`${scenario}: после прихода сохраняются условия списка`, async () => {
    const requests = []
    const view = component(scenario, async (_, options) => {
      requests.push(options)
      return inventory('stock')
    }, { createMaterialReceipt: async () => ({}) })
    view.state.stockFilters.search = 'паста'
    view.state.stockFilters.filter = 'low'
    view.state.stockFilters.page = 2
    view.state.activeView.value = 'receipt'
    Object.assign(view.state.receiptForm, { name: 'Материал', quantity: '5', unitCost: '100' })
    await view.state.addReceipt()
    assert.equal(view.state.activeView.value, 'stocks')
    assert.equal(requests.at(-1).searchQuery, 'паста')
    assert.equal(requests.at(-1).stockFilter, 'low')
    assert.equal(requests.at(-1).stocksPage, 2)
    assert.equal(requests.at(-1).stocksPageSize, 10)
  })

  test(`${scenario}: поиск и страницы общего списания независимы от списка остатков`, async () => {
    const requests = []
    const view = component(scenario, async (_, options) => {
      requests.push(options)
      return inventory('stock')
    })
    view.state.stockFilters.search = 'паста'
    view.state.stockFilters.filter = 'out'
    view.state.stockFilters.page = 3
    view.state.openIssue()
    await Promise.resolve()
    await Promise.resolve()
    assert.equal(requests.at(-1).searchQuery, '')
    assert.equal(requests.at(-1).stockFilter, null)
    assert.equal(requests.at(-1).stocksPage, 1)
    view.state.issueFilters.search = 'состав'
    view.flushWatchers()
    await view.flushTimers()
    await view.state.changeIssuePage(2)
    assert.equal(requests.at(-1).searchQuery, 'состав')
    assert.equal(requests.at(-1).stocksPage, 2)
    assert.equal(requests.at(-1).stocksPageSize, 20)
    assert.equal(view.state.stockFilters.search, 'паста')
    assert.equal(view.state.stockFilters.page, 3)
  })

  test(`${scenario}: поиск объединяет ввод и отбрасывает устаревший ответ`, async () => {
    const oldResponse = deferred()
    const requests = []
    const view = component(scenario, async (_, options) => {
      requests.push(options)
      return options.searchQuery ? inventory(options.searchQuery, 1) : oldResponse.promise
    })
    const oldRequest = view.state.loadSection()
    view.state.stockFilters.page = 3
    view.state.stockFilters.search = 'па'
    view.flushWatchers()
    view.state.stockFilters.search = 'паста'
    view.flushWatchers()
    assert.equal(view.pendingTimers, 1)
    assert.equal(view.state.stockFilters.page, 1)
    oldResponse.resolve(inventory('старый'))
    await oldRequest
    assert.equal(view.state.inventory.value, null)
    await view.flushTimers()
    assert.equal(requests.length, 2)
    assert.equal(view.state.inventory.value.stocks[0].id, 'паста')
    assert.equal(view.state.isLoading.value, false)
  })

  test(`${scenario}: фильтр и размер страницы сбрасывают страницу, переход сохраняет условия`, async () => {
    const requests = []
    const view = component(scenario, async (_, options) => {
      requests.push(options)
      return inventory('stock')
    })
    await view.state.loadSection()
    await view.state.changeStocksPage(3)
    view.state.stockFilters.filter = 'low'
    view.flushWatchers()
    await Promise.resolve()
    await Promise.resolve()
    assert.equal(view.state.stockFilters.page, 1)
    assert.equal(requests.at(-1).stockFilter, 'low')
    view.state.selectedPageSize.value = '25'
    view.flushWatchers()
    await Promise.resolve()
    await Promise.resolve()
    await view.state.changeStocksPage(2)
    assert.equal(requests.at(-1).stocksPage, 2)
    assert.equal(requests.at(-1).stocksPageSize, 25)
    assert.equal(requests.at(-1).stockFilter, 'low')
    assert.equal(view.state.stocksFirstItem.value, 26)
    assert.equal(view.state.stocksLastItem.value, 37)
  })

  test(`${scenario}: уход со склада отменяет отложенный поиск, возврат восстанавливает фильтр`, async () => {
    const requests = []
    const view = component(scenario, async (_, options) => {
      requests.push(options)
      return inventory('stock', 1)
    })
    await view.state.loadSection()
    view.state.stockFilters.search = 'паста'
    view.flushWatchers()
    view.state.openView('receipt')
    assert.equal(view.pendingTimers, 0)
    await view.flushTimers()
    assert.equal(requests.length, 1)
    view.state.openView('stocks')
    await Promise.resolve()
    await Promise.resolve()
    assert.equal(requests.at(-1).searchQuery, 'паста')
  })
}

test('API fallback ошибки склада переводятся, а сообщение сервера сохраняется', async () => {
  const previousLocale = i18n.global.locale.value
  try {
    for (const [locale, expectedLoad, expectedReceipt] of [
      ['en', 'Could not load stock.', 'Could not add the receipt.'],
      ['zh-Hans', '无法加载库存。', '无法添加入库记录。']
    ]) {
      i18n.global.locale.value = locale
      for (const scenario of ['autoservice', 'detailing', 'fleet']) {
        const api = materialsApi(scenario, async () => ({ ok: false, json: async () => null }))
        await assert.rejects(api.loadMaterials('org-1'), error => error.message === expectedLoad)
        await assert.rejects(api.createMaterialReceipt('org-1', {}), error => error.message === expectedReceipt)
      }
    }

    const api = materialsApi('autoservice', async () => ({ ok: false, json: async () => ({ message: 'A server supplied message.' }) }))
    await assert.rejects(api.loadMaterials('org-1'), error => error.message === 'A server supplied message.')
  } finally {
    i18n.global.locale.value = previousLocale
  }
})

for (const scenario of ['detailing', 'autoservice', 'fleet']) {
  test(`${scenario}: приход не отправляет неверные значения и принимает границы`, async () => {
    const saved = []
    const view = component(scenario, async () => inventory('stock'), { createMaterialReceipt: async (_, payload) => { saved.push(payload); return {} } })
    Object.assign(view.state.receiptForm, { name: 'Материал', quantity: '1', unitCost: '0' })
    const invalid = [
      { name: '' }, { name: 'Я'.repeat(161) }, { quantity: '0' }, { quantity: '-1' },
      { quantity: '1000000000' }, { quantity: '1.5' }, { unit: '' },
      { unitCost: '-1' }, { unitCost: '0.00001' }, { unitCost: '1000000000' },
      { packageQuantity: '0' }, { packageQuantity: '1.5' }, { comment: 'Я'.repeat(1001) }
    ]
    for (const changes of invalid) {
      const original = { ...view.state.receiptForm }
      Object.assign(view.state.receiptForm, changes)
      await view.state.addReceipt()
      assert.equal(saved.length, 0, JSON.stringify(changes))
      assert.ok(Object.values(view.state.receiptErrors.value).some(Boolean))
      assert.equal(view.state.errorDialogMessage.value, '')
      Object.assign(view.state.receiptForm, original)
    }
    Object.assign(view.state.receiptForm, { name: 'Я'.repeat(160), unit: 'л', quantity: 1.001, unitCost: 1.2345, packageQuantity: 0.001, comment: 'Я'.repeat(1000) })
    await view.state.addReceipt()
    assert.equal(saved.length, 1)
    assert.equal(saved[0].unitCost, 1.2345)
    assert.equal(saved[0].quantity, 1.001)
  })

  test(`${scenario}: списание учитывает резерв, точность и длину причины`, async () => {
    const stock = { id: 's', unit: 'л', quantityOnHand: 10, reservedQuantity: 4, availableQuantity: 6 }
    const saved = []
    const view = component(scenario, async () => ({ stocks: [stock], stocksTotalCount: 1 }), { createMaterialIssue: async (_, payload) => { saved.push(payload); return stock } })
    await view.state.loadSection()
    view.state.openIssue(stock)
    for (const changes of [{ quantity: '7' }, { quantity: '0' }, { quantity: '0.0001' }, { quantity: '1', comment: 'Я'.repeat(1001) }]) {
      Object.assign(view.state.issueForm, { quantity: '', comment: '', ...changes })
      await view.state.issueMaterial()
      assert.equal(saved.length, 0)
    }
    Object.assign(view.state.issueForm, { quantity: 6, comment: '' })
    await view.state.issueMaterial()
    assert.equal(saved.length, 1)
    assert.equal(saved[0].quantity, 6)
  })

  test(`${scenario}: корректировка сохраняет резерв и историческую точность цены`, async () => {
    const stock = { id: 's', unit: 'шт', quantityOnHand: 10, reservedQuantity: 4, availableQuantity: 6 }
    const saved = []
    const view = component(scenario, async () => inventory('s'), { adjustMaterialStock: async (_, id, payload) => { saved.push(payload); return stock } })
    view.state.selectedStock.value = stock
    view.state.openAdjustment()
    const valid = { quantity: 4, unitCost: '999999998.12345678', reason: 'Инвентаризация' }
    for (const changes of [{ quantity: '3' }, { quantity: '4.5' }, { quantity: '1000000000' }, { unitCost: '-1' }, { reason: '' }, { reason: 'Я'.repeat(1001) }]) {
      Object.assign(view.state.adjustmentForm, valid, changes)
      await view.state.saveAdjustment()
      assert.equal(saved.length, 0)
    }
    Object.assign(view.state.adjustmentForm, valid)
    await view.state.saveAdjustment()
    assert.equal(saved.length, 1)
    assert.equal(saved[0].averageUnitCost, '999999998.12345678')
    stock.averageUnitCost = 12.12345678
    view.state.openAdjustment()
    Object.assign(view.state.adjustmentForm, { quantity: 5, reason: 'Пересчёт без изменения цены' })
    await view.state.saveAdjustment()
    assert.equal(saved[1].averageUnitCost, null)
  })

  test(`${scenario}: подписи остатков и единицы следуют языку, а коды единиц остаются исходными`, async () => {
    const previousLocale = i18n.global.locale.value
    const previousCulture = formattingCulture.value
    try {
      i18n.global.locale.value = 'en'
      formattingCulture.value = 'en-GB'
      const saved = []
      const view = component(scenario, async () => inventory('stock'), {
        createMaterialReceipt: async (_, payload) => { saved.push(payload); return {} }
      })
      assert.equal(view.state.currentTitle.value, 'Stock')
      assert.equal(view.state.movementTypeOptions.value[0].label, 'All types')
      assert.equal(view.state.stockFilterOptions.value[1].label, 'Out of stock')
      assert.equal(view.state.localizedMaterialUnitOptions.value[0].label, 'pcs')
      assert.equal(view.state.formatQuantity(1234.5), '1,234.5')
      assert.equal(view.state.formatDate('2026-10-21'), '21/10/2026')
      assert.match(view.state.formatDateTime('2026-10-25T01:30:00Z'), /08:30/)
      Object.assign(view.state.receiptForm, { name: 'Sample material', unit: 'шт', quantity: '2', unitCost: '1' })
      await view.state.addReceipt()
      assert.equal(saved[0].unit, 'шт')
    } finally {
      i18n.global.locale.value = previousLocale
      formattingCulture.value = previousCulture
    }
  })
}
