import test from 'node:test'
import assert from 'node:assert/strict'
import { remapImportProfileMappings } from '../packages/workspace/src/importProfileStructure.mjs'

const tables = [{ id: 'clients', name: 'Клиенты', columns: ['ID', 'Телефон'] }]
const profile = {
  tables: [{ id: 'clients', name: 'Клиенты', columns: ['ID', 'Телефон'], rows: [] }],
  mappings: [{ tableId: 'clients', kind: 'client', fields: { clientId: [0] } }]
}

test('profile applies to identical table structure', () => {
  assert.deepEqual(remapImportProfileMappings(profile, tables), profile.mappings)
})

test('single-table profile accepts another file name only when ordered columns match and remaps table ID', () => {
  const currentTable = { id: 'new-file', name: 'crm-2026-10-16.csv', columns: ['ID', 'Телефон'] }
  const mappings = remapImportProfileMappings(profile, [currentTable])

  assert.equal(mappings[0].tableId, 'new-file')
})

test('single-table profile rejects reordered columns', () => {
  assert.equal(remapImportProfileMappings(profile, [{ ...tables[0], columns: ['Телефон', 'ID'] }]), null)
})

test('multiple-table profile rejects changed table identities and ambiguous matches', () => {
  const multipleProfile = {
    tables: [
      { id: 'clients', name: 'Клиенты', columns: ['ID', 'Имя'], rows: [] },
      { id: 'vehicles', name: 'Автомобили', columns: ['ID', 'VIN'], rows: [] }
    ],
    mappings: [
      { tableId: 'clients', kind: 'client', fields: { clientId: [0] } },
      { tableId: 'vehicles', kind: 'vehicle', fields: { vehicleId: [0] } }
    ]
  }
  const renamedTables = [
    { id: 'new-clients', name: 'Покупатели', columns: ['ID', 'Имя'] },
    { id: 'new-vehicles', name: 'Транспорт', columns: ['ID', 'VIN'] }
  ]

  assert.equal(remapImportProfileMappings(multipleProfile, renamedTables), null)
  assert.equal(remapImportProfileMappings(profile, []), null)
  assert.equal(remapImportProfileMappings({ ...profile, mappings: [] }, tables), null)
  assert.equal(remapImportProfileMappings(profile, [...tables, tables[0]]), null)
})

test('profile rejects malformed structure', () => {
  assert.equal(remapImportProfileMappings({ tables: null, mappings: [] }, tables), null)
  assert.equal(remapImportProfileMappings(profile, [{ id: 'clients', name: 'Клиенты', columns: null }]), null)
})
