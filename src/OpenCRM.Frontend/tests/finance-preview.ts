import { createApp, defineComponent, h, ref, watch } from 'vue'
import FinanceSection from '../packages/workspace/src/components/FinanceSection.vue'
import type { WorkspaceFinanceSummary } from '../packages/workspace/src/api/finance'
import UiDatePicker from '../packages/ui/src/components/UiDatePicker.vue'
import type { WorkspaceContext } from '../packages/workspace/src/types'
import { syncWorkspaceThemeFromDocument } from '../packages/ui/src/workspaceTheme'
import '../packages/ui/src/styles.css'
import '../packages/workspace/src/styles.css'

const organizationId = '11111111-1111-1111-1111-111111111111'
const parameters = new URLSearchParams(window.location.search)
const theme = parameters.get('theme')
if (theme === 'light' || theme === 'dark') {
  document.documentElement.dataset.crmTheme = theme
  syncWorkspaceThemeFromDocument()
}

const menu = [
  { code: 'finance', title: 'Финансы', urlTemplate: '#finance', isVisible: true, isAvailable: true },
  { code: 'statistics', title: 'Статистика', urlTemplate: '#statistics', isVisible: true, isAvailable: true }
]
const context = {
  user: { id: 'preview-owner', email: 'contact@example.invalid', displayName: 'Тестовый владелец' },
  organization: {
    id: organizationId,
    name: 'Детейлинг-студия · тестовые данные',
    primaryScenarioCode: 'detailing',
    timeZoneId: 'Asia/Novosibirsk',
    canManageBilling: true
  },
  workspaceAccess: {
    isFreePlan: true,
    isBaseWorkspace: true,
    hasActiveSubscription: false,
    isReadOnly: false,
    exportsEnabled: false,
    roleCode: 'owner'
  },
  shell: {
    menu,
    scenario: {
      code: 'detailing',
      title: 'Детейлинг',
      modules: { accountingExport: false }
    }
  },
  links: {}
} as WorkspaceContext

const financeSummary: WorkspaceFinanceSummary = {
  dateFrom: '',
  dateTo: '',
  kpis: {
    ordersCount: 0,
    grossAmount: 0,
    netAmount: 0,
    paidAmount: 0,
    debtAmount: 0,
    discountAmount: 0,
    materialCostAmount: 0,
    laborCostAmount: 0,
    costAmount: 0,
    profitAmount: 0,
    averageCheckAmount: 0
  },
  revenueSeries: [],
  accruedSeries: [],
  costBreakdown: [],
  paymentBreakdown: [],
  topServices: [],
  topDebtors: [],
  orders: []
}

if (parameters.has('cancelled')) {
  financeSummary.kpis.paidAmount = 200
  financeSummary.kpis.materialCostAmount = 20
  financeSummary.kpis.costAmount = 20
  financeSummary.kpis.profitAmount = -20
  financeSummary.costBreakdown = [{ code: 'materials', label: 'Материалы', value: 20, percent: 100 }]
  financeSummary.paymentBreakdown = [{ code: 'paid', label: 'Оплачено', value: 200, percent: 100 }]
  financeSummary.revenueSeries = [{ date: '2026-10-30', label: '30.10', value: 200 }]
  financeSummary.accruedSeries = [{ date: '2026-10-30', label: '30.10', value: 0 }]
  financeSummary.orders = [{
    id: 'qa-cancelled', date: '2026-10-30', vehicleId: 'qa-vehicle',
    vehicleDisplayName: 'QA · Отменённая запись', vehicleSubtitle: 'Е105КХ154',
    isCancelled: true, grossAmount: 500, netAmount: 0, paidAmount: 200, debtAmount: 0,
    discountAmount: 0, materialCostAmount: 20, laborCostAmount: 0, profitAmount: -20,
    paymentStatus: 'overpaid', paymentStatusName: 'Переплата', detailUrl: '#maintenance/qa-cancelled'
  }]
}

globalThis.fetch = async input => {
  const url = new URL(typeof input === 'string' ? input : input.url, window.location.origin)

  if (url.pathname.endsWith('/finance')) {
    return Response.json({
      ...financeSummary,
      dateFrom: url.searchParams.get('dateFrom') ?? '',
      dateTo: url.searchParams.get('dateTo') ?? ''
    })
  }

  if (url.pathname.endsWith('/accounting-export/capabilities')) {
    return Response.json({
      canExport: true,
      canExportBp3: false,
      canExportOrders: true,
      canExportClients: false,
      canExportVehicles: false,
      canExportCatalog: false,
      canExportMaterials: false
    })
  }

  if (url.pathname.endsWith('/accounting-export/preview')) {
    return Response.json({
      schemaVersion: 1,
      ordersCount: 0,
      completedOrdersCount: 0,
      ordersNeedingReviewCount: 0,
      paymentsCount: 0,
      paymentsNeedingReviewCount: 0,
      validationIssues: []
    })
  }

  if (url.pathname.endsWith('/accounting-export')) {
    return new Response(new Blob(['{}'], { type: 'application/json' }), {
      headers: { 'X-Accounting-Issues': '0' }
    })
  }

  return new Response(null, { status: 404 })
}

function readFinanceRoute() {
  const [route, query = ''] = window.location.hash.slice(1).split('?')
  const routeParameters = new URLSearchParams(query)

  return {
    isExportOpen: route === 'finance/export',
    dateFrom: routeParameters.get('dateFrom') ?? undefined,
    dateTo: routeParameters.get('dateTo') ?? undefined
  }
}

const disabledPickerDate = ref('2026-10-01')

createApp(defineComponent({
  setup() {
    const route = ref(readFinanceRoute())
    if (!window.location.hash) window.location.hash = '#finance'
    window.addEventListener('hashchange', () => { route.value = readFinanceRoute() })
    watch(() => route.value, value => {
      if (value.isExportOpen) document.title = 'OpenCRM — выгрузка данных'
      else document.title = 'OpenCRM — финансы'
    })

    return () => h('main', { class: 'finance-preview' }, [
      h('header', { class: 'finance-preview__header' }, [
        h('p', { class: 'finance-preview__eyebrow' }, 'OpenCRM · локальный предпросмотр'),
        h('h1', 'Финансы и выгрузка данных'),
        h('p', 'Тестовая Free-организация и локальные ответы API. Данные не сохраняются.')
      ]),
      h(FinanceSection, {
        context,
        initialDateFrom: route.value.dateFrom,
        initialDateTo: route.value.dateTo,
        initialExportOpen: route.value.isExportOpen
      }),
      parameters.has('disabled-picker')
        ? h('section', { class: 'finance-preview__disabled-picker' }, [
          h('h2', 'Проверка отключённого общего датапикера'),
          h(UiDatePicker, {
            modelValue: disabledPickerDate.value,
            'onUpdate:modelValue': (value: string) => { disabledPickerDate.value = value },
            label: 'Отключённая дата',
            disabled: true
          })
        ])
        : null
    ])
  }
})).mount('#app')
