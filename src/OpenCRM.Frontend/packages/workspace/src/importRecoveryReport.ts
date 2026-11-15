type RecoveryReport = {
  draftId: string
  revision: number
  rowEdits: Array<{ id: string, changes: Partial<Record<string, string | boolean | null>> }>
  mappings?: Array<{ tableId: string, kind: string, fields: Record<string, number[]> }>
}

function csvCell(value: unknown): string {
  const text = String(value ?? '')
  const safe = /^\s*[=+@-]/u.test(text) || /^[\t\r\n]/u.test(text) ? `'${text}` : text
  return `"${safe.replaceAll('"', '""')}"`
}

export function buildImportRecoveryReport(recovery: RecoveryReport): string {
  const rows: unknown[][] = [['Черновик', 'Версия', 'Тип правки', 'ID строки или таблицы', 'Поле', 'Локальное значение']]
  for (const row of recovery.rowEdits) {
    for (const [field, value] of Object.entries(row.changes)) {
      rows.push([recovery.draftId, recovery.revision, 'Строка', row.id, field, value])
    }
  }
  for (const mapping of recovery.mappings ?? []) {
    rows.push([recovery.draftId, recovery.revision, 'Сопоставление', mapping.tableId, 'kind', mapping.kind])
    for (const [field, columns] of Object.entries(mapping.fields)) {
      rows.push([recovery.draftId, recovery.revision, 'Сопоставление', mapping.tableId, field, columns.map(index => index + 1).join(', ')])
    }
  }
  return '\uFEFF' + rows.map(row => row.map(csvCell).join(';')).join('\r\n')
}
