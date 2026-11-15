import { createApp, reactive } from 'vue'
import { UiTooltip } from '@opencrm/ui'
import '@opencrm/ui/styles.css'
import '../packages/workspace/src/styles.css'
import '../Scenarios/detailing-client/src/styles.css'
import ReelsPreview from './ReelsPreview.vue'

export const organizationId = '00000000-0000-0000-0000-000000000001'
const today = new Date().toLocaleDateString('sv-SE')
export const demo = reactive({
  paid: 0,
  payments: [] as any[],
  reports: [] as any[],
  work: { maintenanceId: 'demo-order', detailId: 'demo-work', name: 'Полировка кузова', vehicleName: 'BMW X3', registrationPlate: 'ДЕМО', location: 'Пост 1', plannedDate: today, plannedStartTime: '09:00', plannedEndDate: today, plannedEndTime: '18:00', price: 10000, quantity: 1, status: 1, version: 1, activeTime: '00:00:00', startedAt: null as string | null, pauseReason: '' },
  extra: [] as any[]
})
const services = [
  { id: 'wash', name: 'Детейлинг-мойка', unitPrice: 2500 },
  { id: 'polish', name: 'Полировка кузова', unitPrice: 10000 },
  { id: 'ceramic', name: 'Керамическое покрытие', unitPrice: 7500 }
].map(item => ({ ...item, kindCode: 'service', isActive: true }))

// Все операции выполняются только с локальными тестовыми данными в памяти вкладки.
const originalFetch = window.fetch.bind(window)
window.fetch = async (input, init) => {
  const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, location.origin)
  if (!url.pathname.startsWith('/api/')) return originalFetch(input, init)
  const path = url.pathname
  const body = init?.body ? JSON.parse(String(init.body)) : {}
  if (path.endsWith('/payments')) {
    if (init?.method === 'POST') {
      demo.paid += body.amount
      demo.payments.push({ ...body, createdAt: new Date().toISOString(), createdBy: 'Администратор', payerName: 'Демо-клиент' })
    }
    return Response.json(demo.payments)
  }
  if (path.endsWith('/scenario-catalog')) return Response.json({ services, serviceBundles: [{ id: 'complex', name: 'Комплекс «Защита кузова»', kindCode: 'service_bundle', isActive: true, serviceIds: services.map(item => item.id) }] })
  if (path.endsWith('/employees')) return Response.json({ items: [] })
  if (path.endsWith('/assigned')) return Response.json([demo.work])
  if (path.endsWith('/transition')) {
    demo.work.status = body.action === 'pause' ? 3 : body.action === 'complete' ? 4 : 2
    demo.work.version++
    demo.work.startedAt ||= new Date().toISOString()
    demo.work.pauseReason = body.reason || ''
    return Response.json(demo.work)
  }
  if (path.endsWith('/decision')) {
    const report = demo.reports.find(item => path.includes(item.id))
    if (!report) return new Response(null, { status: 404 })
    Object.assign(report, { decision: body.decision, approvedPrice: body.price })
    if (body.decision === 'approved') demo.extra.push({ detailId: 'extra-work', name: report.text, employeeName: 'Алексей', status: 1, version: 1, activeTime: '00:00:00' })
    return Response.json(report)
  }
  if (path.endsWith('/reports')) {
    if (init?.method === 'POST') {
      const report = { ...body, id: 'demo-report', maintenanceId: 'demo-order', detailId: 'demo-work', employeeName: 'Алексей', createdAt: new Date().toISOString(), decision: null }
      demo.reports.push(report)
      return Response.json(report)
    }
    return Response.json(demo.reports)
  }
  if (path.endsWith('/progress')) return Response.json([{ ...demo.work, employeeName: 'Алексей' }, ...demo.extra])
  if (path.endsWith('/production-report')) return Response.json(null)
  if (path.endsWith('/occupied-slots')) return Response.json(url.searchParams.get('excludeMaintenanceId') ? [] : [{ id: 'ceramic-order', startMinute: -900, endMinute: 2460, vehicle: 'BMW X3', service: 'Керамическое покрытие', startAt: '2026-10-12T09:00:00', endAt: '2026-10-14T17:00:00' }])
  if (['/options', '/mine', '/available', '/notifications'].some(suffix => path.endsWith(suffix))) return Response.json([])
  return new Response(null, { status: 404 })
}
createApp(ReelsPreview).component('UiTooltip', UiTooltip).mount('#app')
