<script setup lang="ts">
// HIDDEN_FLEET: сохранено для прежних организаций и согласованного возврата. См. docs/hidden-fleet-scenario.md.
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref } from 'vue'
import { UiLoader, type ProfileLink } from '@opencrm/ui'
import {
  WorkspaceCrmShell,
  FinanceSection,
  isWorkspaceCommonSectionCode,
  pushHashState,
  type WorkspaceBreadcrumbItem
} from '@opencrm/workspace'
import { loadFleetContext, resolveHostConfig, type FleetWorkspaceContext } from '@/api/fleetBff'
import MaterialsSection from '@/components/MaterialsSection.vue'
import { buildFleetSections, isFleetSectionCode, type FleetSectionCode } from '@/sections'
import WorkspaceTariffExpiryNotice from '@opencrm/workspace/components/WorkspaceTariffExpiryNotice'

const ClientCardSection = defineAsyncComponent(() => import('@opencrm/workspace/components/ClientCardSection'))
const ClientsSection = defineAsyncComponent(() => import('@opencrm/workspace/components/ClientsSection'))
const MaintenanceCardSection = defineAsyncComponent(() => import('@opencrm/workspace/components/MaintenanceCardSection'))
const MaintenanceFormSection = defineAsyncComponent(() => import('@opencrm/workspace/components/MaintenanceFormSection'))
const VehicleCardSection = defineAsyncComponent(() => import('@opencrm/workspace/components/VehicleCardSection'))
const VehicleFormSection = defineAsyncComponent(() => import('@opencrm/workspace/components/VehicleFormSection'))
const VehicleMediaSection = defineAsyncComponent(() => import('@opencrm/workspace/components/VehicleMediaSection'))
const VehiclesSection = defineAsyncComponent(() => import('@opencrm/workspace/components/VehiclesSection'))
const WorkspaceCommonSection = defineAsyncComponent(() => import('@opencrm/workspace/components/WorkspaceCommonSection'))
const OverviewSection = defineAsyncComponent(() => import('@/components/OverviewSection.vue'))
const VisualizationSection = defineAsyncComponent(() => import('@opencrm/workspace/components/WorkspaceVisualizationSection'))
const SchedulerSection = defineAsyncComponent(() => import('@/components/SchedulerSection.vue'))
const WorkJournalSection = defineAsyncComponent(() => import('@/components/WorkJournalSection.vue'))
const DefectsSection = defineAsyncComponent(() => import('@/components/DefectsSection.vue'))
const CountersSection = defineAsyncComponent(() => import('@/components/CountersSection.vue'))

const host = resolveHostConfig()
const context = ref<FleetWorkspaceContext | null>(null)
const isLoading = ref(true)
const errorMessage = ref('')
const initialRoute = resolveRouteFromHash()
const activeSection = ref<FleetSectionCode>(initialRoute.section)
const activeEntityId = ref(initialRoute.entityId)
const activeAction = ref(initialRoute.action)
const activeChildId = ref(initialRoute.childId)
const activeQuery = ref(initialRoute.query)

const organizationName = computed(() => context.value?.organization.name ?? 'Автопарк')
const displayName = computed(() => context.value?.user.displayName || context.value?.user.email || 'Профиль')
const canOpenAdminPanel = computed(() => context.value?.user.canOpenAdminPanel === true)
const profileLinks = computed<ProfileLink[]>(() => {
  const links: ProfileLink[] = []

  if (context.value && canOpenAdminPanel.value) {
    links.push({ label: 'Админ-панель', href: context.value.links.adminUrl ?? '/admin-panel/index.html' })
  }

  links.push(
    { label: 'Организации', href: '/organizations' },
    { label: 'Профиль', href: '/personal' },
    { label: 'Личная техника', href: '/vehicles' },
    { label: '', href: '#', kind: 'divider' },
    { label: 'Выйти', href: '/auth/logout?returnUrl=/', kind: 'logout' }
  )

  return links
})
const sections = computed(() => buildFleetSections(context.value?.shell.menu))
const currentSection = computed(() =>
  sections.value.find(section => section.code === resolveBreadcrumbSectionCode(activeSection.value)) ?? sections.value[0]
)
const breadcrumbs = computed<WorkspaceBreadcrumbItem[]>(() => {
  const items: WorkspaceBreadcrumbItem[] = [
    { title: organizationName.value, href: `#${sections.value[0]?.code ?? 'workspace'}` }
  ]
  const sectionTitle = currentSection.value?.title ?? 'Раздел'
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

function resolveRouteFromHash(): { section: FleetSectionCode, entityId: string, action: string, childId: string, query: URLSearchParams } {
  const [path, query = ''] = window.location.hash.replace(/^#\/?/, '').split('?')
  const [code, entityId = '', action = '', childId = ''] = path.split('/')
  if (code === 'maintenance' && !entityId) {
    return { section: 'scheduler', entityId: '', action: '', childId: '', query: new URLSearchParams(query) }
  }

  return isFleetSectionCode(code)
    ? { section: code, entityId, action, childId, query: new URLSearchParams(query) }
    : { section: 'workspace', entityId: '', action: '', childId: '', query: new URLSearchParams() }
}

function resolveBreadcrumbSectionCode(code: FleetSectionCode): FleetSectionCode {
  return code === 'maintenance' ? 'scheduler' : code
}

function resolveBreadcrumbSectionHref(code: FleetSectionCode) {
  return code === 'maintenance' ? '#scheduler' : `#${code}`
}

function selectSection(code: string) {
  if (!isFleetSectionCode(code)) return
  activeSection.value = code
  activeEntityId.value = ''
  activeAction.value = ''
  activeChildId.value = ''
  activeQuery.value = new URLSearchParams()
  pushHashState(`#${code}`)
}

function handleHashChange() {
  const route = resolveRouteFromHash()
  activeSection.value = route.section
  activeEntityId.value = route.entityId
  activeAction.value = route.action
  activeChildId.value = route.childId
  activeQuery.value = route.query
  if (context.value) ensureVisibleSection()
}

function ensureVisibleSection() {
  const homeSection = sections.value[0]?.code ?? 'workspace'
  const canViewSection = sections.value.some(section => section.code === activeSection.value)
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
    context.value = await loadFleetContext(host.contextUrl)
  } catch (error) {
    console.error('Не удалось обновить рабочий стол автопарка.', error)
  }
}

function resolveEntityBreadcrumbTitle() {
  if (activeSection.value === 'finance' && activeEntityId.value === 'export') {
    return 'Выгрузка для бухгалтерии'
  }

  if (activeEntityId.value === 'new') {
    return activeSection.value === 'maintenance'
      ? 'Новая запись'
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
    context.value = await loadFleetContext(host.contextUrl)
    ensureVisibleSection()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось открыть клиент автопарка.'
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

    <section v-if="isLoading" class="fleet-layout--loading" aria-busy="true">
      <div class="fleet-main">
        <section class="fleet-loading-card">
          <UiLoader text="Открываем рабочий стол..." />
          <div class="fleet-loading-grid">
            <span></span>
            <span></span>
            <span></span>
          </div>
          <div class="fleet-loading-lines">
            <span></span>
            <span></span>
            <span></span>
          </div>
        </section>
      </div>
    </section>

    <section v-else-if="errorMessage" class="fleet-state fleet-state-error">
      {{ errorMessage }}
    </section>

    <section v-else-if="context && sections.length" class="crm-page">
      <div class="fleet-main">
        <OverviewSection v-if="activeSection === 'workspace'" :context="context" />
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
        <DefectsSection v-else-if="activeSection === 'defects'" :context="context" />
        <CountersSection v-else-if="activeSection === 'counters'" :context="context" />
        <ClientCardSection
          v-else-if="activeSection === 'clients' && activeEntityId"
          :context="context"
          :client-id="activeEntityId"
          @changed="refreshWorkspaceContext"
        />
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
        <MaintenanceFormSection
          v-else-if="activeSection === 'maintenance' && activeEntityId === 'new'"
          :context="context"
          :initial-vehicle-id="activeAction || undefined"
          :initial-date="activeQuery.get('date') ?? undefined"
          :initial-start-time="activeQuery.get('start') ?? undefined"
          :initial-end-time="activeQuery.get('end') ?? undefined"
          :initial-location="activeQuery.get('location') ?? undefined"
          :from-vehicle="activeQuery.get('from') === 'vehicle'"
          @changed="refreshWorkspaceContext"
        />
        <MaintenanceFormSection
          v-else-if="activeSection === 'maintenance' && activeEntityId && activeAction === 'edit'"
          :context="context"
          :maintenance-id="activeEntityId"
          :from-vehicle="activeQuery.get('from') === 'vehicle'"
          @changed="refreshWorkspaceContext"
        />
        <MaintenanceCardSection
          v-else-if="activeSection === 'maintenance' && activeEntityId"
          :context="context"
          :maintenance-id="activeEntityId"
          :from-vehicle="activeQuery.get('from') === 'vehicle'"
          @changed="refreshWorkspaceContext"
        />
        <VehiclesSection v-else-if="activeSection === 'vehicles'" :context="context" />
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
