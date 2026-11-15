<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref } from 'vue'
import { UiLoader, type ProfileLink } from '@opencrm/ui'
import {
  WorkspaceCrmShell,
  createRouteScopedNotice,
  FinanceSection,
  isWorkspaceCommonSectionCode,
  pushHashState,
  withPathBase,
  type WorkspaceBreadcrumbItem
} from '@opencrm/workspace'
import {
  loadAutoServiceContext,
  resolveHostConfig,
  type AutoServiceWorkspaceContext
} from '@/api/autoserviceBff'
import MaterialsSection from '@/components/MaterialsSection.vue'
import { buildAutoServiceSections, isAutoServiceSectionCode, type AutoServiceSectionCode } from '@/sections'
import WorkspaceTariffExpiryNotice from '@opencrm/workspace/components/WorkspaceTariffExpiryNotice'

const ClientCardSection = defineAsyncComponent(() => import('@opencrm/workspace/components/ClientCardSection'))
const MessagesSection = defineAsyncComponent(() => import('@opencrm/workspace/components/MessagesSection'))
const InquiriesSection = defineAsyncComponent(() => import('@opencrm/workspace/components/InquiriesSection'))
const ClientsSection = defineAsyncComponent(() => import('@opencrm/workspace/components/ClientsSection'))
const MaintenanceCardSection = defineAsyncComponent(() => import('@opencrm/workspace/components/MaintenanceCardSection'))
const MaintenanceFormSection = defineAsyncComponent(() => import('@opencrm/workspace/components/MaintenanceFormSection'))
const VehicleCardSection = defineAsyncComponent(() => import('@opencrm/workspace/components/VehicleCardSection'))
const VehicleFormSection = defineAsyncComponent(() => import('@opencrm/workspace/components/VehicleFormSection'))
const VehicleMediaSection = defineAsyncComponent(() => import('@opencrm/workspace/components/VehicleMediaSection'))
const VehiclesSection = defineAsyncComponent(() => import('@opencrm/workspace/components/VehiclesSection'))
const WorkspaceCommonSection = defineAsyncComponent(() => import('@opencrm/workspace/components/WorkspaceCommonSection'))
const WorkspaceAssistantSection = defineAsyncComponent(() => import('@opencrm/workspace/components/WorkspaceAssistantSection'))
const OverviewSection = defineAsyncComponent(() => import('@/components/OverviewSection.vue'))
const VisualizationSection = defineAsyncComponent(() => import('@opencrm/workspace/components/WorkspaceVisualizationSection'))
const QuickBookingSection = defineAsyncComponent(() => import('@/components/QuickBookingSection.vue'))
const DefectsSection = defineAsyncComponent(() => import('@/components/DefectsSection.vue'))
const CountersSection = defineAsyncComponent(() => import('@/components/CountersSection.vue'))
const SchedulerSection = defineAsyncComponent(() => import('@/components/SchedulerSection.vue'))
const WorkJournalSection = defineAsyncComponent(() => import('@/components/WorkJournalSection.vue'))

const host = resolveHostConfig()
const context = ref<AutoServiceWorkspaceContext | null>(null)
const isLoading = ref(true)
const errorMessage = ref('')
const { message: bookingSuccessMessage, show: showRouteNotice, navigate: clearRouteNoticeUnlessTarget } = createRouteScopedNotice()
const initialRoute = resolveRouteFromHash()
const activeSection = ref<AutoServiceSectionCode>(initialRoute.section)
const activeEntityId = ref(initialRoute.entityId)
const activeAction = ref(initialRoute.action)
const activeChildId = ref(initialRoute.childId)
const activeQuery = ref(initialRoute.query)

const organizationName = computed(() => context.value?.organization.name ?? 'СТО / Автосервис')
const displayName = computed(() => context.value?.user.displayName || context.value?.user.email || 'Профиль')
const canOpenAdminPanel = computed(() => context.value?.user.canOpenAdminPanel === true)
const profileLinks = computed<ProfileLink[]>(() => {
  const links: ProfileLink[] = []

  if (!context.value) {
    return links
  }

  if (context.value && canOpenAdminPanel.value) {
    links.push({ label: 'Админ-панель', href: withPathBase(context.value.links.adminUrl ?? '/admin-panel/index.html') })
  }

  links.push(
    { label: 'Организации', href: withPathBase('/organizations') },
    { label: 'Профиль', href: withPathBase('/personal') },
    { label: 'Личная техника', href: withPathBase('/vehicles') },
    { label: '', href: '#', kind: 'divider' },
    { label: 'Выйти', href: `${withPathBase('/auth/logout')}?returnUrl=${encodeURIComponent(withPathBase('/'))}`, kind: 'logout' }
  )

  return links
})
const sections = computed(() => buildAutoServiceSections(context.value?.shell.menu))
const currentSection = computed(() =>
  sections.value.find(section => section.code === resolveBreadcrumbSectionCode(activeSection.value)) ?? sections.value[0]
)
const breadcrumbs = computed<WorkspaceBreadcrumbItem[]>(() => {
  const items: WorkspaceBreadcrumbItem[] = [
    { title: organizationName.value, href: `#${sections.value[0]?.code ?? 'workspace'}` }
  ]
  const sectionTitle = activeSection.value === 'assistant' ? 'ИИ - Голосовой помощник' : currentSection.value?.title ?? 'Раздел'
  if (activeSection.value === 'members' && sections.value.some(section => section.code === 'employees')) {
    items.push({ title: 'Команда', href: '#employees' })
  }

  if (activeSection.value === 'workspace') {
    items.push({ title: sectionTitle })
    return items
  }

  if (activeEntityId.value) {
    items.push({ title: sectionTitle, href: resolveBreadcrumbSectionHref(activeSection.value) })
    items.push({ title: resolveEntityBreadcrumbTitle() })
    return items
  }

  items.push({ title: sectionTitle })
  return items
})

function resolveRouteFromHash(): { section: AutoServiceSectionCode, entityId: string, action: string, childId: string, query: URLSearchParams } {
  const [path, query = ''] = window.location.hash.replace(/^#\/?/, '').split('?')
  const [code, entityId = '', action = '', childId = ''] = path.split('/')
  return isAutoServiceSectionCode(code)
    ? { section: code, entityId, action, childId, query: new URLSearchParams(query) }
    : { section: 'workspace', entityId: '', action: '', childId: '', query: new URLSearchParams() }
}

function showBookingSuccess(id: string, message?: string) {
  showRouteNotice({ section: 'maintenance', entityId: id }, message)
}

function showMaintenanceSaveSuccess(id: string, message?: string) {
  showRouteNotice({ section: 'maintenance', entityId: id }, message)
}

function resolveBreadcrumbSectionCode(code: AutoServiceSectionCode): AutoServiceSectionCode {
  return code === 'maintenance' ? 'scheduler' : code
}

function resolveBreadcrumbSectionHref(code: AutoServiceSectionCode) {
  return code === 'maintenance' ? '#scheduler' : `#${code}`
}

function selectSection(code: string) {
  if (!isAutoServiceSectionCode(code)) return
  activeSection.value = code
  activeEntityId.value = ''
  activeAction.value = ''
  activeChildId.value = ''
  activeQuery.value = new URLSearchParams()
  clearRouteNoticeUnlessTarget({ section: code })
  pushHashState(`#${code}`)
}

function handleHashChange() {
  const route = resolveRouteFromHash()
  activeSection.value = route.section
  activeEntityId.value = route.entityId
  activeAction.value = route.action
  activeChildId.value = route.childId
  activeQuery.value = route.query
  clearRouteNoticeUnlessTarget(route)
  if (context.value) ensureVisibleSection()
}

function ensureVisibleSection() {
  const homeSection = sections.value[0]?.code ?? 'workspace'
  const canViewSection = activeSection.value === 'assistant' || sections.value.some(section => section.code === activeSection.value)
    || (activeSection.value === 'maintenance' && sections.value.some(section => section.code === 'scheduler' || section.code === 'vehicles'))
  if (!canViewSection) {
    activeSection.value = homeSection
    activeEntityId.value = ''
    activeAction.value = ''
    activeChildId.value = ''
    activeQuery.value = new URLSearchParams()
    window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}#${homeSection}`)
  }

  if (!window.location.hash.replace(/^#\/?/, '') && activeSection.value !== homeSection) {
    activeSection.value = homeSection
  }
}

async function refreshWorkspaceContext() {
  try {
    context.value = await loadAutoServiceContext(host.contextUrl)
  } catch (error) {
    console.error('Не удалось обновить рабочий стол автосервиса.', error)
  }
}

function resolveEntityBreadcrumbTitle() {
  if (activeSection.value === 'finance' && activeEntityId.value === 'export') {
    return 'Выгрузка для бухгалтерии'
  }

  if (activeEntityId.value === 'new') {
    return activeSection.value === 'maintenance'
      ? 'Новый заказ-наряд'
      : 'Создание'
  }

  if (activeAction.value === 'edit') {
    return 'Редактирование'
  }

  if (activeAction.value === 'photos') {
    return 'Фото'
  }

  if (activeAction.value === 'albums') {
    return activeChildId.value ? 'Альбом' : 'Альбомы'
  }

  return 'Карточка'
}

onMounted(async () => {
  window.addEventListener('hashchange', handleHashChange)

  try {
    context.value = await loadAutoServiceContext(host.contextUrl)
    ensureVisibleSection()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось открыть клиент автосервиса.'
  } finally {
    isLoading.value = false
  }
})

onUnmounted(() => {
  window.removeEventListener('hashchange', handleHashChange)
})
</script>

<template>
  <WorkspaceCrmShell
    :context="context"
    :sections="sections"
    :active-section="resolveBreadcrumbSectionCode(activeSection)"
    :breadcrumbs="breadcrumbs"
    :profile-links="profileLinks"
    :display-name="displayName"
    @select="selectSection"
  >
    <WorkspaceTariffExpiryNotice v-if="context" :context="context" />
    <div v-if="bookingSuccessMessage" class="booking-save-notice" role="status" aria-live="polite">
      <span>{{ bookingSuccessMessage }}</span>
      <button type="button" aria-label="Закрыть сообщение" @click="bookingSuccessMessage = ''">×</button>
    </div>

    <section v-if="isLoading" class="autoservice-layout--loading" aria-busy="true">
      <div class="autoservice-main">
        <section class="autoservice-loading-card">
          <UiLoader text="Открываем рабочий стол..." />
          <div class="autoservice-loading-grid">
            <span></span>
            <span></span>
            <span></span>
          </div>
          <div class="autoservice-loading-lines">
            <span></span>
            <span></span>
            <span></span>
          </div>
        </section>
      </div>
    </section>

    <section v-else-if="errorMessage" class="autoservice-state autoservice-state-error">
      {{ errorMessage }}
    </section>

    <section v-else-if="context && sections.length" class="crm-page">
      <div class="autoservice-main">
        <WorkspaceAssistantSection v-if="activeSection === 'assistant'" :context="context" @changed="refreshWorkspaceContext" />
        <OverviewSection v-else-if="activeSection === 'workspace'" :context="context" />
        <VisualizationSection v-else-if="activeSection === 'visualization'" :context="context" />
        <SchedulerSection v-else-if="activeSection === 'scheduler'" :context="context" @changed="refreshWorkspaceContext" />
        <MaterialsSection v-else-if="activeSection === 'materials'" :context="context" />
        <WorkJournalSection v-else-if="activeSection === 'work-journal'" :context="context" />
        <FinanceSection
          v-else-if="activeSection === 'finance'"
          :context="context"
          :initial-date-from="activeQuery.get('dateFrom') ?? undefined"
          :initial-date-to="activeQuery.get('dateTo') ?? undefined"
          :initial-export-open="activeEntityId === 'export'"
        />
        <ClientCardSection
          v-else-if="activeSection === 'clients' && activeEntityId"
          :context="context"
          :client-id="activeEntityId"
          @changed="refreshWorkspaceContext"
        />
        <MessagesSection v-else-if="activeSection === 'messages'" :context="context" :conversation-id="activeEntityId || undefined" />
        <InquiriesSection v-else-if="activeSection === 'inquiries'" :context="context" :inquiry-id="activeEntityId || undefined" @changed="refreshWorkspaceContext" />
        <ClientsSection v-else-if="activeSection === 'clients'" :context="context" />
        <VehicleFormSection
          v-else-if="activeSection === 'vehicles' && activeEntityId === 'new'"
          :context="context"
          :initial-client-id="activeAction"
        />
        <VehicleFormSection
          v-else-if="activeSection === 'vehicles' && activeEntityId && activeAction === 'edit'"
          :context="context"
          :vehicle-id="activeEntityId"
        />
        <VehicleMediaSection
          v-else-if="activeSection === 'vehicles' && activeEntityId && activeAction === 'photos'"
          :context="context"
          :vehicle-id="activeEntityId"
          mode="photos"
        />
        <VehicleMediaSection
          v-else-if="activeSection === 'vehicles' && activeEntityId && activeAction === 'albums' && activeChildId"
          :context="context"
          :vehicle-id="activeEntityId"
          :album-id="activeChildId"
          mode="album"
        />
        <VehicleMediaSection
          v-else-if="activeSection === 'vehicles' && activeEntityId && activeAction === 'albums'"
          :context="context"
          :vehicle-id="activeEntityId"
          mode="albums"
        />
        <VehicleCardSection
          v-else-if="activeSection === 'vehicles' && activeEntityId"
          :context="context"
          :vehicle-id="activeEntityId"
          :action="activeAction"
          @changed="refreshWorkspaceContext"
        />
        <VehiclesSection v-else-if="activeSection === 'vehicles'" :context="context" />
        <QuickBookingSection
          v-else-if="activeSection === 'maintenance' && (!activeEntityId || activeEntityId === 'new')"
          :context="context"
          :initial-vehicle-id="activeAction || undefined"
          :inquiry-id="activeQuery.get('inquiry') ?? undefined"
          :repeat-visit-id="activeQuery.get('repeatVisit') ?? undefined"
          :initial-date="activeQuery.get('date') ?? undefined"
          :initial-start-time="activeQuery.get('start') ?? undefined"
          :initial-end-time="activeQuery.get('end') ?? undefined"
          :initial-location="activeQuery.get('location') ?? undefined"
          @changed="refreshWorkspaceContext"
          @saved="showBookingSuccess"
        />
        <MaintenanceFormSection
          v-else-if="activeSection === 'maintenance' && activeEntityId && activeAction === 'edit'"
          :context="context"
          :maintenance-id="activeEntityId"
          :from-vehicle="activeQuery.get('from') === 'vehicle'"
          @changed="refreshWorkspaceContext"
          @saved="showMaintenanceSaveSuccess"
        />
        <MaintenanceCardSection
          v-else-if="activeSection === 'maintenance' && activeEntityId"
          :context="context"
          :maintenance-id="activeEntityId"
          :from-vehicle="activeQuery.get('from') === 'vehicle'"
          @changed="refreshWorkspaceContext"
        />
        <DefectsSection v-else-if="activeSection === 'defects'" :context="context" />
        <CountersSection v-else-if="activeSection === 'counters'" :context="context" />
        <WorkspaceCommonSection
          v-else-if="isWorkspaceCommonSectionCode(activeSection)"
          :context="context"
          :section-code="activeSection"
          @changed="refreshWorkspaceContext"
        />
        <OverviewSection v-else :context="context" />
      </div>
    </section>
    <section v-else class="crm-page">Нет доступных разделов организации.</section>
  </WorkspaceCrmShell>
</template>

<style scoped>
.booking-save-notice {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  margin: 0 0 12px;
  padding: 10px 14px;
  border: 1px solid color-mix(in srgb, #247564 35%, var(--crm-line, #d0d5dd));
  border-radius: 8px;
  background: color-mix(in srgb, #247564 9%, var(--crm-surface, #fff));
  color: var(--crm-text, #243044);
  font-size: 13px;
}

.booking-save-notice button {
  border: 0;
  padding: 0;
  background: transparent;
  color: inherit;
  cursor: pointer;
  font: inherit;
}
</style>
