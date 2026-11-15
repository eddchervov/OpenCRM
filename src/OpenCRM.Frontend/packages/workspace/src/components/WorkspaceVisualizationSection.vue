<script setup lang="ts">
import { computed, defineAsyncComponent, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiCard, UiLoader, UiSelect, formatDateOnly } from '@opencrm/ui'
import type { WorkspaceContext } from '../types'
import { loadScenarioCatalog, type WorkspaceScenarioCatalogItem } from '../api/scenarioCatalog'
import { loadWorkspaceVisualization } from '../api/visualization'
import type { WorkspaceWorkday, WorkspaceWorkdayItem } from '../api/workday'
import WorkspaceWorkdayPreview from './WorkspaceWorkdayPreview.vue'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'
import { getWorkdayProgress, shouldOpenRecord } from '../utils/workdayCard'
import { buildVisualWorkPosts, isBookingOverdue, type VisualWorkEntry } from './visualization/workPosts'
import type { WorkPostSceneState } from './visualization/types'

const WorkPostsScene = defineAsyncComponent(() => import('./visualization/WorkPostsScene.vue'))
const props = defineProps<{ context: WorkspaceContext }>()
const data = ref<WorkspaceWorkday | null>(null)
const catalog = ref<WorkspaceScenarioCatalogItem[]>([])
const now = ref(new Date())
const isLoading = ref(false)
const errorMessage = ref('')
const updatedAt = ref('')
const isSceneUnavailable = ref(false)
const selectedPostId = ref('')
const selectedTray = ref<'unassigned' | 'ready' | 'waiting' | 'paused' | null>(null)
const previewId = ref('')
const canOpenMaintenance = computed(() => props.context.shell.menu?.some(item =>
  item.isVisible && (item.code === 'scheduler' || item.code === 'maintenance')) === true)
let request: AbortController | null = null

const today = computed(() => formatDateOnly(now.value, props.context.organization.timeZoneId || 'UTC'))
const time = computed(() => new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: props.context.organization.timeZoneId || 'UTC' }).format(now.value))
const model = computed(() => data.value
  ? buildVisualWorkPosts(catalog.value, data.value, today.value, time.value)
  : { posts: [], unassigned: [], ready: [] })
const waitingEntries = computed<VisualWorkEntry[]>(() => (data.value?.planned.items ?? [])
  .filter(item => isBookingOverdue(item, today.value, time.value))
  .map(item => ({ item, stage: 'planned', isOverdue: true })))
const pausedEntries = computed<VisualWorkEntry[]>(() => (data.value?.inProgress.items ?? [])
  .filter(item => item.workPausedCount > 0)
  .map(item => ({ item, stage: 'inProgress', isOverdue: false })))
const previewItem = computed(() => [
  ...(data.value?.planned.items ?? []), ...(data.value?.inProgress.items ?? []), ...(data.value?.ready.items ?? [])
].find(item => item.id === previewId.value))
const selectedPost = computed(() => model.value.posts.find(post => post.id === selectedPostId.value))
const selectedEntries = computed(() => selectedTray.value === 'ready' ? model.value.ready
  : selectedTray.value === 'waiting' ? waitingEntries.value
  : selectedTray.value === 'paused' ? pausedEntries.value
  : selectedTray.value === 'unassigned' ? model.value.unassigned : selectedPost.value?.entries ?? [])
const selectionTitle = computed(() => selectedTray.value === 'ready' ? 'Готовы к выдаче'
  : selectedTray.value === 'waiting' ? 'Ожидают начала'
  : selectedTray.value === 'paused' ? 'Работы с паузой'
  : selectedTray.value === 'unassigned' ? 'Без поста' : selectedPost.value?.name ?? 'Выберите пост')
const occupiedCount = computed(() => model.value.posts.filter(post => post.count > 0).length)
const conflictPosts = computed(() => model.value.posts.filter(post => post.state === 'conflict'))
const stateLabels: Record<WorkPostSceneState, string> = {
  free: 'Свободен', inProgress: 'В работе', overdue: 'Ожидает начала', paused: 'Есть пауза', conflict: 'Несколько заказов'
}

function selectPost(id: string | null) {
  selectedTray.value = null
  selectedPostId.value = id ?? ''
}

function openPreview(event: MouseEvent, item: WorkspaceWorkdayItem) {
  if (!shouldOpenRecord(canOpenMaintenance.value, event)) return
  event.preventDefault()
  previewId.value = item.id
}

function entrySchedule(entry: VisualWorkEntry) {
  const date = entry.item.date.slice(0, 10)
  const label = date === today.value ? 'Сегодня' : date.split('-').reverse().join('.')
  const endDate = entry.item.endDate?.slice(0, 10)
  const endLabel = endDate && endDate !== date ? endDate.split('-').reverse().join('.') + ' ' : ''

  return `${label} · ${entry.item.startTime?.slice(0, 5) || 'Без времени'}${entry.item.endTime ? ' — ' + endLabel + entry.item.endTime.slice(0, 5) : ''}`
}

function entryProgress(entry: VisualWorkEntry) {
  return getWorkdayProgress(entry.item.workTotalCount, entry.item.workCompletedCount)
}

function entryLabel(entry: VisualWorkEntry): string {
  if (entry.stage === 'ready') return 'Готов к выдаче'
  if (entry.isOverdue) return 'Работа не начата'
  if (entry.stage === 'planned') return entry.item.startTime ? 'Ожидается' : 'Время не задано'

  return entry.item.workPausedCount ? 'В работе · есть пауза' : 'В работе'
}

function postForEntry(entry: VisualWorkEntry) {
  return model.value.posts.find(post => post.entries.some(candidate => candidate.item.id === entry.item.id))
}

async function load() {
  request?.abort()
  const activeRequest = new AbortController()
  request = activeRequest
  isLoading.value = true
  now.value = new Date()
  const timeout = window.setTimeout(() => activeRequest.abort('timeout'), 20000)

  try {
    const [day, scenarioCatalog] = await Promise.all([
      loadWorkspaceVisualization(props.context.organization.id, today.value, activeRequest.signal),
      loadScenarioCatalog(props.context.organization.id, activeRequest.signal)
    ])
    if (request !== activeRequest || activeRequest.signal.aborted) return
    data.value = day
    catalog.value = scenarioCatalog.workPosts
    updatedAt.value = new Intl.DateTimeFormat('ru-RU', {
      hour: '2-digit', minute: '2-digit', timeZone: props.context.organization.timeZoneId || 'UTC'
    }).format(new Date())
    errorMessage.value = ''
  } catch (error) {
    if (request !== activeRequest || (activeRequest.signal.aborted && activeRequest.signal.reason !== 'timeout')) return
    const timedOut = activeRequest.signal.reason === 'timeout'
    activeRequest.abort()
    errorMessage.value = timedOut ? 'Сервер долго отвечает. Повторите обновление.'
      : error instanceof Error ? error.message : 'Не удалось обновить визуализацию.'
  } finally {
    window.clearTimeout(timeout)
    if (request === activeRequest) isLoading.value = false
  }
}

watch(() => props.context.organization.id, () => {
  data.value = null
  catalog.value = []
  updatedAt.value = ''
  errorMessage.value = ''
  selectedPostId.value = ''
  selectedTray.value = null
  previewId.value = ''
  void load()
}, { immediate: true })

watch(() => model.value.posts, posts => {
  if (!posts.some(post => post.id === selectedPostId.value)) selectedPostId.value = ''
})

const timer = window.setInterval(() => {
  if (document.visibilityState !== 'visible') return
  now.value = new Date()
  if (!isLoading.value) void load()
}, 15000)

onBeforeUnmount(() => {
  request?.abort()
  request = null
  window.clearInterval(timer)
})
</script>

<template>
  <UiCard page title="Визуализация">
    <div class="visualization">
      <div class="visualization__toolbar">
        <div class="visualization__live"><i aria-hidden="true"></i><strong>Посты сейчас</strong><span>{{ today.split('-').reverse().join('.') }}</span></div>
        <div class="visualization__refresh-actions">
          <span v-if="updatedAt" class="visualization__updated" role="status">Обновлено {{ updatedAt }}</span>
          <button class="visualization__refresh" type="button" aria-label="Обновить визуализацию" title="Обновить" :disabled="isLoading" @click="load">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 7v5h-5M4 17v-5h5M5.5 7a7.5 7.5 0 0 1 13-1L20 9M4 15l1.5 3a7.5 7.5 0 0 0 13-1"/></svg>
          </button>
        </div>
      </div>
      <div v-if="errorMessage" class="visualization__error" role="alert">
        <strong>{{ data ? 'Показаны последние загруженные данные.' : 'Не удалось загрузить посты.' }}</strong><span>{{ errorMessage }}</span>
        <UiButton variant="secondary" :disabled="isLoading" @click="load">Повторить загрузку</UiButton>
      </div>
      <UiLoader v-if="isLoading && !data" text="Загружаем посты и работы…" />
      <template v-if="data">
        <div class="visualization__summary" aria-label="Состояние работ">
          <div class="visualization__load"><span>Занято постов</span><strong>{{ occupiedCount }} <small>/ {{ model.posts.length }}</small></strong><progress :value="occupiedCount" :max="model.posts.length || 1" :aria-label="`Занято ${occupiedCount} из ${model.posts.length} постов`"></progress><small>Свободно сейчас: {{ model.posts.length - occupiedCount }}</small></div>
          <button type="button" class="visualization__metric metric-waiting" :disabled="!waitingEntries.length" :aria-pressed="selectedTray === 'waiting'" @click="selectedTray = 'waiting'"><span><i aria-hidden="true"></i>Ожидают начала</span><strong>{{ waitingEntries.length }}</strong><small>Время записи наступило</small></button>
          <button type="button" class="visualization__metric metric-paused" :disabled="!pausedEntries.length" :aria-pressed="selectedTray === 'paused'" @click="selectedTray = 'paused'"><span><i aria-hidden="true"></i>Есть пауза</span><strong>{{ pausedEntries.length }}</strong><small>Заказов с паузой в работах</small></button>
          <button type="button" class="visualization__metric" :disabled="!model.unassigned.length" :aria-pressed="selectedTray === 'unassigned'" @click="selectedTray = 'unassigned'"><span><i aria-hidden="true"></i>Без поста</span><strong>{{ model.unassigned.length }}</strong><small>Не назначен пост</small></button>
          <button type="button" class="visualization__metric metric-ready" :disabled="!model.ready.length" :aria-pressed="selectedTray === 'ready'" @click="selectedTray = 'ready'"><span><i aria-hidden="true"></i>Готовы к выдаче</span><strong>{{ model.ready.length }}</strong><small>Работы завершены</small></button>
        </div>
        <div class="visualization__layout">
          <section class="visualization__view" aria-label="Посты организации">
            <header class="visualization__view-header ui-section-header"><div><h3>Загрузка постов</h3><button v-if="conflictPosts.length" type="button" class="visualization__conflict" @click="selectPost(conflictPosts[0]!.id)">Конфликтов: {{ conflictPosts.length }}</button></div></header>
            <div v-if="model.posts.length" class="visualization__post-picker">
              <UiSelect :model-value="selectedTray ? '' : selectedPostId" aria-label="Пост" placeholder="Выберите пост" :options="model.posts.map(post => ({ value: post.id, label: `${post.name} · ${stateLabels[post.state]}` }))" @update:model-value="selectPost" />
              <button v-if="selectedPostId || selectedTray" type="button" class="visualization__clear" aria-label="Сбросить выбор" title="Сбросить выбор" @click="selectPost('')">
                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 6 12 12M6 18 18 6"/></svg>
              </button>
            </div>
            <WorkPostsScene v-if="model.posts.length && !isSceneUnavailable" :posts="model.posts" :selected-post-id="selectedTray ? '' : selectedPostId" @select="selectPost" @unavailable="isSceneUnavailable = true" />
            <div v-if="!model.posts.length" class="visualization__empty">Посты пока не настроены. Добавьте их в справочнике организации.</div>
            <p v-if="isSceneUnavailable" class="visualization__notice">3D недоступно. Выберите пост в списке ниже.</p>
            <div v-if="isSceneUnavailable" class="visualization__posts" aria-label="Список постов">
              <button v-for="post in model.posts" :key="post.id" type="button" :aria-pressed="!selectedTray && selectedPostId === post.id" @click="selectPost(post.id)"><strong>{{ post.name }}</strong><span class="visualization__badge" :class="`state-${post.state}`"><i aria-hidden="true"></i>{{ stateLabels[post.state] }}</span></button>
            </div>
            <div v-if="model.posts.length" class="visualization__legend" aria-label="Обозначения цветов"><span v-for="(label, state) in stateLabels" :key="state"><i :class="`state-${state}`" aria-hidden="true"></i>{{ label }}</span></div>
          </section>
          <aside class="visualization__details" aria-label="Работы выбранного поста">
            <header class="visualization__details-header ui-section-header"><div><small>{{ selectedTray ? 'Подборка заказов' : 'Выбранный пост' }}</small><h3>{{ selectionTitle }}</h3></div></header>
            <p v-if="!selectedTray && selectedPost?.isLegacy" class="visualization__notice">Пост отсутствует среди активных позиций справочника.</p><p v-if="!selectedTray && selectedPost?.state === 'conflict'" class="visualization__notice notice-conflict">На пост претендуют несколько заказов. Проверьте статусы и назначение поста.</p>
            <div v-if="!selectedEntries.length" class="visualization__empty"><strong>{{ selectedTray ? 'Нет заказов' : selectedPost ? 'Пост свободен' : 'Выберите пост' }}</strong><span>{{ selectedTray ? 'В этой подборке пока нет записей.' : selectedPost ? 'Текущих и ожидаемых записей нет.' : 'Здесь появятся его текущие и ожидаемые записи.' }}</span></div>
            <article v-for="entry in selectedEntries" :key="entry.item.id" class="visualization__entry">
              <div class="visualization__entry-top"><span class="visualization__badge" :class="entry.stage === 'ready' ? 'state-free' : entry.isOverdue ? 'state-overdue' : entry.stage === 'planned' ? 'state-planned' : entry.item.workPausedCount ? 'state-paused' : 'state-inProgress'"><i aria-hidden="true"></i>{{ entryLabel(entry) }}</span></div>
              <div class="visualization__vehicle"><img v-if="getVehicleBrandLogo(entry.item.vehicleBrandLogoKey)" :src="getVehicleBrandLogo(entry.item.vehicleBrandLogoKey)!.src" :alt="`Логотип ${getVehicleBrandLogo(entry.item.vehicleBrandLogoKey)!.name}`"><div><strong>{{ entry.item.vehicleName }}</strong><span v-if="entry.item.registrationPlate && !entry.item.vehicleName.includes(entry.item.registrationPlate)" class="visualization__plate">{{ entry.item.registrationPlate }}</span></div></div>
              <p class="visualization__entry-services">{{ entry.item.services.join(' · ') || 'Услуги не указаны' }}</p>
              <div v-if="entry.stage !== 'planned' && entryProgress(entry).total" class="visualization__work-progress"><span>Выполнено работ <strong>{{ entryProgress(entry).completed }} / {{ entryProgress(entry).total }}</strong></span><progress :value="entryProgress(entry).completed" :max="entryProgress(entry).total" :aria-label="`Выполнено ${entryProgress(entry).completed} из ${entryProgress(entry).total} работ`"></progress><small v-if="entry.item.workPausedCount">На паузе: {{ entry.item.workPausedCount }}</small></div>
              <dl class="visualization__facts"><div><dt>Запись</dt><dd>{{ entrySchedule(entry) }}</dd></div><div v-if="entry.item.performer"><dt>Исполнитель</dt><dd>{{ entry.item.performer }}</dd></div><div v-if="entry.item.clientName"><dt>{{ context.shell.scenario?.code === 'fleet' ? (context.shell.scenario.terms?.clients || 'Ответственный') : 'Клиент' }}</dt><dd>{{ entry.item.clientName }}</dd></div></dl>
              <button v-if="selectedTray && postForEntry(entry)" type="button" class="visualization__post-link" @click="selectPost(postForEntry(entry)!.id)">{{ postForEntry(entry)!.name }}</button><span v-else-if="selectedTray === 'waiting' || selectedTray === 'unassigned'" class="visualization__muted">Пост не назначен</span>
              <a v-if="canOpenMaintenance" class="visualization__open" :href="`#maintenance/${entry.item.id}`" @click="openPreview($event, entry.item)">Посмотреть заказ</a>
            </article>
          </aside>
        </div>
      </template>
    </div>
  </UiCard>
  <WorkspaceWorkdayPreview v-if="previewItem && canOpenMaintenance" :key="context.organization.id + ':' + previewItem.id" :context="context" :item="previewItem" @close="previewId = ''" />
</template>

<style scoped>
.visualization { display: grid; gap: 18px; color: var(--crm-text, #202733); container-type: inline-size; }
.visualization__toolbar, .visualization__live, .visualization__view-header, .visualization__view-header > div, .visualization__entry-top { display: flex; align-items: center; gap: 10px; }
.visualization__toolbar, .visualization__view-header, .visualization__entry-top { justify-content: space-between; }
.visualization__live { flex-wrap: wrap; font-size: 14px; }
.visualization .visualization__live > i { background: #62a994; }
.visualization__live > span, .visualization__updated { font-size: 12px; color: var(--crm-text-muted, #667085); }
.visualization__refresh-actions { display: flex; align-items: center; gap: 10px; margin-left: auto; }
.visualization__refresh, .visualization__clear { display: grid; place-items: center; flex: 0 0 32px; width: 32px; height: 32px; padding: 0; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 8px; background: var(--crm-surface, #fff); color: var(--crm-text-muted, #667085); cursor: pointer; }
.visualization__refresh svg, .visualization__clear svg { width: 15px; height: 15px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
.visualization__refresh:hover, .visualization__clear:hover { border-color: var(--crm-line-strong, #cfd5dd); color: var(--opencrm-accent, #e67337); }
.visualization__refresh:disabled { opacity: .45; cursor: wait; }
.visualization__summary { display: grid; grid-template-columns: 1.15fr repeat(4, 1fr); gap: 10px; }
.visualization__load, .visualization__metric { display: grid; align-content: start; gap: 9px; padding: 14px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 10px; background: var(--crm-surface, #fff); text-align: left; color: inherit; font: inherit; }
.visualization__metric { cursor: pointer; }
.visualization__metric > span, .visualization__load > span { display: flex; align-items: center; gap: 7px; font-size: 12px; font-weight: 600; }
.visualization__metric > strong, .visualization__load > strong { font-size: 25px; line-height: 1; }
.visualization__metric small, .visualization__load small { font-size: 11px; color: var(--crm-text-muted, #667085); font-weight: 400; }
.visualization__metric small { display: flex; gap: 6px; justify-content: space-between; }
.visualization__metric[aria-pressed="true"], .visualization__posts button[aria-pressed="true"] { border-color: var(--opencrm-accent, #e67337); box-shadow: inset 0 0 0 1px var(--opencrm-accent, #e67337); }
.visualization__metric:disabled { cursor: default; }
.visualization i { display: inline-block; flex: 0 0 7px; width: 7px; height: 7px; border-radius: 50%; background: #8795a9; }
.visualization .metric-waiting > span > i, .visualization .state-overdue { background: #de4555; }
.visualization .metric-paused > span > i, .visualization .state-paused { background: #d7a133; }
.visualization .metric-ready > span > i, .visualization .state-free { background: #62a994; }
.visualization .state-inProgress { background: #e67337; }
.visualization .state-conflict { background: #aa78d3; }
.visualization .state-planned { background: #8795a9; }
.visualization progress { appearance: none; display: block; width: 100%; height: 5px; border: 0; border-radius: 9px; overflow: hidden; background: var(--crm-line, #e3e6eb); accent-color: var(--opencrm-accent, #e67337); }
.visualization progress::-webkit-progress-bar { background: var(--crm-line, #e3e6eb); border-radius: 9px; }
.visualization progress::-webkit-progress-value { background: var(--opencrm-accent, #e67337); border-radius: 9px; }
.visualization progress::-moz-progress-bar { background: var(--opencrm-accent, #e67337); border-radius: 9px; }
.visualization__layout { display: grid; grid-template-columns: minmax(0, 1fr) 310px; gap: 16px; align-items: start; }
.visualization__view { min-width: 0; }
.visualization__post-picker { display: flex; align-items: center; gap: 8px; width: min(100%, 360px); margin-bottom: 14px; }
.visualization__post-picker > .ui-field { flex: 1; min-width: 0; }
.visualization__view-header { flex-wrap: wrap; margin-bottom: 12px; }
.visualization h3 { margin: 0; font-size: 15px; }
.visualization__conflict, .visualization__post-link { border: 0; background: none; color: var(--opencrm-accent, #e67337); font: inherit; font-size: 12px; cursor: pointer; text-align: left; padding: 0; }
.visualization__conflict { color: var(--crm-danger-text, #b34756); }
.visualization__legend { display: flex; flex-wrap: wrap; gap: 8px 14px; margin-top: 12px; font-size: 11px; color: var(--crm-text-muted, #667085); }
.visualization__legend > span { display: inline-flex; align-items: center; gap: 6px; }
.visualization__posts { display: grid; gap: 8px; }
.visualization__posts button { display: flex; justify-content: space-between; align-items: center; gap: 8px; padding: 12px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 8px; background: var(--crm-surface, #fff); color: inherit; font: inherit; font-size: 13px; text-align: left; cursor: pointer; }
.visualization__details { border: 1px solid var(--crm-line, #e3e6eb); border-radius: 10px; overflow: hidden; background: var(--crm-surface, #fff); max-height: 700px; overflow-y: auto; }
.visualization__details-header { display: flex; justify-content: space-between; gap: 10px; align-items: center; padding: 16px; background: var(--crm-section-header-bg, #f7f8fa); border-bottom: 1px solid var(--crm-section-header-border, #e3e6eb); }
.visualization__details-header > div { display: grid; gap: 5px; }
.visualization__details-header small { color: var(--crm-text-muted, #667085); font-size: 11px; }
.visualization__entry { display: grid; gap: 14px; padding: 16px; font-size: 12px; }
.visualization__entry + .visualization__entry { border-top: 1px solid var(--crm-line, #e3e6eb); }
.visualization__vehicle { display: flex; align-items: center; gap: 11px; min-width: 0; }
.visualization__vehicle img { width: 36px; height: 36px; object-fit: contain; padding: 4px; border-radius: 7px; background: #fff; }
.visualization__vehicle > div { display: grid; gap: 5px; min-width: 0; }
.visualization__muted { color: var(--crm-text-muted, #667085); font-size: 12px; }
.visualization__badge { display: inline-flex; align-items: center; gap: 5px; border-radius: 6px; padding: 5px 7px; font-size: 10px; font-weight: 600; }
.visualization .visualization__badge { background: color-mix(in srgb, var(--status-color) 12%, var(--crm-surface, #fff)); color: var(--crm-text, #202733); }
.visualization__badge i { background: var(--status-color); }
.visualization__badge.state-free { --status-color: #62a994; }
.visualization__badge.state-inProgress { --status-color: #e67337; }
.visualization__badge.state-overdue { --status-color: #de4555; }
.visualization__badge.state-paused { --status-color: #d7a133; }
.visualization__badge.state-conflict { --status-color: #aa78d3; }
.visualization__badge.state-planned { --status-color: #8795a9; }
.visualization__vehicle strong { font-size: 17px; overflow-wrap: anywhere; }
.visualization__plate { justify-self: start; padding: 3px 6px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 4px; font-size: 11px; font-weight: 600; }
.visualization__entry-services { margin: 0; color: var(--crm-text-muted, #667085); line-height: 1.6; }
.visualization__work-progress { display: grid; gap: 8px; }
.visualization__work-progress > span { display: flex; justify-content: space-between; gap: 10px; color: var(--crm-text-muted, #667085); font-size: 11px; }
.visualization__work-progress strong { color: var(--crm-text, #202733); }
.visualization__work-progress small { font-size: 11px; color: var(--crm-text-muted, #667085); }
.visualization__facts { display: grid; gap: 10px; margin: 0; }
.visualization__facts > div { display: grid; gap: 4px; }
.visualization__facts dt { color: var(--crm-text-muted, #667085); font-size: 11px; }
.visualization__facts dd { margin: 0; overflow-wrap: anywhere; }
.visualization__open { display: flex; justify-content: center; gap: 10px; padding: 10px 12px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 7px; text-decoration: none; color: inherit; font-weight: 600; }
.visualization__open:hover { border-color: var(--opencrm-accent, #e67337); color: var(--opencrm-accent, #e67337); }
.visualization__empty { display: grid; gap: 7px; color: var(--crm-text-muted, #667085); padding: 25px 16px; font-size: 13px; line-height: 1.5; }
.visualization__empty strong { color: var(--crm-text, #202733); }
.visualization__error { display: grid; gap: 5px; padding: 12px; border: 1px solid var(--crm-danger-border, #de4555); border-radius: 7px; font-size: 13px; }
.visualization__notice { margin: 12px 16px; font-size: 12px; line-height: 1.5; color: var(--crm-text-muted, #667085); }
.notice-conflict { padding: 10px; margin: 12px; border-radius: 6px; background: var(--crm-danger-surface, #fff2ed); color: var(--crm-danger-text, #9c4450); }
.visualization button:focus-visible, .visualization__open:focus-visible { outline: 2px solid var(--opencrm-accent, #e67337); outline-offset: 3px; }
@container (max-width: 1000px) {
  .visualization__summary { grid-template-columns: repeat(4, 1fr); }
  .visualization__load { grid-column: 1 / -1; grid-template-columns: auto auto minmax(60px, 1fr) auto; align-items: center; }
  .visualization__load > strong { font-size: 20px; }
  .visualization__layout { grid-template-columns: minmax(0, 1fr) 290px; }
}
@container (max-width: 760px) {
  .visualization__layout { grid-template-columns: minmax(0, 1fr); }
  .visualization__details { max-height: none; }
  .visualization__summary { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .visualization__metric { grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: 8px; }
  .visualization__metric > strong { font-size: 20px; }
  .visualization__metric small { display: none; }
  .visualization__load { grid-template-columns: auto auto; }
  .visualization__load progress { grid-column: 1 / -1; grid-row: 2; }
  .visualization__load > small { grid-column: 1 / -1; }
  .visualization__toolbar { gap: 10px; flex-wrap: wrap; }
}
@container (max-width: 380px) {
  .visualization__posts { grid-template-columns: minmax(0, 1fr); }
  .visualization__metric, .visualization__load { padding: 11px; }
  .visualization__metric small { font-size: 10px; }
}
</style>
