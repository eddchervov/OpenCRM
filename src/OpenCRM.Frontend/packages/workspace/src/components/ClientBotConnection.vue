<script setup lang="ts">
import { translate as t } from '@opencrm/ui/i18n'
import '../interactionMessages'
import { computed, ref } from 'vue'
import { UiButton } from '@opencrm/ui'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import ClientBotChannel from './ClientBotChannel.vue'

defineProps<{
  timeZoneId?: string | null
  organizationId: string
  clientId: string
  clientName: string
  canWrite: boolean
  isLocked?: boolean
}>()

const open = ref(false)
const maxBusy = ref(false)
const telegramBusy = ref(false)
const busy = computed(() => maxBusy.value || telegramBusy.value)
</script>

<template>
  <UiButton variant="secondary" @click="open = true">{{ isLocked ? t('interaction.botLinkPro') : t('interaction.botLink') }}</UiButton>
  <SchedulerBookingDialog :open="open" :busy="busy" :dismiss-on-backdrop="false" :title="t('interaction.botTitle')" @close="open = false">
    <section v-if="open" class="client-bot-connection" :aria-label="t('interaction.clientMessengers')">
      <div class="client-bot-connection__intro">
        <i18n-t v-if="isLocked" keypath="interaction.lockedInfo" tag="p" role="status"><template #pro><a href="#billing">{{ t('interaction.pro') }}</a></template></i18n-t>
        <strong>{{ clientName }}</strong>
        <p>{{ t('interaction.botHelp') }}</p>
      </div>
      <div class="client-bot-connection__channels">
        <ClientBotChannel :key="organizationId + ':' + clientId + ':telegram'" channel="telegram" :organization-id="organizationId" :time-zone-id="timeZoneId" :client-id="clientId" :can-write="canWrite" :is-locked="isLocked" @busy-changed="telegramBusy = $event" />
        <ClientBotChannel :key="organizationId + ':' + clientId + ':max'" channel="max" :organization-id="organizationId" :time-zone-id="timeZoneId" :client-id="clientId" :can-write="canWrite" :is-locked="isLocked" @busy-changed="maxBusy = $event" />
      </div>
      <footer class="client-bot-connection__footer"><UiButton variant="secondary" :disabled="busy" @click="open = false">{{ t('interaction.close') }}</UiButton></footer>
    </section>
  </SchedulerBookingDialog>
</template>

<style scoped>
:global(.scheduler-booking-dialog:has(.client-bot-connection)) { width: min(860px, calc(100vw - 32px)); }
.client-bot-connection { display: grid; gap: 20px; padding: 20px 24px; }
.client-bot-connection__intro { display: grid; gap: 6px; }
.client-bot-connection__intro p { margin: 0; color: var(--crm-text-muted, #667085); font-size: 13px; line-height: 1.5; }
.client-bot-connection__channels { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; align-items: start; }
.client-bot-connection__footer { display: flex; justify-content: flex-end; padding-top: 16px; border-top: 1px solid var(--crm-line, #dce2e9); }
@media (max-width: 650px) { .client-bot-connection { padding: 16px; gap: 16px; } .client-bot-connection__channels { grid-template-columns: minmax(0, 1fr); } }
</style>
