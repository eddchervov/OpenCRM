export function remapImportProfileMappings(profile, tables) {
  if (!profile || !Array.isArray(profile.tables) || !Array.isArray(profile.mappings) || !Array.isArray(tables)) return null
  if (profile.tables.length !== tables.length || profile.mappings.length !== tables.length) return null
  const tableIds = tables.map(table => table?.id)
  const profileTableIds = profile.tables.map(table => table?.id)
  const mappingTableIds = profile.mappings.map(mapping => mapping?.tableId)
  if (new Set(tableIds).size !== tables.length || new Set(profileTableIds).size !== profile.tables.length ||
    new Set(mappingTableIds).size !== profile.mappings.length) return null

  if (profile.tables.length === 1) {
    const profileTable = profile.tables[0]
    const table = tables[0]
    if (!hasSameOrderedColumns(profileTable, table) || profile.mappings[0]?.tableId !== profileTable?.id) return null

    return [{ ...profile.mappings[0], tableId: table.id }]
  }

  const structuresMatch = profile.tables.every(profileTable => {
    const table = tables.find(item => item?.id === profileTable?.id)
    return Boolean(table && profileTable && table.name === profileTable.name && hasSameOrderedColumns(profileTable, table))
  })
  if (!structuresMatch || profile.mappings.some(mapping => !tables.some(table => table?.id === mapping?.tableId))) return null

  return profile.mappings.map(mapping => ({ ...mapping }))
}

function hasSameOrderedColumns(first, second) {
  return Boolean(first && second && Array.isArray(first.columns) && Array.isArray(second.columns) &&
    first.columns.length === second.columns.length &&
    first.columns.every((column, index) => column === second.columns[index]))
}
