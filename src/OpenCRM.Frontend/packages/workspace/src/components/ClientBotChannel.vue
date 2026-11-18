<script setup lang="ts">
import { useLocalization } from '@opencrm/ui/i18n'
import '../notificationMessages'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton } from '@opencrm/ui'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import { createMaxClientLink, disconnectMaxClient, loadMaxClientStatus, type MaxClientStatus } from '../api/maxNotifications'
import { createTelegramClientLink, disconnectTelegramClient, loadTelegramClientStatus } from '../api/telegramNotifications'
import MaxNotificationHistory from './MaxNotificationHistory.vue'
import TelegramNotificationHistory from './TelegramNotificationHistory.vue'

const { t } = useLocalization()

const props = defineProps<{
  channel: 'telegram' | 'max'
  timeZoneId?: string | null
  organizationId: string
  clientId: string
  canWrite: boolean
  isLocked?: boolean
}>()

const emit = defineEmits<{ 'busy-changed': [busy: boolean] }>()
const status = ref<MaxClientStatus | null>(null)
const link = ref('')
const error = ref('')
const copied = ref(false)
const loading = ref(false)
const saving = ref(false)
const confirmDisconnect = ref(false)
let generation = 0

const channelName = computed(() => props.channel === 'telegram' ? 'Telegram' : 'MAX')
const busy = computed(() => loading.value || saving.value)
const canChange = computed(() => props.canWrite && !props.isLocked && status.value?.isBotConnected && !busy.value)
const canDisconnect = computed(() => props.canWrite && !props.isLocked && status.value?.isClientConnected && !busy.value)
const statusLabel = computed(() => !status.value ? t('notifications.notLoaded')
  : status.value.isClientConnected && !status.value.isBotConnected ? t('notifications.connectedBotOff')
  : !status.value.isBotConnected ? t('notifications.botNotConnected')
  : status.value.isClientConnected ? t('notifications.clientConnected') : t('notifications.awaitingConnection'))

watch(busy, value => emit('busy-changed', value), { immediate: true, flush: 'sync' })

async function loadStatus() {
  if (busy.value) return
  const current = generation
  loading.value = true
  error.value = ''
  try {
    const loaded = props.channel === 'telegram'
      ? await loadTelegramClientStatus(props.organizationId, props.clientId)
      : await loadMaxClientStatus(props.organizationId, props.clientId)
    if (current !== generation) return
    status.value = loaded
    if (loaded.isClientConnected || !loaded.isBotConnected) link.value = ''
  } catch (reason) {
    if (current === generation) error.value = reason instanceof Error ? reason.message : t('notifications.connectionError')
  } finally {
    if (current === generation) loading.value = false
  }
}

async function createLink() {
  if (!canChange.value) return
  const current = generation
  saving.value = true
  error.value = ''
  copied.value = false
  confirmDisconnect.value = false
  try {
    const result = props.channel === 'telegram'
      ? await createTelegramClientLink(props.organizationId, props.clientId)
      : await createMaxClientLink(props.organizationId, props.clientId)
    if (current === generation) link.value = result.url
  } catch (reason) {
    if (current === generation) error.value = reason instanceof Error ? reason.message : t('notifications.linkError')
  } finally {
    if (current === generation) saving.value = false
  }
}

async function disconnect() {
  if (!canDisconnect.value) return
  if (!confirmDisconnect.value) {
    error.value = ''
    confirmDisconnect.value = true
    return
  }
  const current = generation
  saving.value = true
  error.value = ''
  try {
    if (props.channel === 'telegram') await disconnectTelegramClient(props.organizationId, props.clientId)
    else await disconnectMaxClient(props.organizationId, props.clientId)
    if (current !== generation) return
    status.value = { ...status.value!, isClientConnected: false }
    link.value = ''
    copied.value = false
    confirmDisconnect.value = false
  } catch (reason) {
    if (current === generation) error.value = reason instanceof Error ? reason.message : t('notifications.disconnectError')
  } finally {
    if (current === generation) saving.value = false
  }
}

function closeDisconnectConfirmation() {
  if (!busy.value) confirmDisconnect.value = false
}

async function copyLink() {
  if (!link.value || busy.value) return
  const current = generation
  const copiedLink = link.value
  copied.value = false
  try {
    await navigator.clipboard.writeText(copiedLink)
    if (current === generation && copiedLink === link.value) copied.value = true
  } catch {
    if (current === generation) error.value = t('notifications.copyError')
  }
}

watch(() => [props.organizationId, props.clientId, props.channel], () => {
  generation++
  status.value = null
  link.value = ''
  copied.value = false
  confirmDisconnect.value = false
  loading.value = false
  saving.value = false
  void loadStatus()
}, { immediate: true })
onBeforeUnmount(() => { generation++; emit('busy-changed', false) })
</script>

<template>
  <section class="client-bot-channel" :aria-label="channelName" :aria-busy="busy">
    <header class="client-bot-channel__header ui-section-header">
      <h3>{{ channelName }}</h3>
      <span class="client-bot-channel__status" :class="{ 'is-connected': status?.isBotConnected && status.isClientConnected }">{{ loading ? t('notifications.checking') : statusLabel }}</span>
    </header>
    <div class="client-bot-channel__body">
      <p v-if="status?.botUsername" class="client-bot-channel__username">@{{ status.botUsername }}</p>
      <p v-if="status && !status.isBotConnected">{{ status.isClientConnected ? t('notifications.botOffHint') : t('notifications.connectBotHint') }}</p>
      <p v-else-if="status?.isClientConnected">{{ t('notifications.connectedHint') }}</p>
      <p v-else-if="status">{{ t('notifications.createLinkHint', { channel: channelName }) }}</p>
      <p v-if="!canWrite" class="client-bot-channel__muted">{{ t('notifications.readOnly') }}</p>
      <p v-if="isLocked" class="client-bot-channel__muted">{{ t('notifications.pro') }}</p>
      <UiButton v-if="canWrite && status?.isBotConnected" :disabled="!canChange" @click="createLink">{{ saving ? t('notifications.working') : link || status.isClientConnected ? t('notifications.newLink') : t('notifications.createLink') }}</UiButton>
      <small v-if="canWrite && status?.isBotConnected" class="client-bot-channel__muted">{{ t('notifications.replaceLinkHint') }}</small>
      <div v-if="link" class="client-bot-channel__link">
        <label class="ui-field"><span>{{ t('notifications.clientLink') }}</span><input :value="link" :aria-label="t('notifications.clientChannelLink', { channel: channelName })" readonly /></label>
        <div class="client-bot-channel__actions">
          <UiButton variant="secondary" :disabled="busy" @click="copyLink">{{ copied ? t('notifications.copied') : t('notifications.copy') }}</UiButton>
          <UiButton variant="secondary" :href="link" target="_blank" rel="noopener noreferrer">{{ t('notifications.openBot') }}</UiButton>
        </div>
        <small>{{ t('notifications.linkExpiry') }}</small>
        <span v-if="copied" class="client-bot-channel__muted" role="status">{{ t('notifications.linkCopied') }}</span>
      </div>
      <p v-if="error && !confirmDisconnect" class="client-bot-channel__error" role="alert">{{ error }}</p>
      <UiButton variant="secondary" :disabled="busy" @click="loadStatus">{{ loading ? t('notifications.checking') : t('notifications.checkConnection') }}</UiButton>
      <div v-if="canWrite && status?.isClientConnected" class="client-bot-channel__disconnect">
        <UiButton variant="ghost" :disabled="!canDisconnect" @click="disconnect">{{ t('notifications.disconnectClient') }}</UiButton>
      </div>
      <details v-if="status?.recentNotifications.length" class="client-bot-channel__history">
        <summary>{{ t('notifications.notificationHistory') }}</summary>
        <TelegramNotificationHistory v-if="channel === 'telegram'" :notifications="status.recentNotifications" :time-zone-id="timeZoneId" />
        <MaxNotificationHistory v-else :notifications="status.recentNotifications" :time-zone-id="timeZoneId" />
      </details>
    </div>
  </section>
  <SchedulerBookingDialog :open="confirmDisconnect" :title="t('notifications.disconnectTitle', { channel: channelName })" :busy="busy" :dismiss-on-backdrop="false" @close="closeDisconnectConfirmation">
    <div class="client-bot-disconnect">
      <p>{{ t('notifications.disconnectHint') }}</p>
      <p v-if="error" class="client-bot-channel__error" role="alert">{{ error }}</p>
      <footer>
        <UiButton variant="secondary" :disabled="busy" @click="closeDisconnectConfirmation">{{ t('notifications.cancel') }}</UiButton>
        <UiButton :disabled="!canDisconnect" @click="disconnect">{{ saving ? t('notifications.disconnecting') : t('notifications.disconnect') }}</UiButton>
      </footer>
    </div>
  </SchedulerBookingDialog>
</template>

<style scoped>
.client-bot-channel { min-width: 0; overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; }
.client-bot-channel__header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; padding: 14px 16px; border-bottom: 1px solid var(--crm-line, #dce2e9); }
.client-bot-channel__header h3 { margin: 0; font-size: 15px; }
.client-bot-channel__status { color: var(--crm-text-muted, #667085); font-size: 11px; }
.client-bot-channel__status.is-connected { color: var(--crm-success-text, #246247); }
.client-bot-channel__body { display: grid; gap: 12px; padding: 16px; }
.client-bot-channel__body p { margin: 0; font-size: 13px; line-height: 1.5; overflow-wrap: anywhere; }
.client-bot-channel__username, .client-bot-channel__muted, .client-bot-channel__link small { color: var(--crm-text-muted, #667085); font-size: 12px; }
.client-bot-channel__link { display: grid; gap: 10px; padding: 12px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; background: var(--crm-surface-soft, #f5f6f8); }
.client-bot-channel__link input { min-width: 0; width: 100%; box-sizing: border-box; font-size: 12px; }
.client-bot-channel__actions { display: flex; flex-wrap: wrap; gap: 8px; }
.client-bot-channel__error { color: var(--crm-danger, #b42318); }
.client-bot-channel__disconnect, .client-bot-channel__history { padding-top: 12px; border-top: 1px solid var(--crm-line, #dce2e9); }
.client-bot-channel__disconnect { display: grid; gap: 10px; }
.client-bot-channel__history summary { font-size: 12px; cursor: pointer; }
:global(.scheduler-booking-dialog:has(.client-bot-disconnect)) { width: min(480px, calc(100vw - 32px)); height: fit-content; max-height: calc(100dvh - 32px); border: 1px solid var(--crm-line, #dce2e9); border-radius: 12px; }
.client-bot-disconnect { display: grid; gap: 16px; padding: 20px 24px; }
.client-bot-disconnect p { margin: 0; font-size: 14px; line-height: 1.5; }
.client-bot-disconnect footer { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 8px; padding-top: 16px; border-top: 1px solid var(--crm-line, #dce2e9); }
</style>
