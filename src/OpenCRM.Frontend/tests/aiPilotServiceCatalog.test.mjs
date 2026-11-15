import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'
import vm from 'node:vm'

const source = await readFile(new URL('../../OpenCRM.WebApp/wwwroot/js/ai-pilot-service-catalog.js', import.meta.url), 'utf8')
const context = { organizationId: 'org' }
function resolver() {
  const sandbox = {}
  vm.runInNewContext(source, sandbox)
  return sandbox.OpenCRMAiPilotServiceCatalog
}
const service = (id, name, isActive = true) => ({ id, name, isActive, unitPrice: 900 })

test('unique normalized match uses catalog identity and preserves the spoken price', async () => {
  const catalog = { services: [service('oil', 'Замена   масла')] }
  const resolution = await resolver().prepareRows([{ name: 'замена масла', price: 1500 }], context, { request: async () => catalog })
  assert.equal(resolution.rows[0].catalogItemId, 'oil')
  assert.equal(resolution.rows[0].name, 'Замена   масла')
  assert.equal(resolution.rows[0].price, 1500)
  await resolution.verify()
})

test('unmatched service remains a custom row and archived services are ignored', async () => {
  const resolution = await resolver().prepareRows([{ name: 'Особая диагностика', price: 0 }], context, {
    request: async () => ({ services: [service('archived', 'Особая диагностика', false)] })
  })
  assert.equal(resolution.rows[0].catalogItemId, null)
  assert.equal(resolution.rows[0].price, 0)
})

test('duplicate exact names need an explicit choice and do not write anything', async () => {
  const catalog = { services: [service('one', 'Мойка'), service('two', 'Мойка')] }
  let choice
  await assert.rejects(resolver().prepareRows([{ name: 'мойка', price: 500 }], context, { request: async () => catalog }), error => {
    choice = error.serviceChoice
    return choice.options.length === 2
  })
  const resolution = await resolver().prepareRows([{ name: 'мойка', price: 500 }], {
    ...context, serviceSelections: { [choice.key]: 'two' }
  }, { request: async () => catalog })
  assert.equal(resolution.rows[0].catalogItemId, 'two')
})

test('similar names are offered and never picked automatically', async () => {
  const catalog = { services: [service('one', 'Замена моторного масла'), service('two', 'Замена масла в коробке')] }
  let choice
  await assert.rejects(resolver().prepareRows([{ name: 'Замена масла', price: 1200 }], context, { request: async () => catalog }), error => {
    choice = error.serviceChoice
    return choice.options.length === 2
  })
  const resolution = await resolver().prepareRows([{ name: 'Замена масла', price: 1200 }], {
    ...context, serviceSelections: { [choice.key]: 'custom' }
  }, { request: async () => catalog })
  assert.equal(resolution.rows[0].catalogItemId, null)
})

test('a changed catalog blocks confirmation before any writes', async () => {
  let catalog = { services: [service('one', 'Мойка')] }
  const resolution = await resolver().prepareRows([{ name: 'Мойка', price: 500 }], context, { request: async () => catalog })
  catalog = { services: [service('one', 'Мойка', false)] }
  await assert.rejects(resolution.verify(), /изменился/)
})

test('catalog lookup failures cannot silently become freeform work', async () => {
  await assert.rejects(resolver().prepareRows([{ name: 'Мойка', price: 500 }], context, { request: async () => { throw new Error('403') } }), /403/)
  await assert.rejects(resolver().prepareRows([{ name: 'Мойка', price: 500 }], context, { request: async () => ({}) }), /справочник/)
})

test('existing historical rows keep their identity and snapshot without re-matching', async () => {
  const old = { detailId: 'row', catalogItemId: 'old', name: 'Историческое название', price: 450 }
  const resolution = await resolver().prepareRows([old], context, { request: async () => { throw new Error('Unexpected catalog lookup') } })
  assert.equal(resolution.rows[0], old)
  await resolution.verify()
})

test('an unfinished service without a price is labelled accurately', async () => {
  const resolution = await resolver().prepareRows([{ name: 'Мойка', price: null }], context, {
    request: async () => ({ services: [service('one', 'Мойка')] })
  })
  assert.match(resolution.notes[0], /Цена пока не указана/)
  assert.equal(resolution.rows[0].price, null)
})

test('too many similar services require a more precise name instead of a truncated choice', async () => {
  const services = Array.from({ length: 21 }, (_, index) => service('id-' + index, 'Мойка вариант ' + index))
  await assert.rejects(resolver().prepareRows([{ name: 'Мойка', price: 500 }], context, {
    request: async () => ({ services })
  }), error => /точнее/.test(error.message) && !error.serviceChoice)
})
