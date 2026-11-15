import type { WorkspaceScenarioCatalogItem } from '../../api/scenarioCatalog'
import type { WorkspaceWorkday, WorkspaceWorkdayItem } from '../../api/workday'

export function createVisualizationDemo(today: string): { catalog: WorkspaceScenarioCatalogItem[]; day: WorkspaceWorkday } {
  const catalog = ['Пост 1', 'Пост 2', 'Пост 3', 'Пост 4'].map((name, index) => ({
    id: 'demo-post-' + index, kindCode: 'work_post', code: 'demo-' + index, name,
    sortOrder: index, isSystem: false, isUsed: true, isActive: true
  }))

  const item = (id: string, vehicleName: string, location: string, services: string[]): WorkspaceWorkdayItem => ({
    id, vehicleName, location, services, date: today, startTime: '00:00', endTime: '23:59',
    registrationPlate: 'А' + id.padStart(3, '0') + 'АА 154',
    workTotalCount: services.length, workCompletedCount: 0, workPausedCount: 0, performer: 'Мастер'
  })

  const washing = item('1', 'BMW X3', 'Пост 1', ['Мойка кузова', 'Уборка салона'])
  washing.vehicleBrandLogoKey = 'bmw'
  washing.workCompletedCount = 1
  const waiting = item('2', 'Toyota Camry', 'Пост 2', ['Полировка кузова'])
  waiting.vehicleBrandLogoKey = 'toyota'
  const paused = item('3', 'Kia Sportage', 'Пост 3', ['Химчистка салона', 'Сушка'])
  paused.vehicleBrandLogoKey = 'kia'
  paused.workPausedCount = 1
  const ready = item('4', 'Volkswagen Tiguan', '', ['Защитное покрытие'])
  ready.vehicleBrandLogoKey = 'volkswagen'
  ready.workCompletedCount = ready.workTotalCount
  ready.readyForHandoverAt = `${today}T08:00:00Z`

  return {
    catalog,
    day: {
      hasAnyWork: true,
      planned: { totalCount: 1, items: [waiting] },
      inProgress: { totalCount: 2, items: [washing, paused] },
      ready: { totalCount: 1, items: [ready] },
      handedOver: { totalCount: 0, items: [] }
    }
  }
}
