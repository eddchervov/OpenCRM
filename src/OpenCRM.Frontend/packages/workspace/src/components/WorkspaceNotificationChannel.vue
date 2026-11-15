<script setup lang="ts">
import { getProjectBrand } from '@opencrm/ui'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiInput } from '@opencrm/ui'
import { loadMaxBotSettings, saveMaxBotSettings, type MaxBotSettings } from '../api/maxNotifications'
import { loadTelegramBotSettings, saveTelegramBotSettings, type TelegramBotSettings } from '../api/telegramNotifications'

const projectBrand = getProjectBrand()








type NotificationSettings = {
  botUsername?: string | null
  isConfigured: boolean
  isEnabled: boolean
  isAvailable?: boolean
  notifyBooking: boolean
  notifyReady: boolean
}

const props = defineProps<{
  channel: 'telegram' | 'max'
  organizationId: string
  canWrite: boolean
}>()

const emit = defineEmits<{
  'busy-changed': [isBusy: boolean]
}>()

const settings = ref<NotificationSettings | null>(null)
const token = ref('')
const error = ref('')
const saved = ref(false)
const isLoading = ref(false)
const isSaving = ref(false)
let requestVersion = 0

const channelName = computed(() => props.channel === 'telegram' ? 'Telegram' : 'MAX')
const helpUrl = computed(() => props.channel === 'telegram'
  ? 'https://t.me/BotFather'
  : 'https://dev.max.ru/docs/chatbots/bots-create/create')
const helpLabel = computed(() => props.channel === 'telegram' ? 'BotFather' : 'инструкции MAX')
const isAvailable = computed(() => props.channel !== 'telegram' || settings.value?.isAvailable !== false)
const statusLabel = computed(() => {
  if (!settings.value) {
    return error.value && !isLoading.value ? 'Ошибка загрузки' : 'Загрузка'
  }
  if (!isAvailable.value) {
    return 'Недоступен'
  }
  if (!settings.value.isConfigured) {
    return 'Не подключён'
  }

  return settings.value.isEnabled ? 'Настроен' : 'Выключен'
})
const isBusy = computed(() => isLoading.value || isSaving.value)
const canSave = computed(() => Boolean(settings.value)
  && props.canWrite
  && !isBusy.value
  && isAvailable.value
  && (settings.value?.isConfigured || Boolean(token.value.trim())))

watch(isSaving, value => emit('busy-changed', value), { immediate: true })
watch([
  token,
  () => settings.value?.isEnabled,
  () => settings.value?.notifyBooking,
  () => settings.value?.notifyReady
], () => {
  if (!isBusy.value) {
    saved.value = false
  }
}, { flush: 'sync' })

function normalizeSettings(value: TelegramBotSettings | MaxBotSettings): NotificationSettings {
  return { ...value }
}

async function loadSettings() {
  const currentVersion = ++requestVersion
  const organizationId = props.organizationId
  const channel = props.channel
  isLoading.value = true
  settings.value = null
  error.value = ''
  saved.value = false

  try {
    const loaded = channel === 'telegram'
      ? await loadTelegramBotSettings(organizationId)
      : await loadMaxBotSettings(organizationId)

    if (currentVersion !== requestVersion || organizationId !== props.organizationId || channel !== props.channel) {
      return
    }

    settings.value = normalizeSettings(loaded)
  } catch (reason) {
    if (currentVersion === requestVersion && organizationId === props.organizationId && channel === props.channel) {
      error.value = reason instanceof Error ? reason.message : `Не удалось загрузить настройки ${channelName.value}.`
    }
  } finally {
    if (currentVersion === requestVersion && organizationId === props.organizationId && channel === props.channel) {
      isLoading.value = false
    }
  }
}

watch(() => [props.organizationId, props.channel], () => {
  token.value = ''
  isSaving.value = false
  void loadSettings()
}, { immediate: true })

onBeforeUnmount(() => {
  requestVersion++
  emit('busy-changed', false)
})

async function save() {
  if (!settings.value || !props.canWrite || isBusy.value || !isAvailable.value
    || (!settings.value.isConfigured && !token.value.trim())) {
    return
  }

  const organizationId = props.organizationId
  const channel = props.channel
  const currentVersion = requestVersion
  const draft = settings.value
  isSaving.value = true
  error.value = ''
  saved.value = false

  try {
    const payload = {
      token: token.value.trim() || null,
      isEnabled: draft.isEnabled,
      notifyBooking: draft.notifyBooking,
      notifyReady: draft.notifyReady
    }
    const updated = channel === 'telegram'
      ? await saveTelegramBotSettings(organizationId, payload)
      : await saveMaxBotSettings(organizationId, payload)

    if (currentVersion !== requestVersion || organizationId !== props.organizationId || channel !== props.channel) {
      return
    }

    settings.value = normalizeSettings(updated)
    token.value = ''
    saved.value = true
  } catch (reason) {
    if (currentVersion === requestVersion && organizationId === props.organizationId && channel === props.channel) {
      error.value = reason instanceof Error ? reason.message : `Не удалось сохранить настройки ${channelName.value}.`
    }
  } finally {
    if (currentVersion === requestVersion && organizationId === props.organizationId && channel === props.channel) {
      isSaving.value = false
    }
  }
}
</script>

<template>
  <section class="workspace-notification-channel" :aria-labelledby="`notification-${channel}-title`">
    <header class="workspace-notification-channel__header ui-section-header">
      <h3 :id="`notification-${channel}-title`">{{ channelName }}</h3>
      <span class="workspace-notification-channel__badge" :class="{
        'is-configured': statusLabel === 'Настроен',
        'is-unavailable': statusLabel === 'Недоступен'
      }">{{ statusLabel }}</span>
    </header>

    <p v-if="settings?.botUsername" class="workspace-notification-channel__bot">Бот: <strong>@{{ settings.botUsername }}</strong></p>
    <p v-if="!canWrite" class="workspace-notification-channel__readonly">Только просмотр</p>
    <p v-if="error" class="workspace-notification-channel__message is-error" role="alert">{{ error }}</p>
    <UiButton v-if="error && !settings" type="button" variant="secondary" :disabled="isBusy" @click="loadSettings">
      Повторить загрузку
    </UiButton>
    <p v-if="saved" class="workspace-notification-channel__message is-success" role="status">Настройки сохранены.</p>
    <p v-if="settings && !isAvailable" class="workspace-notification-channel__readonly">Канал пока недоступен в {{ projectBrand }}.</p>

    <form v-if="settings" class="workspace-notification-channel__form" @submit.prevent="save">
      <label class="workspace-notification-channel__toggle">
        <input v-model="settings.isEnabled" type="checkbox" :disabled="!canWrite || isBusy || !isAvailable || (!settings.isConfigured && !token.trim())" />
        <span>Включить уведомления</span>
      </label>

      <div class="workspace-notification-channel__events">
        <label>
          <input v-model="settings.notifyBooking" type="checkbox" :disabled="!canWrite || isBusy || !isAvailable" />
          <span>Запись, перенос и отмена</span>
        </label>
        <label>
          <input v-model="settings.notifyReady" type="checkbox" :disabled="!canWrite || isBusy || !isAvailable" />
          <span>Автомобиль готов к выдаче</span>
        </label>
      </div>

      <details class="workspace-notification-channel__connection" :open="!settings.isConfigured">
        <summary>{{ settings.isConfigured ? 'Изменить подключение' : 'Подключить бота' }}</summary>
        <div class="workspace-notification-channel__connection-content">
          <p>Создайте бота через <a :href="helpUrl" target="_blank" rel="noopener noreferrer">{{ helpLabel }}</a>, затем вставьте токен. Сохранённый токен не отображается.</p>
          <UiInput
            v-model="token"
            type="password"
            autocomplete="new-password"
            :label="settings.isConfigured ? 'Новый токен (необязательно)' : 'Токен бота'"
            :placeholder="settings.isConfigured ? 'Оставьте пустым, чтобы не менять токен' : ''"
            :disabled="!canWrite || isBusy || !isAvailable"
            :required="!settings.isConfigured"
          />
        </div>
      </details>

      <footer v-if="canWrite" class="workspace-notification-channel__actions">
        <UiButton type="submit" :disabled="!canSave">
          {{ isSaving ? 'Сохраняем…' : 'Сохранить настройки' }}
        </UiButton>
      </footer>
    </form>
  </section>
</template>

<style scoped>
.workspace-notification-channel {
  display: grid;
  gap: 10px;
  padding: 14px 16px;
  border: 1px solid var(--crm-line, #d6dce5);
  border-radius: 10px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #202733);
}

.workspace-notification-channel__header,
.workspace-notification-channel__actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.workspace-notification-channel h3,
.workspace-notification-channel p {
  margin: 0;
}

.workspace-notification-channel h3 {
  font-size: 1rem;
  font-weight: 650;
}

.workspace-notification-channel__badge {
  padding: 3px 8px;
  border: 1px solid var(--crm-line, #d6dce5);
  border-radius: 999px;
  color: var(--crm-text-muted, #687386);
  font-size: .75rem;
  white-space: nowrap;
}

.workspace-notification-channel__badge.is-configured {
  border-color: var(--crm-success-line, #9ccbb5);
  color: var(--crm-success-text, #246247);
}

.workspace-notification-channel__badge.is-unavailable,
.workspace-notification-channel__message.is-error {
  color: var(--crm-danger-text, #9b352e);
}

.workspace-notification-channel__bot,
.workspace-notification-channel__readonly,
.workspace-notification-channel__connection-content p {
  color: var(--crm-text-muted, #687386);
  font-size: .875rem;
}

.workspace-notification-channel__form,
.workspace-notification-channel__events,
.workspace-notification-channel__connection-content {
  display: grid;
  gap: 10px;
}

.workspace-notification-channel__toggle,
.workspace-notification-channel__events label {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.workspace-notification-channel__toggle {
  font-weight: 600;
  width: fit-content;
}

.workspace-notification-channel__events {
  padding-left: 22px;
}

.workspace-notification-channel__events label {
  width: fit-content;
}

.workspace-notification-channel__connection {
  border-top: 1px solid var(--crm-line, #d6dce5);
  padding-top: 10px;
}

.workspace-notification-channel__connection summary {
  color: var(--crm-text-muted, #687386);
  cursor: pointer;
  font-size: .875rem;
}

.workspace-notification-channel__connection-content {
  padding-top: 10px;
}

.workspace-notification-channel__actions {
  justify-content: flex-end;
}

.workspace-notification-channel input[type="checkbox"] {
  accent-color: #e87532;
}

.workspace-notification-channel__actions :deep(.ui-button--primary) {
  background: #e87532;
  border-color: #e87532;
  color: #fff;
}

@media (max-width: 520px) {
  .workspace-notification-channel {
    padding: 12px;
  }

  .workspace-notification-channel__actions :deep(.ui-button) {
    width: 100%;
  }
}
</style>
