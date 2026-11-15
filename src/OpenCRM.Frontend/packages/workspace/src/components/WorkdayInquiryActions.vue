<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiModal, UiPagination } from '@opencrm/ui'
import { completeInquiryAction, loadInquiries, rescheduleInquiryAction, type Inquiry, type InquiryPage } from '../api/inquiries'
import { formatInquiryTime, inquiryLocalTime, inquiryUtcTime, resolveInquiryTimeZone } from '../utils/inquiryTime'

const props = defineProps<{ organizationId: string; userId: string; date: string }>()
const result = ref<InquiryPage | null>(null)
const error = ref('')
const page = ref(1)
const busyId = ref('')
const actionError = ref('')
const isOpen = ref(false)
const currentTime = ref(Date.now())
let request: AbortController | undefined
let refreshTimer: ReturnType<typeof setInterval> | undefined
async function load() {
  request?.abort()
  const active = new AbortController()
  request = active
  error.value = ''
  try { result.value = await loadInquiries(props.organizationId, { actionDue: 'through-day', date: props.date, responsibleUserId: props.userId, page: page.value, pageSize: 5 }, active.signal) }
  catch (caught) { if (!active.signal.aborted) error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить следующие действия.' }
}
watch(() => [props.organizationId, props.userId, props.date], () => { isOpen.value = false; result.value = null; page.value = 1; void load() }, { immediate: true })
watch(page, () => { void load() })
onMounted(() => { refreshTimer = setInterval(() => { currentTime.value = Date.now() }, 30_000) })
onBeforeUnmount(() => {
  request?.abort()
  if (refreshTimer) clearInterval(refreshTimer)
})

function isDueSoon(item: Inquiry) {
  if (!item.nextActionAt) return false
  const dueAt = new Date(item.nextActionAt).getTime()
  return dueAt > currentTime.value && dueAt - currentTime.value <= 15 * 60 * 1000
}

function isOverdue(item: Inquiry) {
  return !!item.nextActionAt && new Date(item.nextActionAt).getTime() <= currentTime.value
}

async function runAction(item: Inquiry, action: () => Promise<Inquiry>) {
  if (busyId.value) return
  busyId.value = item.id
  actionError.value = ''
  try {
    await action()
    page.value = 1
    await load()
  } catch (caught) {
    actionError.value = caught instanceof Error ? caught.message : 'Не удалось обновить действие.'
  } finally {
    busyId.value = ''
  }
}

function rescheduleForTomorrow(item: Inquiry) {
  if (!item.nextActionAt) return
  const timeZone = resolveInquiryTimeZone(result.value?.timeZoneId)
  const tomorrow = new Date(`${inquiryLocalTime(new Date().toISOString(), timeZone).slice(0, 10)}T00:00:00Z`)
  tomorrow.setUTCDate(tomorrow.getUTCDate() + 1)
  const date = tomorrow.toISOString().slice(0, 10)
  const nextActionAt = inquiryUtcTime(`${date}T09:00`, timeZone)
  if (!nextActionAt) return
  void runAction(item, () => rescheduleInquiryAction(props.organizationId, item.id, item.version, nextActionAt))
}
</script>

<template>
  <UiButton variant="secondary" aria-haspopup="dialog" :aria-expanded="isOpen" @click="isOpen = true">Обращения <span class="workday-inquiries__count">{{ error ? '!' : result?.totalCount ?? '…' }}</span></UiButton>
  <UiModal :open="isOpen" title="Связаться по обращениям" @close="!busyId && (isOpen = false)">
    <section class="workday-inquiries" aria-label="Связаться с клиентами">
      <header><h3>Обращения <span v-if="result">{{ result.totalCount }}</span></h3><a href="#inquiries">Все обращения →</a></header>
      <p v-if="!result && !error" role="status">Загружаем действия…</p>
      <p v-if="error" role="alert">{{ error }} <button type="button" @click="load">Повторить</button></p>
      <p v-if="actionError" role="alert">{{ actionError }}</p>
      <p v-else-if="result && !result.items.length">Нет действий на выбранный день и просроченных</p>
      <article v-for="item in result?.items" :key="item.id" class="workday-inquiries__row">
        <strong><a :href="`#inquiries/${item.id}`">{{ item.contactName }}</a></strong>
        <span>{{ item.nextActionText }}</span>
        <small>{{ formatInquiryTime(item.nextActionAt, result!.timeZoneId) }} · {{ item.responsibleUserName || 'Без ответственного' }}<span v-if="isOverdue(item)" class="workday-inquiries__overdue"> · Просрочено</span><span v-else-if="isDueSoon(item)" class="workday-inquiries__soon"> · Скоро срок</span></small>
        <div v-if="item.canManage" class="workday-inquiries__actions">
          <UiButton variant="secondary" :disabled="!!busyId" @click="runAction(item, () => completeInquiryAction(organizationId, item.id, item.version))">{{ busyId === item.id ? 'Сохраняем…' : 'Выполнено' }}</UiButton>
          <UiButton variant="secondary" :disabled="!!busyId" @click="rescheduleForTomorrow(item)">На завтра, 09:00</UiButton>
        </div>
      </article>
      <UiPagination v-model="page" :total-items="result?.totalCount || 0" :items-per-page="5" label="Следующие действия" />
    </section>
  </UiModal>
</template>

<style scoped>
.workday-inquiries { min-width: 0; max-height: max(160px, calc(100dvh - 180px)); overflow-y: auto; }
.workday-inquiries__count { min-width: 20px; padding: 2px 5px; border-radius: 5px; background: var(--crm-surface-soft, #f1f3f5); color: var(--crm-text, #263142); font-size: 11px; text-align: center; }
.workday-inquiries header { display: flex; gap: 12px; flex-wrap: wrap; justify-content: space-between; align-items: center; }
.workday-inquiries h3 { font-size: 14px; margin: 0; }
.workday-inquiries h3 span, .workday-inquiries p, .workday-inquiries small { color: var(--crm-text-muted, #667085); font-size: 12px; }
.workday-inquiries a { color: var(--crm-text, #263142); font-size: 13px; }
.workday-inquiries__row { display: grid; grid-template-columns: minmax(0, 1fr); gap: 8px 12px; padding: 12px 0; border-bottom: 1px solid var(--crm-line, #e3e6eb); overflow-wrap: anywhere; }
.workday-inquiries__row:last-of-type { border: 0; }
.workday-inquiries__soon { color: var(--crm-header-accent, #9d5419); font-weight: 600; }
.workday-inquiries__overdue { color: var(--crm-danger-text, #b42318); font-weight: 600; }
.workday-inquiries__actions { grid-column: 1 / -1; display: flex; flex-wrap: wrap; gap: 8px; }
@media (max-width: 650px) { .workday-inquiries__row { grid-template-columns: minmax(0, 1fr); gap: 6px; } }
</style>
