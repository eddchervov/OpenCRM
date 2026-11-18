import { i18n, localeCultures, translate as t } from '@opencrm/ui/i18n'
import '@opencrm/workspace/bookingTimeMessages'
import type { WorkspaceScenarioCatalogItem } from './api/scenarioCatalog'
import type { WorkspaceMaintenanceDetail } from './api/maintenance'

export type SelectedServiceBundle = { serviceIds: string[], durationMinutes: number }

export function formatServiceDuration(minutes: number) {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60

  const locale = i18n.global.locale.value
  const unit = (amount: number, name: string, suffix: string) => locale === 'ru'
    ? `${amount} ${suffix}`
    : new Intl.NumberFormat(localeCultures[locale], { style: 'unit', unit: name, unitDisplay: 'short' }).format(amount)

  return [hours ? unit(hours, 'hour', 'ч') : '', rest ? unit(rest, 'minute', 'мин') : ''].filter(Boolean).join(' ')
}

export function getUnknownServiceDurationSaveMessage(hasManualEnd: boolean) {
  return hasManualEnd
    ? t('bookingTime.savedManual')
    : t('bookingTime.savedAuto')
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
