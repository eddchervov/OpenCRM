<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiCard, UiLoader, UiPagination, UiSearchInput, UiSelect } from '@opencrm/ui'
import type { WorkspaceContext } from '../types'
import { inquirySources, inquiryStages, loadInquiries, loadInquiryBoard, type Inquiry, type InquiryColumn } from '../api/inquiries'
import { formatInquiryTime } from '../utils/inquiryTime'
import { createMemberSelectOptions } from '../utils/memberSelectOptions'
import InquiryCardSection from './InquiryCardSection.vue'

const props = defineProps<{ context: WorkspaceContext; inquiryId?: string }>()
const emit = defineEmits<{ changed: [] }>()
const search = ref('')
const source = ref('')
const responsible = ref('')
const stage = ref('')
const due = ref('')
const filtersOpen = ref(false)
const view = ref<'board' | 'list'>(window.matchMedia('(max-width: 650px)').matches ? 'list' : 'board')
const page = ref(1)
const items = ref<Inquiry[]>([])
const columns = ref<InquiryColumn[]>([])
const total = ref(0)
const canManage = ref(false)
const canViewClients = ref(false)
const canViewVehicles = ref(false)
const timeZone = ref<string | null>(null)
const loading = ref(false)
const error = ref('')
let request: AbortController | undefined
let searchTimer: ReturnType<typeof setTimeout> | undefined
const activeFilters = computed(() => [responsible.value, source.value, stage.value, due.value].filter(Boolean).length)
const resultCount = computed(() => listMode.value ? total.value : columns.value.reduce((count, column) => count + column.count, 0))
const listMode = computed(() => view.value === 'list' || !!stage.value)
const memberOptions = computed(() => [{ value: '', label: 'Все ответственные' }, { value: props.context.user.id, label: 'Только мои' },
  ...createMemberSelectOptions((props.context.organization.members ?? []).filter(member => member.userId !== props.context.user.id).map(member => ({ ...member, value: member.userId })))])

async function load() {
  request?.abort()
  const activeRequest = new AbortController()
  request = activeRequest
  loading.value = true
  error.value = ''
  try {
    const query = { searchQuery: search.value, sourceCode: source.value, responsibleUserId: responsible.value, stageCode: stage.value, actionDue: due.value, page: page.value, pageSize: listMode.value ? 20 : 8 }
    const result = listMode.value
      ? await loadInquiries(props.context.organization.id, query, activeRequest.signal)
      : await loadInquiryBoard(props.context.organization.id, query, activeRequest.signal)
    if (activeRequest.signal.aborted) return
    canManage.value = result.canManage && props.context.workspaceAccess?.isReadOnly !== true
    canViewClients.value = result.canViewClients
    canViewVehicles.value = result.canViewVehicles
    timeZone.value = result.timeZoneId
    if ('columns' in result) columns.value = result.columns
    else { items.value = result.items; total.value = result.totalCount }
  } catch (caught) {
    if (!activeRequest.signal.aborted) error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить обращения.'
  } finally {
    if (request === activeRequest) loading.value = false
  }
}

watch([source, responsible, stage, due, view], () => { page.value = 1; void load() })
watch(search, () => { clearTimeout(searchTimer); searchTimer = setTimeout(() => { page.value = 1; void load() }, 300) })
watch(page, () => { void load() })
watch(() => [props.context.organization.id, props.inquiryId], () => { if (!props.inquiryId || props.inquiryId === 'new') void load() }, { immediate: true })
onBeforeUnmount(() => { request?.abort(); clearTimeout(searchTimer) })

function resetFilters() { source.value = ''; responsible.value = ''; stage.value = ''; due.value = '' }
function showBoard() { stage.value = ''; view.value = 'board' }
function stageName(value: string) { return inquiryStages.find(item => item.value === value)?.label ?? value }
function sourceName(value: string) { return inquirySources.find(item => item.value === value)?.label ?? value }
function cardTitle(item: Inquiry) { return item.contactName || item.organizationClientName || 'Обращение' }
function overdue(item: Inquiry) { return !!item.nextActionAt && new Date(item.nextActionAt).getTime() < Date.now() }
</script>

<template>
  <InquiryCardSection v-if="inquiryId" :key="context.organization.id + ':' + inquiryId" :context="context" :inquiry-id="inquiryId" :can-create="canManage" :can-view-clients="canViewClients" :can-view-vehicles="canViewVehicles" :time-zone-id="timeZone" @changed="emit('changed')" />
  <UiCard v-else page title="Обращения">
    <div class="inquiries">
      <div class="inquiries__toolbar">
        <UiSearchInput v-model="search" placeholder="Поиск по контакту, телефону или запросу" label="Поиск обращений" />
        <div class="inquiries__views" role="group" aria-label="Представление обращений">
          <button type="button" :aria-pressed="!listMode" @click="showBoard"><svg viewBox="0 0 20 20" aria-hidden="true"><rect x="3" y="4" width="5" height="12" rx="1"/><rect x="12" y="4" width="5" height="12" rx="1"/></svg>Доска</button>
          <button type="button" :aria-pressed="listMode" @click="view = 'list'"><svg viewBox="0 0 20 20" aria-hidden="true"><path d="M7 5h10M7 10h10M7 15h10M3 5h.01M3 10h.01M3 15h.01"/></svg>Список</button>
        </div>
        <UiButton v-if="canManage" class="inquiries__create" href="#inquiries/new" aria-label="Новое обращение"><span aria-hidden="true">＋</span><span class="inquiries__new-label">Новое обращение</span><span class="inquiries__new-short">Создать</span></UiButton>
      </div>
      <div class="inquiries__filter-bar">
        <div class="inquiries__filter-actions"><button type="button" class="inquiries__filter-toggle" :aria-expanded="filtersOpen" aria-controls="inquiry-filters" @click="filtersOpen = !filtersOpen"><svg viewBox="0 0 20 20" aria-hidden="true"><path d="M3 5h14M5 10h10M8 15h4"/></svg>Фильтры<span v-if="activeFilters" class="inquiries__count">{{ activeFilters }}</span></button><button v-if="activeFilters" type="button" class="inquiries__reset" @click="resetFilters">Сбросить</button><span class="inquiries__scope">{{ stage ? stageName(stage) : 'Открытые обращения' }}</span></div>
        <span class="inquiries__result" aria-live="polite">{{ loading ? 'Загрузка…' : 'Всего: ' + resultCount }}</span>
      </div>
      <div v-if="filtersOpen" id="inquiry-filters" class="inquiries__filters">
        <UiSelect v-model="responsible" label="Ответственный" :options="memberOptions" />
        <UiSelect v-model="source" label="Источник" :options="[{ value: '', label: 'Все источники' }, ...inquirySources]" />
        <UiSelect v-model="stage" label="Этап" :options="[{ value: '', label: 'Открытые обращения' }, ...inquiryStages]" />
        <UiSelect v-model="due" label="Следующее действие" :options="[{ value: '', label: 'Любой срок' }, { value: 'overdue', label: 'Просрочено' }, { value: 'today', label: 'Сегодня' }]" />
      </div>
      <p v-if="error" role="alert" class="inquiries__error">{{ error }} <button type="button" @click="load">Повторить</button></p>
      <UiLoader v-if="loading" />
      <div v-else-if="!error && !listMode" class="inquiries__board">
        <section v-for="column in columns" :key="column.stageCode" class="inquiries__column" :class="'inquiries__column--' + column.stageCode">
          <header class="inquiries__column-header"><h3><i aria-hidden="true"></i>{{ stageName(column.stageCode) }}</h3><span class="inquiries__count">{{ column.count }}</span></header>
          <div class="inquiries__column-body">
            <a v-for="item in column.items" :key="item.id" :href="'#inquiries/' + item.id" class="inquiry-tile">
              <strong class="inquiry-tile__title">{{ cardTitle(item) }}</strong>
              <p class="inquiry-tile__request">{{ item.interestText }}</p>
              <div v-if="item.vehicleId || item.vehicleDescription" class="inquiry-vehicle">
                <span class="inquiry-vehicle__label">{{ item.vehicleId ? 'Автомобиль из базы' : 'Со слов клиента' }}</span>
                <span class="inquiry-vehicle__name">{{ item.vehicleId ? item.vehicleName || 'Автомобиль без названия' : item.vehicleDescription }}</span>
                <small v-if="item.vehicleId && item.vehicleDescription" class="inquiry-vehicle__description">Со слов клиента: {{ item.vehicleDescription }}</small>
              </div>
              <div class="inquiry-tile__meta"><span>{{ sourceName(item.sourceCode) }}</span><span :title="item.responsibleUserName || 'Ответственный не назначен'">{{ item.responsibleUserName || 'Не назначен' }}</span></div>
              <div v-if="item.nextActionText" class="inquiry-tile__action"><span>{{ item.nextActionText }}</span><time :datetime="item.nextActionAt || undefined" :class="{ 'inquiry-due--overdue': overdue(item) }">{{ formatInquiryTime(item.nextActionAt, timeZone) }}</time></div>
            </a>
            <div v-if="!column.items.length" class="inquiries__column-empty"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 7h14v13H5zM8 4h8M9 11h6M9 15h4"/></svg><span>Пока нет обращений</span></div>
            <button v-if="column.hasMore" type="button" class="inquiries__more" @click="stage = column.stageCode">Показать все · {{ column.count }} →</button>
          </div>
        </section>
      </div>
      <div v-else-if="!error" class="inquiries__list">
        <div v-if="items.length" class="inquiry-row inquiry-row--header" aria-hidden="true"><span>Контакт и запрос</span><span>Этап</span><span>Ответственный</span><span>Следующее действие</span></div>
        <a v-for="item in items" :key="item.id" :href="'#inquiries/' + item.id" class="inquiry-row">
          <div class="inquiry-row__contact">
            <strong>{{ cardTitle(item) }}</strong>
            <span>{{ item.interestText }}</span>
            <div v-if="item.vehicleId || item.vehicleDescription" class="inquiry-vehicle">
              <span class="inquiry-vehicle__label">{{ item.vehicleId ? 'Автомобиль из базы' : 'Со слов клиента' }}</span>
              <span class="inquiry-vehicle__name">{{ item.vehicleId ? item.vehicleName || 'Автомобиль без названия' : item.vehicleDescription }}</span>
              <small v-if="item.vehicleId && item.vehicleDescription" class="inquiry-vehicle__description">Со слов клиента: {{ item.vehicleDescription }}</small>
            </div>
          </div>
          <div><span class="inquiry-row__stage">{{ stageName(item.stageCode) }}</span></div>
          <span class="inquiry-row__owner">{{ item.responsibleUserName || 'Не назначен' }}</span>
          <div class="inquiry-row__action"><span>{{ item.nextActionText || '—' }}</span><time v-if="item.nextActionAt" :datetime="item.nextActionAt" :class="{ 'inquiry-due--overdue': overdue(item) }">{{ formatInquiryTime(item.nextActionAt, timeZone) }}</time></div>
        </a>
        <p v-if="!items.length" class="inquiries__empty">Обращения не найдены</p>
        <UiPagination v-model="page" :total-items="total" :items-per-page="20" label="Страницы обращений" />
      </div>
    </div>
  </UiCard>
</template>

<style scoped>
.inquiries { display: grid; gap: 16px; min-width: 0; color: var(--crm-text, #263142); }
.inquiries__toolbar { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.inquiries__toolbar > :first-child { flex: 1; min-width: 200px; }
.inquiries__create { white-space: nowrap; gap: 6px; }
.inquiries__new-short { display: none; }
.inquiries__views { display: flex; gap: 2px; padding: 3px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 9px; background: var(--crm-surface-soft, #f5f6f8); }
.inquiries__views button { display: flex; align-items: center; gap: 6px; padding: 7px 10px; border: 0; border-radius: 6px; background: transparent; color: var(--crm-text-muted, #667085); cursor: pointer; font: inherit; font-size: 13px; }
.inquiries__views button[aria-pressed=true] { background: var(--crm-surface, #fff); color: var(--crm-text, #263142); box-shadow: 0 1px 3px var(--crm-shadow, #10182818); }
.inquiries svg { width: 17px; height: 17px; fill: none; stroke: currentColor; stroke-width: 1.7; stroke-linecap: round; stroke-linejoin: round; flex-shrink: 0; }
.inquiries__filter-bar, .inquiries__filter-actions { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
.inquiries__filter-bar { justify-content: space-between; padding-bottom: 14px; border-bottom: 1px solid var(--crm-line, #e3e6eb); }
.inquiries__filter-toggle { display: inline-flex; align-items: center; gap: 6px; padding: 6px 9px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 6px; background: var(--crm-surface, #fff); color: inherit; cursor: pointer; font: inherit; font-size: 12px; }
.inquiries__filter-toggle[aria-expanded=true] { background: var(--crm-surface-soft, #f5f6f8); }
.inquiries__reset, .inquiries__more { border: 0; background: none; color: var(--crm-accent, #df6c22); font: inherit; font-size: 12px; cursor: pointer; }
.inquiries__scope, .inquiries__result { font-size: 12px; color: var(--crm-text-muted, #667085); }
.inquiries__filters { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; padding: 16px; border-radius: 10px; background: var(--crm-surface-soft, #f5f6f8); }
.inquiries__board { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px; align-items: start; }
.inquiries__column { min-width: 0; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 10px; overflow: hidden; }
.inquiries__column-header { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 13px 12px; border-bottom: 1px solid var(--crm-line, #e3e6eb); }
.inquiries__column-header h3 { display: flex; gap: 7px; align-items: center; margin: 0; font-size: 13px; font-weight: 600; }
.inquiries__column-header i { width: 6px; height: 6px; border-radius: 50%; background: var(--crm-text-muted, #667085); }
.inquiries__column--clarifying i { background: #7399bd; }
.inquiries__column--proposal i { background: var(--crm-accent, #df6c22); }
.inquiries__column--ready i { background: #5d9e7b; }
.inquiries__count { padding: 2px 6px; min-width: 20px; box-sizing: border-box; border-radius: 5px; background: var(--crm-surface-soft, #f5f6f8); color: var(--crm-text-muted, #667085); text-align: center; font-size: 11px; font-weight: 600; }
.inquiries__column-body { display: grid; grid-template-columns: minmax(0, 1fr); gap: 10px; align-content: start; padding: 10px; min-height: 210px; background: var(--crm-surface-soft, #f5f6f8); }
.inquiry-tile { display: grid; grid-template-columns: minmax(0, 1fr); gap: 10px; padding: 14px; text-decoration: none; overflow-wrap: anywhere; background: var(--crm-surface, #fff); color: inherit; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 8px; font-size: 13px; transition: border-color .15s, box-shadow .15s; }
.inquiry-tile:hover { border-color: var(--crm-accent, #df6c22); box-shadow: 0 3px 10px var(--crm-shadow, #1018280c); }
.inquiry-tile__title { font-size: 13px; line-height: 1.45; }
.inquiry-tile__request { display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 3; overflow: hidden; margin: 0; line-height: 1.5; }
.inquiry-vehicle { display: grid; gap: 3px; font-size: 12px; line-height: 1.45; }
.inquiry-vehicle__label, .inquiry-vehicle__description { color: var(--crm-text-muted, #667085); font-size: 11px; }
.inquiry-vehicle__name { font-weight: 600; }
.inquiry-tile__meta { display: flex; justify-content: space-between; gap: 8px; color: var(--crm-text-muted, #667085); font-size: 11px; }
.inquiry-tile__meta > span { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.inquiry-tile__meta > span:first-child { flex-shrink: 0; }
.inquiry-tile__action { display: grid; gap: 7px; padding-top: 10px; border-top: 1px solid var(--crm-line, #e3e6eb); font-size: 12px; line-height: 1.4; }
.inquiry-tile time, .inquiry-row time { color: var(--crm-text-muted, #667085); font-size: 11px; }
.inquiries .inquiry-due--overdue, .inquiries__error { color: var(--crm-danger-text, #bd4141); }
.inquiries__column-empty { display: grid; justify-items: center; gap: 10px; padding: 45px 4px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiries__column-empty svg { width: 23px; height: 23px; opacity: .6; }
.inquiries__more { padding: 6px; }
.inquiries__empty { padding: 24px 0; text-align: center; color: var(--crm-text-muted, #667085); font-size: 13px; }
.inquiry-row { display: grid; grid-template-columns: minmax(0, 2.2fr) minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1.4fr); gap: 20px; align-items: center; padding: 16px 12px; border-bottom: 1px solid var(--crm-line, #e3e6eb); text-decoration: none; color: inherit; font-size: 13px; overflow-wrap: anywhere; }
.inquiry-row:hover { background: var(--crm-surface-soft, #f5f6f8); }
.inquiry-row--header { padding-top: 8px; padding-bottom: 10px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiry-row--header:hover { background: none; }
.inquiry-row__contact, .inquiry-row__action { display: grid; gap: 6px; }
.inquiry-row__contact > span { display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow: hidden; color: var(--crm-text-muted, #667085); font-size: 12px; line-height: 1.5; }
.inquiry-row small, .inquiry-row__owner { color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiry-row__stage { display: inline-block; padding: 5px 8px; border-radius: 5px; background: var(--crm-surface-soft, #f5f6f8); font-size: 11px; }
@media (max-width: 1150px) { .inquiries__board, .inquiries__filters { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 650px) { .inquiries__board, .inquiries__filters { grid-template-columns: minmax(0, 1fr); } .inquiries__new-label { display: none; } .inquiries__new-short { display: inline; } .inquiries__create { padding: 8px 10px; } .inquiries__toolbar > :first-child { flex-basis: 100%; } .inquiries__views { margin-right: auto; } .inquiries__scope { display: none; } .inquiry-row { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; padding: 16px 8px; } .inquiry-row__contact, .inquiry-row__action { grid-column: 1 / -1; } .inquiry-row--header { display: none; } .inquiry-row__owner { text-align: right; } .inquiry-row__action { padding-top: 10px; border-top: 1px solid var(--crm-line, #e3e6eb); } }
</style>
