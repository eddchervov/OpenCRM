import type { WorkspaceScenarioCatalogItem } from './api/scenarioCatalog'
import type { WorkspaceMaintenanceDetail } from './api/maintenance'

export type SelectedServiceBundle = { serviceIds: string[], durationMinutes: number }

export function formatServiceDuration(minutes: number) {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60

  return [hours ? `${hours} ч` : '', rest ? `${rest} мин` : ''].filter(Boolean).join(' ')
}

export function getUnknownServiceDurationSaveMessage(hasManualEnd: boolean) {
  return hasManualEnd
    ? 'Сохранено. Длительность части услуг не задана; указанное время окончания сохранено.'
    : 'Сохранено. Длительность части услуг не задана; автоматический расчёт окончания пропущен.'
}

export function calculateServiceDuration(
  rows: WorkspaceMaintenanceDetail[],
  services: WorkspaceScenarioCatalogItem[],
  bundles: SelectedServiceBundle[] = []
): number | null | undefined {
  if (!rows.length) return undefined
  const quantities = new Map(rows.map(row => [row.catalogItemId, row.quantity ?? 1]))
  let total = 0
  for (const bundle of bundles) {
    if (!bundle.serviceIds.every(id => (quantities.get(id) ?? 0) >= 1)) continue
    total += bundle.durationMinutes
    for (const id of bundle.serviceIds) quantities.set(id, quantities.get(id)! - 1)
  }
  for (const [id, quantity] of quantities) {
    if (!quantity) continue
    const duration = services.find(item => item.id === id)?.durationMinutes
    if (duration == null || duration <= 0) return null
    total += duration * quantity
  }

  return total > 0 ? total : undefined
}
