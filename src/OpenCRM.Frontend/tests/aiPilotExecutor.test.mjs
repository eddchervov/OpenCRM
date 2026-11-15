import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'
import vm from 'node:vm'

const catalogSource = await readFile(new URL('../../OpenCRM.WebApp/wwwroot/js/ai-pilot-service-catalog.js', import.meta.url), 'utf8')
const source = await readFile(new URL('../../OpenCRM.WebApp/wwwroot/js/ai-pilot-executor.js', import.meta.url), 'utf8')
const context = { userId: 'test-user', organizationId: 'test-organization', workspaceUrl: '/autoservice/test-organization' }

function createExecutor() {
  const storage = new Map()
  const sandbox = { Intl, Date, Set, Map, Error, Object, Array, String, Number, JSON, crypto: { randomUUID: () => 'payment-id' }, sessionStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value) } }
  vm.runInNewContext(catalogSource, sandbox)
  vm.runInNewContext(source, sandbox)

  return sandbox.OpenCRMAiPilotExecutor
}

function command(overrides = {}) {
  return { action: 'visit', registrationPlate: 'А123ВС154', date: '2026-10-26', services: [{ name: 'Замена масла', price: 1500 }], totalAmount: 1500, ...overrides }
}

test('normalizes Cyrillic, Latin and spaces to the same plate', () => {
  const executor = createExecutor()
  assert.equal(executor.normalizePlate('А 123 ВС 154'), 'A123BC154')
  assert.equal(executor.normalizePlate('a123bc154'), 'A123BC154')
})

test('rejects actions outside the allowlist before making API calls', async () => {
  let calls = 0
  await assert.rejects(createExecutor().execute(command({ action: 'grant_tariff' }), context, { draftToken: 'one', request: () => { calls += 1 } }), /не разрешено/)
  assert.equal(calls, 0)
})

test('creates a completed visit once and returns the same result on repeat', async () => {
  const executor = createExecutor()
  const mutations = []
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (!options) return { items: [], totalCount: 0, totalPages: 0 }
    mutations.push({ path, options })

    return path === 'api/v1/vehicles' ? 'vehicle-id' : 'maintenance-id'
  }
  const options = { draftToken: 'same-draft', request }
  const first = await executor.execute(command(), context, options)
  const repeated = await executor.execute(command(), context, options)
  assert.equal(first.maintenanceId, 'maintenance-id')
  assert.equal(repeated.maintenanceId, 'maintenance-id')
  assert.equal(mutations.length, 2)
  assert.equal(mutations[1].options.body.status, 3)
})

test('does not automatically repeat a write after an uncertain network failure', async () => {
  const executor = createExecutor()
  let mutations = 0
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (!options) return { items: [], totalCount: 0 }
    mutations += 1
    throw new Error('network failure')
  }
  const options = { draftToken: 'uncertain', request }
  await assert.rejects(executor.execute(command(), context, options), /network failure/)
  await assert.rejects(executor.execute(command(), context, options), /не подтверждён/)
  assert.equal(mutations, 1)
})

test('rejects duplicate normalized plates without changing data', async () => {
  let mutations = 0
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (options) { mutations += 1; return }

    return { items: [{ id: 'one', registrationPlate: 'А123ВС154' }, { id: 'two', registrationPlate: 'A 123 BC 154' }], totalCount: 2, totalPages: 1 }
  }
  await assert.rejects(createExecutor().execute(command(), context, { draftToken: 'duplicates', request }), /несколько автомобилей/)
  assert.equal(mutations, 0)
})

test('preserves the created visit when payment fails', async () => {
  const executor = createExecutor()
  let creations = 0
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (!options && path.includes('api/v1/maintenance/')) return { canManagePayments: true, paidAmount: 0 }
    if (!options) return { items: [], totalCount: 0 }
    if (path.endsWith('/payments?organizationId=test-organization')) throw Object.assign(new Error('payment rejected'), { status: 400 })
    if (path === 'api/v1/vehicles') return 'vehicle-id'
    creations += 1

    return 'maintenance-id'
  }
  await assert.rejects(executor.execute(command({ paidAmount: 1500 }), context, { draftToken: 'partial', request }), error => error.maintenanceUrl.endsWith('#maintenance/maintenance-id'))
  assert.equal(creations, 1)
})

test('rejects contradictory prices before creating a vehicle or a visit', async () => {
  let calls = 0
  const request = async () => { calls += 1 }
  await assert.rejects(createExecutor().execute(command({ services: [{ name: 'Масло', price: 1000 }, { name: 'Фильтр', price: 500 }], totalAmount: 2000 }), context, { draftToken: 'bad-total', request }), /отличается/)
  assert.equal(calls, 0)
})

test('a separate completed visit does not overwrite an existing planned order', async () => {
  const mutations = []
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (!options && path.startsWith('api/v1/vehicles?')) return { items: [{ id: 'vehicle-id', registrationPlate: 'А123ВС154' }], totalCount: 1, totalPages: 1 }
    if (!options) throw new Error('A new visit must not select an existing order')
    mutations.push({ path, options })

    return 'new-visit-id'
  }
  const result = await createExecutor().execute(command(), context, { draftToken: 'new-visit', request })
  assert.equal(result.maintenanceId, 'new-visit-id')
  assert.equal(mutations.length, 1)
  assert.equal(mutations[0].options.method, 'POST')
})

test('a new visit saves the matched catalog identity and keeps the spoken price', async () => {
  const mutations = []
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [{ id: 'oil-catalog', name: 'Замена масла', unitPrice: 999, isActive: true }] }
    if (!options) return { items: [], totalCount: 0 }
    mutations.push({ path, options })
    return path === 'api/v1/vehicles' ? 'vehicle-id' : 'maintenance-id'
  }
  await createExecutor().execute(command({ services: [{ name: 'замена масла', price: 1500 }] }), context, { draftToken: 'catalog', request })
  assert.equal(mutations[1].options.body.details[0].catalogItemId, 'oil-catalog')
  assert.equal(mutations[1].options.body.details[0].name, 'Замена масла')
  assert.equal(mutations[1].options.body.details[0].price, 1500)
})

for (const action of ['book', 'arrive', 'visit']) {
  test(`new ${action} cannot create an empty record`, async () => {
    let calls = 0
    await assert.rejects(createExecutor().execute(command({ action, services: [] }), context, {
      draftToken: 'empty-' + action, request: async () => { calls += 1 }
    }), /работ/)
    assert.equal(calls, 0)
  })
}

test('separate work names cannot be silently collapsed into a custom bundle', async () => {
  let calls = 0
  await assert.rejects(createExecutor().execute(command({ services: [{ name: 'Мойка' }, { name: 'Полировка' }] }), context, {
    draftToken: 'no-collapse', request: async () => { calls += 1 }
  }), /каждой работы/)
  assert.equal(calls, 0)
})

test('arrival saves a newly named work with an unspecified price before starting the order', async () => {
  const mutations = []
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (options) { mutations.push({ path, options }); return null }
    if (path.startsWith('api/v1/vehicles?')) return { items: [{ id: 'vehicle-id', registrationPlate: 'А123ВС154' }], totalCount: 1, totalPages: 1 }
    if (path.includes('/maintenance/1/100')) return { items: path.endsWith('status=1') ? [{ id: 'planned-id' }] : [], hasMore: false }
    return { id: 'planned-id', version: 2, status: 1, maintenanceDetails: [] }
  }
  await createExecutor().execute(command({ action: 'arrive', services: [{ name: 'Замена масла', price: null }], totalAmount: null }), context, { draftToken: 'arrival', request })
  assert.equal(mutations.length, 2)
  assert.equal(mutations[0].options.method, 'PUT')
  assert.equal(mutations[0].options.body.allowUnspecifiedPrices, true)
  assert.equal(mutations[0].options.body.details[0].price, null)
  assert.equal(mutations[1].options.body.action, 'start')
})

test('rejects a completed visit without a price before changing data', async () => {
  let calls = 0
  await assert.rejects(createExecutor().execute(command({ services: [{ name: 'Масло', price: null }], totalAmount: null }), context, { draftToken: 'missing-price', request: async () => { calls += 1 } }), /стоимость/)
  assert.equal(calls, 0)
})

for (const totalAmount of [null, 0, 2000]) {
  test(`completion requires a price and applies a spoken total: ${totalAmount}`, async () => {
    const mutations = []
    const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
      if (options) { mutations.push({ path, options }); return null }
      if (path.startsWith('api/v1/vehicles?')) return { items: [{ id: 'vehicle-id', registrationPlate: 'А123ВС154' }], totalCount: 1, totalPages: 1 }
      if (path.includes('/maintenance/1/100')) return { items: path.endsWith('status=2') ? [{ id: 'active-id' }] : [], hasMore: false }
      return { id: 'active-id', version: 2, status: 2, maintenanceDetails: [{ name: 'Масло', price: null }] }
    }
    const execution = createExecutor().execute(command({ action: 'complete', services: [], totalAmount }), context, { draftToken: 'completion', request })
    if (totalAmount === null) {
      await assert.rejects(execution, /стоимость/)
      assert.equal(mutations.length, 0)
      return
    }
    await execution
    assert.equal(mutations.length, 2)
    assert.equal(mutations[0].options.body.details[0].price, totalAmount)
    assert.equal(mutations[1].options.body.action, 'complete')
  })
}

for (const material of [{ materialCatalogItemId: 'material-id' }, { materialName: 'Масло' }, { materialQuantity: 1 }]) {
  test(`does not convert existing materials into services during completion: ${Object.keys(material)[0]}`, async () => {
    let mutations = 0
    const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
      if (options) { mutations += 1; return null }
      if (path.startsWith('api/v1/vehicles?')) return { items: [{ id: 'vehicle-id', registrationPlate: 'А123ВС154' }], totalCount: 1, totalPages: 1 }
      if (path.includes('/maintenance/1/100')) return { items: path.endsWith('status=2') ? [{ id: 'active-id' }] : [], hasMore: false }
      return { version: 2, status: 2, maintenanceDetails: [{ name: 'Масло', ...material, price: 500 }] }
    }
    await assert.rejects(createExecutor().execute(command({ action: 'complete', services: [], totalAmount: 2000 }), context, { draftToken: 'materials', request }), /есть материалы/)
    assert.equal(mutations, 0)
  })
}
