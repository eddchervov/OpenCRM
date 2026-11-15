import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'
import vm from 'node:vm'

const catalogSource = await readFile(new URL('../../OpenCRM.WebApp/wwwroot/js/ai-pilot-service-catalog.js', import.meta.url), 'utf8')
const source = await readFile(new URL('../../OpenCRM.WebApp/wwwroot/js/ai-pilot-actions.js', import.meta.url), 'utf8')
const maintenanceId = '12345678-1234-1234-1234-123456789000'
const context = { userId: 'user', organizationId: 'org', workspaceUrl: '/autoservice/org' }
const copy = value => JSON.parse(JSON.stringify(value))

function executor() {
  const storage = new Map()
  const sandbox = { Intl, Date, Set, Map, Error, Object, Array, String, Number, Boolean, JSON, Promise,
    OpenCRMAiPilotExecutor: { normalizePlate: value => value }, crypto: { randomUUID: () => 'fixed-operation-id' }, sessionStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value) } }
  vm.runInNewContext(catalogSource, sandbox)
  vm.runInNewContext(source, sandbox)
  return sandbox.OpenCRMAiPilotActions
}

function fixture(overrides = {}) {
  let current = { id: maintenanceId, vehicleId: 'car', version: 42, status: 2, type: 1, canEditServices: true,
    canManagePayments: true, plannedDate: '2026-10-26', paidAmount: 0, discountAmount: 0, laborCostAmount: 200,
    maintenanceDetails: [
      { detailId: 'oil', name: 'Замена масла', price: 1000, quantity: 2, vatRateCode: 'none', employeeId: 'mechanic', catalogItemId: 'catalog-oil' },
      { detailId: 'filter', name: 'Замена фильтра', price: 500, quantity: 1 },
      { detailId: 'material', name: 'Масло', materialName: 'Масло', materialQuantity: 4, materialUnitCost: 300 }
    ], ...overrides }
  const mutations = []
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (!options) return copy(current)
    mutations.push({ path, ...copy(options) })
    return {}
  }
  return { get current() { return current }, set current(value) { current = value }, mutations, request }
}

function command(action, parameters = {}, extras = {}) {
  return { action, registrationPlate: null, services: [], parameters: { maintenanceId, ...parameters }, ...extras }
}

test('adding a service only prepares a plan and preserves the old row identities and fields', async () => {
  const f = fixture()
  const plan = await executor().prepare(command('service_add', { serviceName: 'Мойка', price: 0 }), context, { request: f.request })
  assert.equal(f.mutations.length, 0)
  await plan.execute({ draftToken: 'add' })
  assert.equal(f.mutations.length, 1)
  const body = f.mutations[0].body
  assert.equal(body.version, 42)
  assert.equal(body.details.length, 3)
  assert.deepEqual(body.details[0], f.current.maintenanceDetails[0])
  assert.equal(body.details[2].price, 0)
  assert.equal(body.details.some(item => item.materialName), false)
})

test('adding a named catalog service links its identity without replacing the price', async () => {
  const f = fixture()
  const request = (path, options) => path.endsWith('/scenario-catalog')
    ? Promise.resolve({ services: [{ id: 'wash-catalog', name: 'Мойка', unitPrice: 800, isActive: true }] })
    : f.request(path, options)
  const plan = await executor().prepare(command('service_add', { serviceName: 'мойка', price: 500 }), context, { request })
  assert.equal(f.mutations.length, 0)
  await plan.execute({ draftToken: 'catalog-add' })
  const added = f.mutations[0].body.details.at(-1)
  assert.equal(added.catalogItemId, 'wash-catalog')
  assert.equal(added.name, 'Мойка')
  assert.equal(added.price, 500)
})

test('removing one service requires explicit confirmation and keeps every other service', async () => {
  const f = fixture()
  const plan = await executor().prepare(command('service_remove', { serviceName: 'Замена масла' }), context, { request: f.request })
  await assert.rejects(plan.execute({ draftToken: 'remove' }), /Подтвердите/)
  assert.equal(f.mutations.length, 0)
  await plan.execute({ draftToken: 'remove', confirmed: true })
  assert.equal(f.mutations[0].body.details.length, 1)
  assert.equal(f.mutations[0].body.details[0].detailId, 'filter')
})

test('a changed preflight snapshot prevents any mutation', async () => {
  const f = fixture()
  const plan = await executor().prepare(command('service_add', { serviceName: 'Мойка', price: 500 }), context, { request: f.request })
  f.current.version = 43
  await assert.rejects(plan.execute({ draftToken: 'stale' }), /Данные изменились/)
  assert.equal(f.mutations.length, 0)
})

test('duplicate service names require a row number instead of guessing', async () => {
  const f = fixture({ maintenanceDetails: [{ detailId: 'one', name: 'Мойка', price: 100 }, { detailId: 'two', name: 'Мойка', price: 200 }] })
  await assert.rejects(executor().prepare(command('service_remove', { serviceName: 'Мойка' }), context, { request: f.request }), /несколько/)
  const plan = await executor().prepare(command('service_remove', { lineNumber: 2 }), context, { request: f.request })
  await plan.execute({ draftToken: 'row', confirmed: true })
  assert.equal(f.mutations[0].body.details[0].detailId, 'one')
})

test('removing the last unfinished work is rejected before saving', async () => {
  const f = fixture({ maintenanceDetails: [{ name: 'Мойка', price: null }] })
  await assert.rejects(executor().prepare(command('service_remove', { lineNumber: 1 }), context, { request: f.request }), /пустой список/)
  assert.equal(f.mutations.length, 0)
})

test('a completed order cannot lose its last work', async () => {
  const f = fixture({ status: 3, maintenanceDetails: [{ name: 'Мойка', price: 500 }] })
  await assert.rejects(executor().prepare(command('service_remove', { lineNumber: 1 }), context, { request: f.request }), /пустой список/)
  assert.equal(f.mutations.length, 0)
})

test('replaying a confirmed plan does not repeat the write', async () => {
  const f = fixture()
  const plan = await executor().prepare(command('service_update', { lineNumber: 1, price: 1200 }), context, { request: f.request })
  await plan.execute({ draftToken: 'repeat' })
  await plan.execute({ draftToken: 'repeat' })
  assert.equal(f.mutations.length, 1)
})

test('an uncertain response blocks replay and keeps the real error message', async () => {
  const f = fixture()
  let calls = 0
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (!options) return f.request(path)
    calls += 1
    throw new Error('network lost')
  }
  const plan = await executor().prepare(command('payment_add', {}, { paidAmount: 100 }), context, { request })
  await assert.rejects(plan.execute({ draftToken: 'unknown' }), /network lost/)
  await assert.rejects(plan.execute({ draftToken: 'unknown' }), /потерян/)
  assert.equal(calls, 1)
})

test('editing an order preserves buyer, work rows, materials and financial fields', async () => {
  const f = fixture({ comment: 'Старое', buyerClientId: 'buyer', linkedDefects: [{ id: 'defect' }] })
  const plan = await executor().prepare(command('maintenance_update', { comment: 'Новое' }), context, { request: f.request })
  await plan.execute({ draftToken: 'comment' })
  const body = f.mutations[0].body
  assert.equal(body.comment, 'Новое')
  assert.equal(body.buyerClientId, 'buyer')
  assert.equal(body.laborCostAmount, 200)
  assert.equal(body.details.length, 3)
  assert.deepEqual(body.linkedDefectIds, ['defect'])
})

test('completion keeps unmentioned services and advances versions after service editing', async () => {
  const f = fixture()
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (!options) return copy(f.current)
    f.mutations.push({ path, ...copy(options) })
    f.current.version += 1
    if (path.includes('/services?')) f.current.maintenanceDetails = [...copy(options.body.details), f.current.maintenanceDetails[2]]
    return {}
  }
  const plan = await executor().prepare(command('complete', {}, { services: [{ name: 'Замена фильтра', price: 700 }] }), context, { request })
  await plan.execute({ draftToken: 'complete' })
  await plan.execute({ draftToken: 'complete' })
  assert.equal(f.mutations.length, 2)
  assert.equal(f.mutations[0].body.details.length, 2)
  assert.equal(f.mutations[0].body.details[0].detailId, 'oil')
  assert.equal(f.mutations[1].body.version, 43)
})

test('completion fills a legacy empty order with a matched service before changing status', async () => {
  const f = fixture({ maintenanceDetails: [] })
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [{ id: 'diagnostic', name: 'Диагностика', isActive: true }] }
    if (!options) return copy(f.current)
    f.mutations.push({ path, ...copy(options) })
    f.current.version += 1
    if (path.includes('/services?')) f.current.maintenanceDetails = copy(options.body.details).map(item => ({ ...item, detailId: 'saved-row' }))
    return {}
  }
  const plan = await executor().prepare(command('complete', {}, { services: [{ name: 'диагностика', price: 1200 }] }), context, { request })
  assert.equal(f.mutations.length, 0)
  await plan.execute({ draftToken: 'complete-empty' })
  assert.equal(f.mutations.length, 2)
  assert.equal(f.mutations[0].body.details[0].catalogItemId, 'diagnostic')
  assert.equal(f.mutations[0].body.details[0].price, 1200)
  assert.equal(f.mutations[1].body.action, 'complete')
})

test('archiving a matched service after preparation blocks the whole write plan', async () => {
  const f = fixture()
  const service = { id: 'wash', name: 'Мойка', isActive: true }
  const request = async (path, options) => path.endsWith('/scenario-catalog')
    ? { services: [copy(service)] } : f.request(path, options)
  const plan = await executor().prepare(command('service_add', { serviceName: 'Мойка', price: 500 }), context, { request })
  service.isActive = false
  await assert.rejects(plan.execute({ draftToken: 'archived-catalog' }), /Справочник услуг изменился/)
  assert.equal(f.mutations.length, 0)
})

test('client phone edits locate the client by name and preserve other client fields', async () => {
  const client = { id: 'client', name: 'Иван', phone: '79000000001', inn: '123', responsibleUserId: 'owner', tags: [{ name: 'Постоянный' }] }
  const writes = []
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (options) { writes.push(options); return {} }
    return path.endsWith('/1/100') ? { items: [client], totalCount: 1 } : { client, canEdit: true }
  }
  const plan = await executor().prepare({ action: 'client_update', parameters: { clientName: 'Иван', newPhone: '79000000002' } }, context, { request })
  await plan.execute({ draftToken: 'client' })
  assert.equal(writes[0].body.phone, '79000000002')
  assert.equal(writes[0].body.inn, '123')
  assert.equal(writes[0].body.tagNamesText, 'Постоянный')
  assert.equal(writes[0].body.responsibleUserId, 'owner')
})

test('a plate change between preparation and confirmation blocks the operation', async () => {
  const f = fixture()
  const car = { id: 'car', registrationPlate: 'A123BC154', organizationId: 'org', organizationClientId: 'buyer' }
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (path.startsWith('api/v1/vehicles?')) return { items: [copy(car)], totalCount: 1, totalPages: 1 }
    if (path.startsWith('api/v1/vehicles/car?')) return copy(car)
    return f.request(path, options)
  }
  const plan = await executor().prepare(command('service_add', { serviceName: 'Мойка', price: 500 }, { registrationPlate: 'A123BC154' }), context, { request })
  car.registrationPlate = 'A555BC154'
  await assert.rejects(plan.execute({ draftToken: 'plate-changed' }), /Данные изменились/)
  assert.equal(f.mutations.length, 0)
})

test('a foreign payment between completion steps prevents another payment', async () => {
  const f = fixture({ maintenanceDetails: [{ detailId: 'one', name: 'Мойка', price: 500, quantity: 1 }] })
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (!options) return copy(f.current)
    f.mutations.push({ path, ...copy(options) })
    if (path.includes('/workflow?')) {
      f.current.status = 3
      f.current.completedDate = '2026-10-26'
      f.current.paidAmount = 500
    }
    return {}
  }
  const plan = await executor().prepare(command('complete', {}, { date: '2026-10-26', paidAmount: 500 }), context, { request })
  await assert.rejects(plan.execute({ draftToken: 'foreign-payment' }), /между шагами/)
  await assert.rejects(plan.execute({ draftToken: 'foreign-payment' }), /частично/)
  assert.equal(f.mutations.length, 1)
})

test('moving a multi-day appointment keeps its duration and workflow can calculate its own end date', async () => {
  const f = fixture({ plannedDate: '2026-10-26', plannedEndDate: '2026-10-28' })
  const update = await executor().prepare(command('maintenance_update', {}, { date: '2026-11-01' }), context, { request: f.request })
  await update.execute({ draftToken: 'move-fields' })
  assert.equal(f.mutations[0].body.plannedEndDate, '2026-11-03')
  const workflow = await executor().prepare(command('reschedule', {}, { date: '2026-11-01' }), context, { request: f.request })
  await workflow.execute({ draftToken: 'move-workflow' })
  assert.equal('plannedEndDate' in f.mutations[1].body, false)
})

test('client mutations require a named identity even if there is only one client', async () => {
  await assert.rejects(executor().prepare({ action: 'client_update', parameters: { newPhone: '79000000002' } }, context, { request: async () => { throw new Error('Unexpected lookup') } }), /Назовите имя/)
})

test('completion accounts for server readiness and default material costs before taking payment', async () => {
  const f = fixture({ maintenanceDetails: [] })
  const request = async (path, options) => {
    if (path.endsWith('/scenario-catalog')) return { services: [] }
    if (!options) return copy(f.current)
    f.mutations.push({ path, ...copy(options) })
    f.current.version += 1
    if (path.includes('/services?')) f.current.maintenanceDetails = options.body.details.map(item => ({ ...item, detailId: 'new-line', materialUnitCost: 0, materialTotalCost: 0 }))
    if (path.includes('/workflow?')) { f.current.status = 3; f.current.completedDate = '2026-10-26'; f.current.readyForHandoverAt = '2026-10-26T10:00:00Z' }
    if (path.includes('/payments?')) f.current.paidAmount = options.body.amount
    return {}
  }
  const plan = await executor().prepare(command('complete', {}, { date: '2026-10-26', services: [{ name: 'Диагностика', price: null }], totalAmount: 1500, paidAmount: 1500 }), context, { request })
  await plan.execute({ draftToken: 'ready-and-paid' })
  assert.equal(f.mutations.length, 3)
  assert.equal(f.current.paidAmount, 1500)
})

test('explicit clearing removes time and mileage without changing the date or work rows', async () => {
  const f = fixture({ mileage: 500, plannedStartTime: '09:00:00', plannedEndTime: '11:00:00' })
  const plan = await executor().prepare(command('maintenance_update', { clearTime: true, clearMileage: true }), context, { request: f.request })
  await plan.execute({ draftToken: 'clear' })
  assert.equal(f.mutations[0].body.plannedDate, '2026-10-26')
  assert.equal(f.mutations[0].body.plannedStartTime, null)
  assert.equal(f.mutations[0].body.plannedEndTime, null)
  assert.equal(f.mutations[0].body.mileage, null)
  assert.equal(f.mutations[0].body.details.length, 3)
})

test('employee unassignment must be explicit', async () => {
  const f = fixture()
  await assert.rejects(executor().prepare(command('assign_employee'), context, { request: f.request }), /явно попросите/)
  const plan = await executor().prepare(command('assign_employee', { employeeName: '' }), context, { request: f.request })
  await plan.execute({ draftToken: 'unassign' })
  assert.equal(f.mutations[0].body.employeeId, null)
})

test('renaming cannot silently drop a historical catalog reference', async () => {
  const f = fixture()
  await assert.rejects(executor().prepare(command('service_update', { lineNumber: 1, newName: 'Новое название' }), context, { request: f.request }), /историческое название/)
  assert.equal(f.mutations.length, 0)
  assert.equal(f.current.maintenanceDetails[0].catalogItemId, 'catalog-oil')
})
