import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import { legacyClientText } from './legacyClientTestMessages.mjs'

const webRoot = new URL('../../OpenCRM.WebApp/wwwroot/', import.meta.url)
const fixedNow = '2026-11-15T16:30:00Z'

function page(filename) {
  let definition
  function Vue(options) { definition = options }
  Vue.component = () => {}
  Vue.options = { components: {} }
  const window = { OpenCRMText: legacyClientText('en') }
  const sandbox = vm.createContext({
    window, Vue, VueSelect: { VueSelect: {} }, Intl,
    maintenanceId: 'record-1', vehicleId: 'vehicle-1',
    getVehicleDisplayName: () => 'Vehicle',
    Date: class extends Date {
      constructor(...args) { super(...(args.length ? args : [fixedNow])) }
      static now() { return new Date(fixedNow).getTime() }
    },
    document: {
      querySelector: selector => ({ content: selector.includes('opencrm-time-zone') ? 'Asia/Shanghai' : selector.includes('formatting-culture') ? 'en-GB' : '' }),
      getElementById: () => ({ dataset: { messages: JSON.stringify({ dateCalendarFormat: 'DD/MM/YYYY' }) } })
    }
  })
  for (const source of ['lib/momentjs.min.js', 'app/regional-formatting.js', 'app/' + filename]) {
    vm.runInContext(readFileSync(new URL(source, webRoot), 'utf8'), sandbox, { filename: source })
  }
  return { ...definition.data(), ...definition.methods }
}

test('Просрочка карточки организации определяется календарным днём организации', () => {
  const state = page('maintenance.organization.js')
  state.maintenance = { status: 1, plannedDate: '2026-11-15' }
  assert.equal(state.isMaintenanceOverdue(), true)
  state.maintenance.plannedDate = '2026-11-16'
  assert.equal(state.isMaintenanceOverdue(), false)
  state.maintenance = { status: 3, plannedDate: '2026-11-15' }
  assert.equal(state.isMaintenanceOverdue(), false)
  state.maintenance.status = 4
  assert.equal(state.isMaintenanceOverdue(), false)
})

test('История автомобиля использует тот же день организации для просрочки', () => {
  const state = page('vehicle-card.personal.js')
  assert.equal(state.isMaintenanceOverdue({ status: 1, plannedDate: '2026-11-15' }), true)
  assert.equal(state.isMaintenanceOverdue({ status: 1, plannedDate: '2026-11-16' }), false)
  assert.equal(state.isMaintenanceOverdue({ status: 3, plannedDate: '2026-11-15' }), false)
})

test('Создание и смена статуса сохраняют ISO дату организации, существующие даты не заменяются', () => {
  for (const source of ['create-maintenance.organization.page.js', 'update-maintenance.organization.page.js']) {
    const state = page(source)
    if (source.startsWith('create')) assert.equal(state.selectedMaintenance.plannedDate, '2026-11-16')
    state.selectedMaintenance = { status: 3, plannedDate: null, completedDate: null }
    state.changeStatus()
    assert.equal(state.selectedMaintenance.completedDate, '2026-11-16')
    state.selectedMaintenance = { status: 1, plannedDate: null, completedDate: null }
    state.changeStatus()
    assert.equal(state.selectedMaintenance.plannedDate, '2026-11-16')
    state.selectedMaintenance = { status: 3, plannedDate: '2026-11-12', completedDate: null }
    state.changeStatus()
    assert.equal(state.selectedMaintenance.completedDate, '2026-11-12')
  }
})
