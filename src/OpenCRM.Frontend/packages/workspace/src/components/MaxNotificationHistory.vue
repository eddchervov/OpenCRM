<script setup lang="ts">
import type { MaxNotificationRecord } from '../api/maxNotifications'

const props = defineProps<{ timeZoneId?: string | null; notifications: MaxNotificationRecord[] }>()

const eventLabels: Record<string, string> = {
  booking_created: 'Запись создана',
  booking_changed: 'Запись перенесена',
  booking_cancelled: 'Запись отменена',
  ready: 'Автомобиль готов'
}

const statusLabels: Record<string, string> = {
  queued: 'Ожидает отправки',
  processing: 'Отправляется',
  retrying: 'Повторная попытка',
  sent: 'Принято MAX',
  simulated: 'Проверка без отправки',
  failed: 'Ошибка отправки',
  skipped: 'Не отправлено: данные изменились'
}
</script>

<template>
  <div v-if="notifications.length" class="notification-history">
    <div v-for="item in notifications" :key="item.id" class="notification-history__item">
      <strong>MAX · {{ eventLabels[item.eventCode] ?? item.eventCode }}</strong>
      <span>{{ statusLabels[item.status] ?? item.status }} · {{ new Date(item.sentAt ?? item.createdAt).toLocaleString('ru-RU', { timeZone: props.timeZoneId || 'UTC' }) }}</span>
      <p>{{ item.body }}</p>
    </div>
  </div>
</template>

<style scoped>
.notification-history { display: grid; gap: 8px; }
.notification-history__item { display: grid; gap: 3px; padding: 8px 0; border-bottom: 1px solid var(--crm-line, #d0d5dd); }
.notification-history__item:last-child { border-bottom: 0; }
.notification-history__item span { color: var(--crm-muted, #667085); font-size: .875rem; }
.notification-history__item p { margin: 0; overflow-wrap: anywhere; }
</style>
