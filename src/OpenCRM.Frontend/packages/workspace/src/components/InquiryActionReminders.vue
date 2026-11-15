<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { InquiryRequestError, loadInquiryActionReminders, type Inquiry } from '../api/inquiries'
import { formatInquiryTime } from '../utils/inquiryTime'

const props = defineProps<{ organizationId: string; userId: string }>()
const reminders = ref<Inquiry[]>([])
const loading = ref(false)
const error = ref('')
const allowed = ref(true)
let activeRequest: AbortController | undefined
let requestVersion = 0
let refreshTimer: ReturnType<typeof setInterval> | undefined
let isMounted = false

async function load() {
  activeRequest?.abort()
  const controller = new AbortController()
  activeRequest = controller
  const version = ++requestVersion
  loading.value = true
  error.value = ''

  try {
    const result = await loadInquiryActionReminders(props.organizationId, controller.signal)
    if (!controller.signal.aborted && version === requestVersion) reminders.value = result
  } catch (caught) {
    if (!controller.signal.aborted && version === requestVersion) {
      reminders.value = []
      error.value = 'Не удалось проверить сроки действий.'
      if (caught instanceof InquiryRequestError && [401, 403].includes(caught.status)) {
        allowed.value = false
        if (refreshTimer) clearInterval(refreshTimer)
        refreshTimer = undefined
      }
    }
  } finally {
    if (version === requestVersion) loading.value = false
  }
}

function resetAndLoad() {
  reminders.value = []
  allowed.value = true
  if (isMounted && !refreshTimer) refreshTimer = setInterval(() => { void load() }, 45_000)
  activeRequest?.abort()
  void load()
}

function isOverdue(item: Inquiry) {
  return !!item.nextActionAt && new Date(item.nextActionAt).getTime() <= Date.now()
}

watch(() => [props.organizationId, props.userId], resetAndLoad, { immediate: true })
onMounted(() => {
  isMounted = true
  if (!refreshTimer) refreshTimer = setInterval(() => { void load() }, 45_000)
})
onBeforeUnmount(() => {
  isMounted = false
  activeRequest?.abort()
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>

<template>
  <details v-if="allowed" class="crm-dropdown crm-action-reminders">
    <summary class="crm-icon-button crm-action-reminders__button" :aria-label="`Мои действия: ${reminders.length}`" title="Мои ближайшие действия">
      <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></svg>
      <span v-if="reminders.length" class="crm-action-reminders__badge">{{ reminders.length > 99 ? '99+' : reminders.length }}</span>
    </summary>
    <section class="crm-action-reminders__panel" aria-label="Мои ближайшие действия">
      <strong>Мои ближайшие действия</strong>
      <p v-if="loading && !reminders.length" class="crm-action-reminders__empty">Проверяем сроки…</p>
      <p v-else-if="error" role="alert">{{ error }} <button type="button" @click="load">Повторить</button></p>
      <p v-else-if="!reminders.length" class="crm-action-reminders__empty">Срочных действий нет</p>
      <ul v-else>
        <li v-for="item in reminders" :key="item.id">
          <a :href="`#inquiries/${item.id}`">
            <strong>{{ item.contactName }}</strong>
            <span>{{ item.nextActionText }}</span>
            <small :class="{ 'crm-action-reminders__overdue': isOverdue(item) }">
              {{ isOverdue(item) ? 'Просрочено' : 'Срок' }} · {{ formatInquiryTime(item.nextActionAt, item.timeZoneId) }}
            </small>
          </a>
        </li>
      </ul>
      <a class="crm-action-reminders__all" href="#inquiries">Все обращения</a>
    </section>
  </details>
</template>

<style scoped>
.crm-action-reminders { position: relative; }
.crm-action-reminders__button { position: relative; display: grid; place-items: center; width: 40px; height: 40px; padding: 8px; border: 0; border-radius: 50%; background: color-mix(in srgb, var(--crm-action-warning) 10%, var(--crm-surface, #fff)); color: var(--crm-action-warning); cursor: pointer; list-style: none; }
.crm-action-reminders__button::-webkit-details-marker { display: none; }
.crm-action-reminders__button svg { width: 20px; height: 20px; fill: none; stroke: currentColor; stroke-width: 1.7; stroke-linecap: round; stroke-linejoin: round; }
.crm-action-reminders__button:hover, .crm-action-reminders[open] > summary { background: color-mix(in srgb, var(--crm-action-warning) 18%, var(--crm-surface, #fff)); }
.crm-action-reminders__button:focus-visible { outline: 2px solid var(--crm-focus, #b8551e); outline-offset: 2px; }
.crm-action-reminders__badge { position: absolute; top: -3px; right: -4px; min-width: 17px; height: 17px; padding: 0 4px; border-radius: 9px; background: var(--crm-accent, #b8551e); color: #fff; font-size: 10px; line-height: 17px; text-align: center; }
.crm-action-reminders__panel { position: absolute; z-index: 40; top: calc(100% + 8px); right: 0; width: min(360px, calc(100vw - 24px)); max-height: min(420px, 65dvh); overflow-y: auto; padding: 12px; border: 1px solid var(--crm-border, #e8eaee); border-radius: 10px; background: var(--crm-surface, #fff); color: var(--crm-text, #242b36); box-shadow: 0 12px 28px var(--crm-shadow, #27354b20); }
.crm-action-reminders__panel > strong { display: block; padding: 6px 8px 12px; }
.crm-action-reminders__panel ul { margin: 0; padding: 0; list-style: none; }
.crm-action-reminders__panel li { border-top: 1px solid var(--crm-border, #e8eaee); }
.crm-action-reminders__panel li a { display: grid; gap: 3px; padding: 10px 8px; color: inherit; text-decoration: none; overflow-wrap: anywhere; }
.crm-action-reminders__panel li a:hover { background: var(--crm-hover, #f5f6f8); }
.crm-action-reminders__panel small, .crm-action-reminders__empty { color: var(--crm-text-muted, #667080); font-size: 12px; }
.crm-action-reminders__overdue { color: var(--crm-danger-text, #b42318) !important; font-weight: 600; }
.crm-action-reminders__all { display: block; padding: 10px 8px 4px; color: var(--crm-text, #242b36); }
@media (max-width: 520px) { .crm-action-reminders__panel { right: -56px; } }
</style>
