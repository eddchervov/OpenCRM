<script setup lang="ts">
import { computed, defineAsyncComponent, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiCard, UiLoader, formatDateOnly, parseDateOnly } from '@opencrm/ui'
import WorkspaceAccessNotice from './WorkspaceAccessNotice.vue'
import type { WorkspaceContext } from '../types'
import { loadWorkspaceWorkday, type WorkspaceWorkday, type WorkspaceWorkdayItem } from '../api/workday'
import WorkspaceWorkdayCard from './WorkspaceWorkdayCard.vue'
import WorkdayInquiryActions from './WorkdayInquiryActions.vue'
import WorkdayRepeatVisitActions from './WorkdayRepeatVisitActions.vue'
import WorkspaceSetupWizard from './WorkspaceSetupWizard.vue'

const WorkspaceWorkdayPreview = defineAsyncComponent(() => import('./WorkspaceWorkdayPreview.vue'))

type WorkdayStage = 'planned' | 'inProgress' | 'ready' | 'handedOver'
type WorkdayView = 'stages' | 'posts'
type WorkdayBoardEntry = { item: WorkspaceWorkdayItem, stage: WorkdayStage }
type WorkdayBoardGroup = {
  code: string
  title: string
  subtitle: string
  empty: string
  totalCount: number
  items: WorkdayBoardEntry[]
}

const props = defineProps<{ context: WorkspaceContext, showSetup?: boolean }>()
const today = ref(formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'))
const selectedDate = ref(today.value)
const data = ref<WorkspaceWorkday | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const updatedAt = ref('')
const storedView = window.localStorage.getItem('opencrm-workday-view')
const viewMode = ref<WorkdayView>(storedView === 'posts' ? 'posts' : 'stages')
const mobileGroup = ref('planned')
const expandedGroups = ref<string[]>([])
const previewId = ref('')
let request: AbortController | null = null
let refreshTimer: ReturnType<typeof setInterval> | undefined

const isReadOnly = computed(() => props.context.workspaceAccess?.isReadOnly !== false)
const hasInquiryActions = computed(() => props.context.shell.menu?.some(item => item.code === 'inquiries' && item.isVisible))
const hasRepeatVisitActions = computed(() => ['detailing', 'service-station', 'autoservice'].includes(props.context.organization.primaryScenarioCode || props.context.shell.scenario?.code || '')
  && props.context.shell.menu?.some(item => item.code === 'clients' && item.isVisible)
  && props.context.shell.menu?.some(item => item.code === 'vehicles' && item.isVisible)
  && props.context.shell.menu?.some(item => item.isVisible && (item.code === 'maintenance' || item.code === 'scheduler' || item.sectionCodes?.includes('maintenance'))))
const readOnlyMessage = computed(() => props.context.workspaceAccess?.readOnlyMessage ?? '')
const canCreate = computed(() => !isReadOnly.value && (props.context.shell.quickActions ?? [])
  .some(action => action.isVisible && (action.code === 'create-maintenance' || action.formCode === 'maintenance-create')))
const canOpenRecords = computed(() => (props.context.shell.menu ?? []).some(item => item.isVisible
  && (item.code === 'scheduler' || item.code === 'maintenance' || item.code === 'vehicles')))
const previewItem = computed(() => stageGroups.value.flatMap(group => group.items).find(entry => entry.item.id === previewId.value)?.item)
const bookingHref = computed(() => '#maintenance/new?date=' + selectedDate.value)
const dateHeading = computed(() => {
  const label = parseDateOnly(selectedDate.value)?.toLocaleDateString('ru-RU', {
    weekday: 'long', day: 'numeric', month: 'long'
  }) ?? ''

  return label.charAt(0).toUpperCase() + label.slice(1)
})
const stageGroups = computed<WorkdayBoardGroup[]>(() => data.value ? [
  {
    code: 'planned',
    title: 'Запланировано',
    subtitle: 'Сегодня и просроченные',
    empty: 'Нет запланированных работ',
    totalCount: data.value.planned.totalCount,
    items: data.value.planned.items.map(item => ({ item, stage: 'planned' }))
  },
  {
    code: 'inProgress',
    title: 'В работе',
    subtitle: 'Техника обслуживается сейчас',
    empty: 'Сейчас нет работ в процессе',
    totalCount: data.value.inProgress.totalCount,
    items: data.value.inProgress.items.map(item => ({ item, stage: 'inProgress' }))
  },
  {
    code: 'ready',
    title: 'Готово',
    subtitle: 'Ожидает передачи',
    empty: 'Нет готовой к выдаче техники',
    totalCount: data.value.ready.totalCount,
    items: data.value.ready.items.map(item => ({ item, stage: 'ready' }))
  },
  {
    code: 'handedOver',
    title: 'Выдано',
    subtitle: 'За выбранный день',
    empty: 'В этот день выдач не было',
    totalCount: data.value.handedOver.totalCount,
    items: data.value.handedOver.items.map(item => ({ item, stage: 'handedOver' }))
  }
] : [])
const postGroups = computed<WorkdayBoardGroup[]>(() => {
  const entries = stageGroups.value.flatMap(group => group.items)
  const grouped = new Map<string, WorkdayBoardEntry[]>()

  for (const entry of entries) {
    const post = entry.item.location?.trim() || 'Без поста'
    const postEntries = grouped.get(post) ?? []
    postEntries.push(entry)
    grouped.set(post, postEntries)
  }

  return Array.from(grouped.entries())
    .sort(([left], [right]) => {
      if (left === 'Без поста') return 1
      if (right === 'Без поста') return -1

      return left.localeCompare(right, 'ru')
    })
    .map(([post, items], index) => {
      const sortedItems = [...items].sort((left, right) => {
        const leftTime = left.item.date.slice(0, 10) + ' ' + (left.item.startTime ?? '99:99')
        const rightTime = right.item.date.slice(0, 10) + ' ' + (right.item.startTime ?? '99:99')

        return leftTime.localeCompare(rightTime)
      })

      return {
        code: 'post-' + index,
        title: post,
        subtitle: items.length === 1 ? '1 работа' : items.length < 5 ? items.length + ' работы' : items.length + ' работ',
        empty: 'На этом посту нет работ',
        totalCount: items.length,
        items: sortedItems
      }
    })
})
const groups = computed(() => viewMode.value === 'posts' ? postGroups.value : stageGroups.value)

function moveDay(offset: number) {
  const date = parseDateOnly(selectedDate.value)!
  date.setDate(date.getDate() + offset)
  selectedDate.value = formatDateOnly(date)
}

async function loadDay() {
  today.value = formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
  request?.abort()
  const activeRequest = new AbortController()
  request = activeRequest
  isLoading.value = true
  errorMessage.value = ''
  const timeout = window.setTimeout(() => activeRequest.abort('timeout'), 20000)

  try {
    const result = await loadWorkspaceWorkday(props.context.organization.id, selectedDate.value, activeRequest.signal)
    if (request !== activeRequest) return
    data.value = result
    updatedAt.value = new Intl.DateTimeFormat('ru-RU', {
      hour: '2-digit', minute: '2-digit', timeZone: props.context.organization.timeZoneId || 'UTC'
    }).format(new Date())
  } catch (error) {
    if (request !== activeRequest || (activeRequest.signal.aborted && activeRequest.signal.reason !== 'timeout')) return
    errorMessage.value = activeRequest.signal.reason === 'timeout'
      ? 'Сервер долго не отвечает. Попробуйте обновить рабочий день.'
      : error instanceof Error ? error.message : 'Не удалось загрузить рабочий день.'
  } finally {
    window.clearTimeout(timeout)
    if (request === activeRequest) isLoading.value = false
  }
}

watch(viewMode, value => {
  window.localStorage.setItem('opencrm-workday-view', value)
  mobileGroup.value = groups.value[0]?.code ?? ''
  expandedGroups.value = []
})

watch(groups, value => {
  if (!value.some(group => group.code === mobileGroup.value)) {
    mobileGroup.value = value[0]?.code ?? ''
  }
})

watch([() => props.context.organization.id, () => props.context.organization.timeZoneId, selectedDate], () => {
  previewId.value = ''
  today.value = formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
  data.value = null
  updatedAt.value = ''
  expandedGroups.value = []
  void loadDay()
}, { immediate: true })
refreshTimer = setInterval(() => {
  if (!isLoading.value && document.visibilityState === 'visible') void loadDay()
}, 15000)
onBeforeUnmount(() => {
  request?.abort()
  request = null
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>

<template>
  <UiCard page title="Рабочий день">
    <template #actions>
      <UiButton v-if="canCreate" :href="bookingHref" class="workday-create"><span aria-hidden="true">＋</span> Новая запись</UiButton>
    </template>
    <div class="workday">
      <WorkspaceAccessNotice v-if="readOnlyMessage" :message="readOnlyMessage" :can-manage-billing="context.organization.canManageBilling === true" />
      <WorkspaceSetupWizard v-if="showSetup && context.organization.canManageSettings && !isReadOnly" :key="context.organization.id" :organization-id="context.organization.id" :scenario-code="context.shell.scenario?.code" :can-write="!isReadOnly" :can-create="canCreate" />
      <div class="workday-toolbar">
        <div class="workday-date">
          <button type="button" class="workday-date__nav" aria-label="Предыдущий день" title="Предыдущий день" @click="moveDay(-1)">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 18-6-6 6-6"/></svg>
          </button>
          <div class="workday-date__text">
            <h3>{{ dateHeading }}</h3>
            <span v-if="selectedDate === today" class="workday-date__caption">Сегодня</span>
            <button v-else type="button" class="workday-today" @click="selectedDate = today">Вернуться к сегодня</button>
          </div>
          <button type="button" class="workday-date__nav" aria-label="Следующий день" title="Следующий день" @click="moveDay(1)">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 6 6 6-6 6"/></svg>
          </button>
        </div>
        <div class="workday-view" aria-label="Представление рабочего дня">
          <button type="button" :aria-pressed="viewMode === 'stages'" @click="viewMode = 'stages'">Этапы</button>
          <button type="button" :aria-pressed="viewMode === 'posts'" @click="viewMode = 'posts'">Посты</button>
        </div>
        <div class="workday-toolbar__links">
          <span v-if="updatedAt" class="workday-updated">Обновлено {{ updatedAt }}</span>
          <button class="workday-refresh" type="button" aria-label="Обновить рабочий день" title="Обновить" :disabled="isLoading" @click="loadDay">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 7v5h-5M4 17v-5h5M5.5 7a7.5 7.5 0 0 1 13-1L20 9M4 15l1.5 3a7.5 7.5 0 0 0 13-1"/></svg>
          </button>
        </div>
      </div>
      <div v-if="hasInquiryActions || hasRepeatVisitActions" class="workday-client-actions" role="group" aria-label="Связаться с клиентами">
        <strong>Связаться с клиентами</strong>
        <WorkdayInquiryActions v-if="hasInquiryActions" :organization-id="context.organization.id" :user-id="context.user.id" :date="selectedDate" />
        <WorkdayRepeatVisitActions v-if="hasRepeatVisitActions"
          :organization-id="context.organization.id" :user-id="context.user.id" :date="selectedDate" />
      </div>
      <div v-if="errorMessage" class="workday-error" role="alert">
        <div><strong>Рабочий день не обновлён</strong><p>{{ errorMessage }}</p></div>
        <UiButton variant="secondary" :disabled="isLoading" @click="loadDay">Повторить</UiButton>
      </div>
      <div v-if="isLoading && !data" class="workday-loading">
        <UiLoader text="Загружаем записи" />
      </div>
      <template v-else-if="data">
        <div v-if="!data.hasAnyWork" class="workday-welcome">
          <div class="workday-welcome__illustration" aria-hidden="true">
            <svg viewBox="0 0 200 120"><rect x="18" y="16" width="164" height="94" rx="14"/><path d="M18 42h164M51 8v18M149 8v18"/><path class="workday-car" d="m55 77 9-20h61l14 20 8 5v15H47V82zM55 77h84M67 88h1M127 88h1"/><path d="M62 100v6M132 100v6"/></svg>
          </div>
          <span class="workday-eyebrow">ВАШ РАБОЧИЙ ДЕНЬ НАЧИНАЕТСЯ ЗДЕСЬ</span>
          <h3 v-if="canCreate">Первый автомобиль.<br>Первая запись.</h3><h3 v-else>Рабочий день организации</h3>
          <p v-if="canCreate">Создайте первую запись — она появится на доске и будет двигаться по этапам работы.</p>
          <UiButton v-if="canCreate" :href="bookingHref" class="workday-create">Записать первого клиента <span aria-hidden="true">↗</span></UiButton>
          <p v-else class="workday-welcome__muted">В организации пока нет записей.</p>
        </div>
        <template v-else>
          <div class="workday-mobile-tabs" aria-label="Группы работ">
            <button v-for="group in groups" :key="group.code" type="button" :aria-pressed="mobileGroup === group.code" @click="mobileGroup = group.code">{{ group.title }} <span>{{ group.totalCount }}</span></button>
          </div>
          <div v-if="viewMode === 'posts' && !groups.length" class="workday-posts-empty">
            <strong>На выбранной доске нет работ по постам</strong>
            <span>Назначьте пост в записи или выберите другой день.</span>
          </div>
          <div v-else class="workday-board" :class="'workday-board--' + viewMode" :aria-busy="isLoading">
            <section v-for="group in groups" :key="group.code" class="workday-column" :class="[`workday-column--${group.code}`, { 'workday-column--mobile-active': mobileGroup === group.code }]" :aria-label="group.title">
              <header class="workday-column__header">
                <div><h3>
                  <svg class="workday-column__icon" viewBox="0 0 24 24" aria-hidden="true">
                    <template v-if="group.code === 'planned'"><circle cx="12" cy="12" r="9"/><path d="M12 6v6l4 2"/></template>
                    <template v-else-if="group.code === 'inProgress'"><path d="m10 3-.7 3-2 .9-2.7-.9-2 3.5 2.2 2.1v2.3L2.6 16l2 3.5 2.7-.8 2 .9.7 3h4l.7-3 2-.9 2.7.8 2-3.5-2.2-2.1v-2.3l2.2-2.1-2-3.5-2.7.9-2-.9-.7-3z"/><circle cx="12" cy="13" r="3"/></template>
                    <template v-else-if="group.code === 'ready'"><circle cx="12" cy="12" r="9"/><path d="m7 12 3 3 7-7"/></template>
                    <template v-else-if="group.code === 'handedOver'"><path d="m5 10 2-6h10l2 6M4 10h16v9H4zM7 19v2m10-2v2M7 14h1m8 0h1"/></template>
                    <template v-else><path d="M3 21V4h18v17M7 21V9h10v12M7 13h10M7 17h10"/></template>
                  </svg>
                  {{ group.title }} <span>{{ group.totalCount }}</span>
                </h3><p>{{ group.subtitle }}</p></div>
                <a v-if="group.code === 'planned' && canCreate" :href="bookingHref" class="workday-add" aria-label="Добавить запись">＋</a>
              </header>
              <div class="workday-column__body">
                <WorkspaceWorkdayCard v-for="entry in (expandedGroups.includes(group.code) ? group.items : group.items.slice(0, 5))" :key="entry.item.id" :item="entry.item" :stage="entry.stage" :today="today" :time-zone-id="context.organization.timeZoneId || 'UTC'" :can-open="canOpenRecords" @open="previewId = $event" />
                <div v-if="!group.items.length" class="workday-column__empty"><span aria-hidden="true">{{ group.code === 'inProgress' ? '✓' : '—' }}</span><p>{{ group.empty }}</p></div>
                <button v-if="group.items.length > 5 && !expandedGroups.includes(group.code)" type="button" class="workday-more" @click="expandedGroups.push(group.code)">Ещё {{ group.items.length - 5 }} <span aria-hidden="true">↓</span></button>
                <p v-if="group.totalCount > group.items.length" class="workday-limit">Показаны первые {{ group.items.length }} из {{ group.totalCount }}. Остальные — в разделе записей.</p>
              </div>
            </section>
          </div>
        </template>
      </template>
    </div>
  </UiCard>
  <WorkspaceWorkdayPreview v-if="previewItem && canOpenRecords" :key="context.organization.id + ':' + previewItem.id" :context="context" :item="previewItem" @close="previewId = ''" />
</template>

<style scoped>
.workday-client-actions { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; }
.workday-client-actions > strong { margin-right: 4px; font-size: 13px; color: var(--crm-text, #263142); }

.workday {
  display: grid;
  gap: 16px;
}

.workday-toolbar {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: center;
  gap: 18px;
  padding: 11px 12px;
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 10px;
  background: var(--crm-surface-soft, #f5f6f8);
}

.workday-date {
  display: grid;
  grid-template-columns: 32px minmax(0, max-content) 32px;
  align-items: start;
  gap: 10px;
  min-width: 0;
}

.workday-date__text {
  min-width: 0;
}

.workday-date h3 {
  display: flex;
  align-items: center;
  min-height: 32px;
  margin: 0;
  color: var(--crm-text, #202733);
  font-size: 16px;
  line-height: 1.3;
}

.workday-date__caption,
.workday-today {
  display: block;
  margin-top: 2px;
  color: var(--crm-text-muted, #667085);
  font-size: 11px;
  line-height: 1.3;
}

.workday-today {
  border: 0;
  padding: 0;
  background: none;
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 3px;
}

.workday-date__nav,
.workday-refresh {
  width: 32px;
  height: 32px;
  display: grid;
  flex: 0 0 32px;
  place-items: center;
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 8px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #344054);
  cursor: pointer;
}

.workday-date__nav svg,
.workday-refresh svg {
  width: 15px;
  height: 15px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.8;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.workday-date__nav:hover,
.workday-refresh:hover {
  border-color: var(--crm-line-strong, #cfd5dd);
  background: var(--crm-surface, #fff);
  color: var(--opencrm-accent);
}

.workday-view {
  display: flex;
  justify-self: center;
  padding: 4px;
  border: 1px solid var(--crm-line-strong, #cfd5dd);
  border-radius: 10px;
  background: var(--crm-surface, #fff);
}

.workday-view button {
  min-width: 86px;
  min-height: 36px;
  padding: 7px 16px;
  border: 0;
  border-radius: 7px;
  background: transparent;
  color: var(--crm-text-muted, #667085);
  cursor: pointer;
  font-size: 13px;
  font-weight: 700;
  transition: background-color .15s, color .15s, box-shadow .15s;
}

.workday-view button:hover {
  color: var(--crm-text, #202733);
  background: var(--crm-surface-soft, #f5f6f8);
}

.workday-view button[aria-pressed="true"] {
  background: var(--opencrm-accent, #dc5b1b);
  color: #fff;
  box-shadow: 0 2px 7px rgba(220, 91, 27, .22);
}

.workday-toolbar__links {
  display: flex;
  align-items: center;
  justify-self: end;
  gap: 10px;
  min-width: 0;
  font-size: 12px;
}

.workday-updated {
  color: var(--crm-text-muted, #667085);
  font-size: 11px;
  white-space: nowrap;
}

.workday-refresh {
  color: var(--crm-text-muted, #667085);
}

.workday-refresh:disabled {
  opacity: .45;
  cursor: wait;
}
:global(html[data-crm-theme]) .workday-create.ui-button {
  background: #dc5b1b;
  border-color: #dc5b1b;
  color: #fff;
  gap: 10px;
}

:global(html[data-crm-theme]) .workday-create.ui-button:hover {
  background: #bf4810;
  border-color: #bf4810;
}

.workday-posts-empty {
  min-height: 180px;
  display: grid;
  place-content: center;
  gap: 7px;
  padding: 24px;
  border: 1px dashed var(--crm-line, #e3e6eb);
  border-radius: 10px;
  background: var(--crm-surface-soft, #f5f6f8);
  color: var(--crm-text-muted, #667085);
  text-align: center;
}

.workday-posts-empty strong {
  color: var(--crm-text, #202733);
  font-size: 13px;
}

.workday-posts-empty span {
  font-size: 12px;
}
.workday-board {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  align-items: stretch;
}

.workday-board--posts {
  grid-template-columns: repeat(auto-fit, minmax(230px, 1fr));
}

.workday-column {
  min-width: 0;
  border-radius: 10px;
  background: var(--crm-surface-soft, #f5f6f8);
  border: 1px solid var(--crm-line, #eef0f3);
}

.workday-column__header {
  min-height: 62px;
  padding: 12px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  border-radius: 9px 9px 0 0;
  background: var(--crm-section-header-bg, #f2f4f7);
  border: 1px solid var(--crm-section-header-border, #dde3eb);
}

.workday-board--stages .workday-column__header {
  background: color-mix(in srgb, var(--workday-stage-color) 14%, var(--crm-surface, #fff));
}

.workday-column--planned { --workday-stage-color: #7189ac; }
.workday-column--inProgress { --workday-stage-color: #e67337; }
.workday-column--ready { --workday-stage-color: #baa02d; }
.workday-column--handedOver { --workday-stage-color: #389b82; }

.workday-column__header h3 {
  display: flex;
  gap: 8px;
  align-items: center;
  font-size: 14px;
  font-weight: 750;
  margin: 0;
  color: var(--crm-text, #202733);
}

.workday-column__icon {
  width: 20px;
  height: 20px;
  flex: 0 0 20px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.8;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.workday-column__header h3 span {
  border-radius: 5px;
  padding: 2px 6px;
  font-size: 11px;
  background: var(--crm-surface, #fff);
  border: 1px solid var(--crm-line, #e3e6eb);
  font-variant-numeric: tabular-nums;
}

.workday-column__header p {
  margin: 5px 0 0 28px;
  font-size: 11px;
  color: var(--crm-text-muted, #667085);
}

.workday-add {
  color: var(--crm-text-muted, #667085);
  text-decoration: none;
  font-size: 22px;
  width: 30px;
  height: 30px;
  display: grid;
  place-items: center;
  border-radius: 7px;
}

.workday-add:hover {
  background: var(--crm-surface, #fff);
}

.workday-column__body {
  display: grid;
  gap: 12px;
  align-content: start;
  padding: 8px;
}

.workday-column__empty {
  min-height: 148px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 20px;
  color: var(--crm-text-muted, #667085);
}

.workday-column__empty > span {
  font-size: 26px;
  color: var(--crm-text-muted, #98a2b3);
}

.workday-column__empty p {
  font-size: 12px;
  line-height: 1.6;
  max-width: 180px;
}

.workday-more {
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 8px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #344054);
  padding: 12px;
  cursor: pointer;
  font-size: 12px;
  font-weight: 650;
}

.workday-limit {
  margin: 5px;
  font-size: 11px;
  color: var(--crm-text-muted, #667085);
  line-height: 1.5;
}

.workday-mobile-tabs {
  display: none;
}

.workday-loading {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: center;
  padding: 80px 20px;
  color: var(--crm-text-muted, #667085);
  font-size: 13px;
}

.workday-error {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px;
  border: 1px solid #d48367;
  border-radius: 10px;
  color: var(--crm-text, #963b24);
  background: var(--crm-surface, #fffaf7);
}

.workday-error strong {
  font-size: 13px;
}

.workday-error p {
  margin: 5px 0 0;
  font-size: 12px;
}

.workday-welcome {
  padding: 38px 20px 60px;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
}

.workday-welcome__illustration {
  width: 170px;
  margin-bottom: 24px;
  color: var(--crm-text-muted, #c0c7d0);
}

.workday-welcome__illustration svg {
  fill: none;
  stroke: currentColor;
  stroke-width: 2;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.workday-car {
  stroke: #e67337;
}

.workday-eyebrow {
  font-size: 10px;
  letter-spacing: 1.8px;
  font-weight: 650;
  color: var(--crm-text-muted, #667085);
}

.workday-welcome h3 {
  color: var(--crm-text, #202733);
  font-size: clamp(24px, 3vw, 34px);
  line-height: 1.25;
  margin: 15px 0 12px;
}

.workday-welcome p {
  max-width: 390px;
  font-size: 13px;
  line-height: 1.75;
  color: var(--crm-text-muted, #667085);
  margin: 0 0 24px;
}

.workday-welcome .workday-welcome__muted {
  margin: 8px 0;
}

.workday button:focus-visible, .workday a:focus-visible {
  outline: 2px solid var(--opencrm-accent);
  outline-offset: 3px;
}

@media (max-width: 1080px) and (min-width: 821px) {
  .workday-board {
    grid-template-columns: 1fr 1fr;
  }
}

@media (max-width: 820px) {
  .workday {
    gap: 18px;
  }
  .workday-toolbar {
    grid-template-columns: minmax(0, 1fr) 32px;
    gap: 12px;
    padding: 10px;
  }
  .workday-date {
    grid-column: 1 / -1;
    grid-template-columns: 32px minmax(0, 1fr) 32px;
    width: 100%;
  }
  .workday-date__text {
    text-align: center;
  }
  .workday-date h3 {
    justify-content: center;
    font-size: 16px;
    white-space: normal;
  }
  .workday-view {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    width: 100%;
    justify-self: stretch;
  }
  .workday-view button {
    width: 100%;
    min-width: 0;
  }
  .workday-toolbar__links {
    justify-self: end;
    justify-content: flex-end;
  }
  .workday-updated {
    display: none;
  }
  .workday-board {
    grid-template-columns: minmax(0, 1fr);
  }
  .workday-column:not(.workday-column--mobile-active) {
    display: none;
  }
  .workday-mobile-tabs {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    border-bottom: 1px solid var(--crm-line, #e3e6eb);
    gap: 4px;
  }
  .workday-mobile-tabs button {
    flex: 1;
    padding: 11px 3px;
    border: 0;
    border-bottom: 2px solid transparent;
    background: none;
    color: var(--crm-text-muted, #667085);
    cursor: pointer;
    font-size: 12px;
    font-weight: 650;
    white-space: nowrap;
  }
  .workday-mobile-tabs button[aria-pressed="true"] {
    color: var(--crm-text, #202733);
    border-bottom-color: var(--opencrm-accent);
  }
  .workday-mobile-tabs span {
    margin-left: 3px;
    font-size: 10px;
  }
}

@media (max-width: 420px) {
  .workday-date {
    gap: 8px;
  }
  .workday-date h3 {
    font-size: 14px;
  }
  .workday-error {
    flex-direction: column;
    align-items: flex-start;
  }
  .workday-column__body {
    padding: 0 8px 8px;
  }
}
</style>
