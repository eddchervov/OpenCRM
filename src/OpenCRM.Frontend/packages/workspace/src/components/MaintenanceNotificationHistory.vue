<script setup lang="ts">
import { computed } from 'vue'
import type { TelegramNotificationRecord } from '../api/telegramNotifications'
import type { MaxNotificationRecord } from '../api/maxNotifications'

const props = defineProps<{ telegram: TelegramNotificationRecord[]; max: MaxNotificationRecord[]; timeZoneId?: string | null }>()
const items = computed(() => [
  ...props.telegram.map(item => ({ ...item, channel: 'Telegram' })),
  ...props.max.map(item => ({ ...item, channel: 'MAX' }))
].sort((left, right) => new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime() || `${left.channel}:${left.id}`.localeCompare(`${right.channel}:${right.id}`)))
const eventLabels: Record<string, string> = { booking_created: 'Запись создана', booking_changed: 'Запись перенесена', booking_cancelled: 'Запись отменена', ready: 'Автомобиль готов', booking_reminder: 'Напоминание о визите' }
const statusLabels: Record<string, string> = { queued: 'Ожидает отправки', processing: 'Отправляется', retrying: 'Повторная попытка', sent: 'Принято', simulated: 'Проверка без отправки', failed: 'Ошибка отправки', skipped: 'Данные изменились — отправка отменена' }
const date = (value: string) => new Intl.DateTimeFormat('ru-RU', {
  dateStyle: 'medium', timeStyle: 'short', timeZone: props.timeZoneId || 'UTC'
}).format(new Date(value))
</script>

<template>
  <div class="maintenance-notifications">
    <article v-for="item in items" :key="`${item.channel}:${item.id}`" class="maintenance-notifications__item">
      <header><strong>{{ eventLabels[item.eventCode] ?? item.eventCode }}</strong><span class="maintenance-notifications__channel">{{ item.channel }}</span></header>
      <p>{{ item.body }}</p>
      <footer><span :class="{ 'maintenance-notifications__failed': item.status === 'failed' }">{{ statusLabels[item.status] ?? item.status }}{{ item.status === 'sent' ? ` ${item.channel}` : '' }}</span><time :datetime="item.sentAt ?? item.createdAt">{{ date(item.sentAt ?? item.createdAt) }}</time></footer>
      <small v-if="item.status === 'failed' && item.errorCode">Код ошибки: {{ item.errorCode }}</small>
    </article>
  </div>
</template>

<style scoped>
.maintenance-notifications { display:grid; gap:12px; }
.maintenance-notifications__item { display:grid; gap:12px; padding:16px; border:1px solid var(--crm-line, #d4dce6); border-radius:10px; }
.maintenance-notifications__item header, .maintenance-notifications__item footer { display:flex; justify-content:space-between; flex-wrap:wrap; gap:8px; }
.maintenance-notifications__item strong { font-size:14px; }
.maintenance-notifications__channel { padding:3px 8px; border-radius:6px; background:var(--crm-surface-soft, #f6f7f9); font-size:11px; }
.maintenance-notifications__item p { margin:0; white-space:pre-wrap; overflow-wrap:anywhere; line-height:1.5; font-size:13px; }
.maintenance-notifications__item footer, .maintenance-notifications__item small { color:var(--crm-text-muted, #667085); font-size:11px; overflow-wrap:anywhere; }
.maintenance-notifications__failed { color:var(--crm-danger, #b42318); }
</style>
