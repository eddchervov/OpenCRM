<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { UiButton, useLocalization } from '@opencrm/ui'
import '../notificationMessages'
import WorkspaceVisitReminderSettings from './WorkspaceVisitReminderSettings.vue'
import WorkspaceTelegramSettings from './WorkspaceTelegramSettings.vue'
import WorkspaceMaxSettings from './WorkspaceMaxSettings.vue'
import WorkspaceSettingsChoices from './WorkspaceSettingsChoices.vue'

defineProps<{
  organizationId: string
  canWrite: boolean
  previewOnly?: boolean
}>()

const emit = defineEmits<{
  'busy-changed': [isBusy: boolean]
}>()
const { t } = useLocalization()

type NotificationSection = 'visitReminder' | 'telegram' | 'max'
type NotificationGroup = { title: string, items: { code: NotificationSection, title: string, description: string, symbol: string }[] }
const selectedSection = ref<NotificationSection | null>(null)
const visited = ref<Partial<Record<NotificationSection, boolean>>>({})
const busy = ref(false)
const overview = ref<InstanceType<typeof WorkspaceSettingsChoices> | null>(null)
const backButton = ref<InstanceType<typeof UiButton> | null>(null)
const groups = computed<NotificationGroup[]>(() => [
  { title: t('notificationSettings.automation'), items: [
    { code: 'visitReminder' as const, title: t('notificationSettings.visitReminder'), description: t('notificationSettings.visitReminderDescription'), symbol: 'clock' }
  ] },
  { title: t('notificationSettings.channels'), items: [
    { code: 'telegram' as const, title: t('notificationChannel.telegram'), description: t('notificationSettings.telegramDescription'), symbol: 'send' },
    { code: 'max' as const, title: t('notificationChannel.max'), description: t('notificationSettings.maxDescription'), symbol: 'chat' }
  ] }
])
const selectedTitle = computed(() => groups.value.flatMap(group => group.items).find(item => item.code === selectedSection.value)?.title)

async function selectSection(section: NotificationSection | null) {
  if (busy.value) return
  const previousSection = selectedSection.value
  selectedSection.value = section
  if (section) visited.value[section] = true
  await nextTick()
  if (section) {
    backButton.value?.$el.focus()
    return
  }
  overview.value?.$el.querySelector(`[data-section="${previousSection}"]`)?.focus()
}

function setBusy(value: boolean) {
  busy.value = value
  emit('busy-changed', value)
}
</script>

<template>
  <div class="notification-settings">
    <template v-if="previewOnly">
      <WorkspaceSettingsChoices :title="t('notificationSettings.title')" :groups="groups" disabled />
    </template>
    <template v-else>
      <WorkspaceSettingsChoices v-show="!selectedSection" ref="overview" :title="t('notificationSettings.title')" :groups="groups" :disabled="busy" @select="selectSection($event as NotificationSection)" />
      <div v-show="selectedSection" class="notification-settings__editor">
        <nav class="notification-settings__back" :aria-label="t('notificationSettings.backAria')">
          <UiButton ref="backButton" variant="secondary" :disabled="busy" @click="selectSection(null)">{{ t('notificationSettings.backAll') }}</UiButton>
          <span aria-current="page">{{ selectedTitle }}</span>
        </nav>
        <WorkspaceVisitReminderSettings v-if="visited.visitReminder" v-show="selectedSection === 'visitReminder'" :organization-id="organizationId" :can-write="canWrite" @busy-changed="setBusy" />
        <WorkspaceTelegramSettings v-if="visited.telegram" v-show="selectedSection === 'telegram'" :organization-id="organizationId" :can-write="canWrite" @busy-changed="setBusy" />
        <WorkspaceMaxSettings v-if="visited.max" v-show="selectedSection === 'max'" :organization-id="organizationId" :can-write="canWrite" @busy-changed="setBusy" />
      </div>
    </template>
  </div>
</template>

<style scoped>
.notification-settings, .notification-settings__editor { display: grid; gap: 14px; min-width: 0; }
.notification-settings__back { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; }
.notification-settings__back > span { color: var(--crm-text-muted, #687386); font-size: 12px; }
@media (max-width: 600px) {
  .notification-settings__back > span { display: none; }
}
</style>
