import { createApp, defineComponent, h, ref } from 'vue'
import WorkspaceCrmShell from '../packages/workspace/src/components/WorkspaceCrmShell.vue'
import WorkspaceBillingSection from '../packages/workspace/src/components/WorkspaceBillingSection.vue'
import WorkspaceAssistantSection from '../packages/workspace/src/components/WorkspaceAssistantSection.vue'
import WorkspaceProLock from '../packages/workspace/src/components/WorkspaceProLock.vue'
import { proFeatureDetails, type ProFeatureKey } from '../packages/workspace/src/proFeatureDetails'
import type { WorkspaceContext } from '../packages/workspace/src/types'
import { syncWorkspaceThemeFromDocument } from '../packages/ui/src/workspaceTheme'
import '../packages/ui/src/styles.css'
import '../packages/workspace/src/styles.css'

const organizationId = '11111111-1111-1111-1111-111111111111'
const menu = [
  { code: 'workspace', title: 'Рабочий день', groupTitle: 'Ежедневная работа', isAvailable: true },
  { code: 'scheduler', title: 'Записи', groupTitle: 'Ежедневная работа', isAvailable: true },
  { code: 'clients', title: 'Клиенты', groupTitle: 'Клиенты', isAvailable: true },
  { code: 'vehicles', title: 'Автомобили', groupTitle: 'Клиенты', isAvailable: true },
  { code: 'messages', title: 'Сообщения', groupTitle: 'Клиенты', isAvailable: false },
  { code: 'materials', title: 'Склад', groupTitle: 'Ресурсы', isAvailable: true },
  { code: 'employees', title: 'Команда', groupTitle: 'Ресурсы', isAvailable: false },
  { code: 'shifts', title: 'Смены', groupTitle: 'Ресурсы', isAvailable: false },
  { code: 'members', title: 'Роли и права', groupTitle: 'Ресурсы', isAvailable: false },
  { code: 'statistics', title: 'Статистика', groupTitle: 'Отчёты', isAvailable: false },
  { code: 'billing', title: 'Тарифы', groupTitle: 'Справка и доступ', isAvailable: true }
].map(item => ({ ...item, isVisible: true, urlTemplate: `#${item.code}`, sectionCodes: [item.code] }))

const context = {
  user: { id: 'preview-owner', email: 'contact@example.invalid', displayName: 'Тестовый владелец' },
  organization: { id: organizationId, name: 'Детейлинг-студия', primaryScenarioCode: 'detailing', canManageBilling: true },
  workspaceAccess: { isFreePlan: true, isBaseWorkspace: true, hasActiveSubscription: false, isReadOnly: false, exportsEnabled: true, roleCode: 'owner' },
  shell: { menu, scenario: { code: 'detailing', title: 'Детейлинг', modules: { assistant: false, messaging: false, intake: false, photos: false, members: false, materials: true } } },
  links: {}
} as WorkspaceContext

const parameters = new URLSearchParams(window.location.search)
const previewTheme = parameters.get('theme')
if (previewTheme === 'light' || previewTheme === 'dark') {
  document.documentElement.dataset.crmTheme = previewTheme
  syncWorkspaceThemeFromDocument()
}
if (parameters.has('member')) {
  context.organization.canManageBilling = false
  context.shell.menu = menu.filter(item => item.code !== 'billing')
  context.workspaceAccess = { ...context.workspaceAccess, isReadOnly: true, roleCode: 'employee' }
}
const selectedFeature = parameters.get('feature') as ProFeatureKey | null

const billing = {
  organization: context.organization,
  effectivePlan: { id: 1, name: 'Бесплатный', group: 'workspace.free.v1' },
  workspaceFeatures: [{ code: 'workspace.clients.limit', intValue: 300 }],
  workspaceVehicleLimit: 300,
  workspaceMembersLimit: 1,
  availableBusinessPlans: parameters.has('overview') ? [{
    id: 2, name: 'PRO', price: 990, durationDays: 30, vehicleLimit: 10000,
    features: [...['workspace.maintenance.intake', 'vehicle.photo_albums', 'workspace.module.members',
      'workspace.module.analytics', 'workspace.assistant', 'workspace.client_messaging']
      .map(code => ({ code, boolValue: true })),
      { code: 'workspace.clients.limit', intValue: 2147483647 },
      { code: 'workspace.members.limit', intValue: 50 }]
  }] : [],
  demoBusinessAvailable: false,
  canExtendBusinessSubscription: false,
  businessDaysUntilExpiry: 0,
  canManageBilling: false
}

// Предпросмотр не обращается к рабочему API и не выполняет бизнес-действий.
globalThis.fetch = async input => new Response(JSON.stringify(
  String(input).includes('/notifications') ? [] : { items: [], unreadCount: 0, revision: 'preview' }
), { headers: { 'Content-Type': 'application/json' } })

createApp(defineComponent({
  setup() {
    const activeSection = ref(window.location.hash.slice(1) || 'workspace')
    const select = (code: string) => { activeSection.value = code; window.location.hash = code }
    window.addEventListener('hashchange', () => { activeSection.value = window.location.hash.slice(1) || 'workspace' })
    return () => h(WorkspaceCrmShell, {
      context,
      sections: parameters.has('member') ? menu.filter(item => item.code !== 'billing') : menu,
      activeSection: activeSection.value,
      breadcrumbs: [{ title: 'Детейлинг-студия', href: '#workspace' }, { title: menu.find(item => item.code === activeSection.value)?.title || 'Рабочий день' }],
      profileLinks: [],
      displayName: parameters.has('member') ? 'Сотрудник' : 'Тестовый владелец',
      onSelect: select
    }, {
      default: () => activeSection.value === 'assistant'
        ? h(WorkspaceAssistantSection, { context })
        : activeSection.value === 'feature' && selectedFeature && proFeatureDetails[selectedFeature]
        ? h(WorkspaceProLock, { locked: true, feature: selectedFeature, message: `${proFeatureDetails[selectedFeature].title} · PRO`, centered: true }, () => h('section', { style: 'min-height:420px;padding:24px;' }, proFeatureDetails[selectedFeature].description))
        : activeSection.value === 'billing'
        ? h(WorkspaceBillingSection, { billing: billing as never, enabledFeatures: [], isSaving: false })
        : h('section', { style: 'padding:24px;color:var(--crm-text);' }, [
          h('h1', menu.find(item => item.code === activeSection.value)?.title || 'Рабочий день'),
          h('p', activeSection.value === 'materials'
            ? 'Склад доступен бесплатно: материалы, остатки, поступления и списания в заказах.'
            : 'Бесплатный тариф: до 300 клиентов и 300 автомобилей.'),
          h('p', 'Склад входит в бесплатный тариф. Выберите «Команда» или «Смены», чтобы проверить ограничение PRO.')
        ])
    })
  }
})).mount('#app')
