<script setup lang="ts">
import { useLocalization, formattingCulture } from '@opencrm/ui/i18n'
import '../notificationMessages'
import { computed } from 'vue'
import type { TelegramNotificationRecord } from '../api/telegramNotifications'

const { t } = useLocalization()

const props = defineProps<{ timeZoneId?: string | null; notifications: TelegramNotificationRecord[] }>()

const eventLabels = computed<Record<string, string>>(() => ({
  booking_created: t('notifications.bookingCreated'),
  booking_changed: t('notifications.bookingChanged'),
  booking_cancelled: t('notifications.bookingCancelled'),
  ready: t('notifications.vehicleReady')
}))

const statusLabels = computed<Record<string, string>>(() => ({
  queued: t('notifications.awaitingSend'),
  processing: t('notifications.processing'),
  retrying: t('notifications.retrying'),
  sent: t('notifications.acceptedTelegram'),
  simulated: t('notifications.testWithoutSending'),
  failed: t('notifications.sendError'),
  skipped: t('notifications.skippedChanged')
}))
</script>

<template>
  <div v-if="notifications.length" class="notification-history">
    <div v-for="item in notifications" :key="item.id" class="notification-history__item">
      <strong>Telegram · {{ eventLabels[item.eventCode] ?? item.eventCode }}</strong>
      <span>{{ statusLabels[item.status] ?? item.status }} · {{ new Date(item.sentAt ?? item.createdAt).toLocaleString(formattingCulture, { timeZone: props.timeZoneId || 'UTC' }) }}</span>
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
