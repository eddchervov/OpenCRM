<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { getEmailComposeData, previewEmailRecipients, scheduleEmailCampaign } from '@/api/adminOperationsApi'
import { ApiError } from '@/api/ApiError'
import type { AdminEmailComposeData } from '@/models/AdminEmailComposeData'

const router = useRouter()
const data = ref<AdminEmailComposeData>({ organizationTypes: [], users: [] })
const targetMode = ref('email')
const email = ref('')
const userId = ref('')
const userSearch = ref('')
const organizationTypeCode = ref('')
const subject = ref('')
const body = ref('')
const bodyFormat = ref('text')
const intervalSeconds = ref(60)
const startDelayMinutes = ref(0)
const onlyConfirmedEmail = ref(false)
const onlyAdvertisingConsent = ref(false)
const recipients = ref<{ totalCount: number; items: Array<{ email: string; displayName: string }> } | null>(null)
const error = ref('')
const isSaving = ref(false)
const isSearching = ref(false)
const isPreviewing = ref(false)
const isSingleRecipient = computed(() => ['email', 'user'].includes(targetMode.value))
const htmlBody = computed(() => bodyFormat.value === 'html' ? body.value : body.value
  .split(/\n\s*\n/)
  .map(paragraph => '<p style="margin:0 0 16px;">' + escapeHtml(paragraph).replace(/\n/g, '<br>') + '</p>')
  .join(''))
const bodyPreview = computed(() => '<html lang="ru"><body style="font:16px/26px Arial,sans-serif;color:#334155;padding:24px;"><h1>' + escapeHtml(subject.value) + '</h1>' + htmlBody.value + '</body></html>')

onMounted(searchUsers)
watch([targetMode, email, userId, organizationTypeCode, onlyConfirmedEmail, onlyAdvertisingConsent], () => {
  recipients.value = null
})

function escapeHtml(value: string) {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;')
}

function target() {
  return {
    targetMode: targetMode.value,
    email: targetMode.value === 'email' ? email.value.trim() : null,
    userId: targetMode.value === 'user' ? userId.value : null,
    organizationTypeCode: targetMode.value === 'organizationType' ? organizationTypeCode.value : null,
    onlyConfirmedEmail: targetMode.value !== 'email' && onlyConfirmedEmail.value,
    onlyAdvertisingConsent: targetMode.value !== 'email' && onlyAdvertisingConsent.value
  }
}

async function searchUsers() {
  isSearching.value = true
  error.value = ''
  try {
    data.value = await getEmailComposeData(userSearch.value)
    if (!data.value.users.some(user => user.id === userId.value)) userId.value = ''
  } catch {
    error.value = 'Не удалось загрузить пользователей.'
  } finally {
    isSearching.value = false
  }
}

async function preview() {
  error.value = ''
  isPreviewing.value = true
  try {
    const selection = target()
    const result = await previewEmailRecipients(selection)
    if (JSON.stringify(selection) === JSON.stringify(target())) recipients.value = result
  } catch (failure) {
    error.value = failure instanceof ApiError ? failure.message : 'Не удалось проверить получателей.'
  } finally {
    isPreviewing.value = false
  }
}

async function send() {
  if (isSaving.value || !recipients.value?.totalCount) return
  isSaving.value = true
  error.value = ''
  try {
    await scheduleEmailCampaign({
      ...target(), subject: subject.value.trim(), htmlBody: htmlBody.value,
      intervalSeconds: intervalSeconds.value, startDelayMinutes: startDelayMinutes.value
    })
    await router.push('/communications/email-campaigns')
  } catch (failure) {
    error.value = failure instanceof ApiError ? failure.message : 'Не удалось поставить письмо в очередь.'
  } finally {
    isSaving.value = false
  }
}
</script>

<template>
  <section class="operations-page operation-form-page">
    <h1>Написать письмо</h1>
    <form class="standalone-form" @submit.prevent="send">
      <div v-if="error" class="form-message form-message--error" role="alert">{{ error }}</div>
      <label>
        <span>Получатель</span>
        <SearchSelect v-model="targetMode" :options="[
          { value: 'email', label: 'Указать email' },
          { value: 'user', label: 'Выбрать пользователя' },
          { value: 'organizationType', label: 'Тип организации' },
          { value: 'all', label: 'Все пользователи' }
        ]" />
      </label>
      <label v-if="targetMode === 'email'">
        <span>Email получателя</span>
        <input v-model="email" type="email" maxlength="320" placeholder="contact@example.invalid" required>
        <small>Можно отправить человеку, который ещё не зарегистрирован.</small>
      </label>
      <template v-if="targetMode === 'user'">
        <div class="operations-toolbar">
          <input v-model="userSearch" type="search" aria-label="Поиск пользователя" placeholder="Email, имя или телефон" @keydown.enter.prevent="searchUsers">
          <button class="secondary-button" type="button" :disabled="isSearching" @click="searchUsers">{{ isSearching ? 'Поиск…' : 'Найти пользователя' }}</button>
        </div>
        <label>
          <span>Пользователь</span>
          <SearchSelect v-model="userId" :options="data.users.map(user => ({ value: user.id, label: user.email }))" placeholder="Выберите пользователя" />
          <small>Поиск работает по всей базе. Показано до 30 совпадений.</small>
        </label>
      </template>
      <label v-if="targetMode === 'organizationType'">
        <span>Тип организации</span>
        <SearchSelect v-model="organizationTypeCode" :options="data.organizationTypes.map(type => ({ value: type.code, label: type.name }))" placeholder="Выберите тип" />
      </label>
      <div v-if="targetMode !== 'email'" class="inline-checks">
        <label><input v-model="onlyConfirmedEmail" type="checkbox"> Только подтверждённые email</label>
        <label><input v-model="onlyAdvertisingConsent" type="checkbox"> Только с согласием на рассылку</label>
      </div>
      <div class="recipient-preview">
        <button class="secondary-button" type="button" :disabled="isPreviewing" @click="preview">Проверить получателей</button>
        <div v-if="recipients" aria-live="polite">
          <strong>Получателей: {{ recipients.totalCount }}</strong>
          <ul><li v-for="recipient in recipients.items" :key="recipient.email">{{ recipient.email }}</li></ul>
          <small v-if="recipients.totalCount > recipients.items.length">Показаны первые {{ recipients.items.length }} адресов.</small>
        </div>
      </div>
      <label><span>Тема</span><input v-model="subject" maxlength="300" required></label>
      <label><span>Формат текста</span><SearchSelect v-model="bodyFormat" :options="[{ value: 'text', label: 'Обычный текст' }, { value: 'html', label: 'HTML' }]" /></label>
      <label><span>Текст письма</span><textarea v-model="body" rows="10" required></textarea></label>
      <details v-if="body">
        <summary>Предпросмотр текста</summary>
        <iframe class="email-preview" sandbox="" referrerpolicy="no-referrer" title="Предпросмотр текста письма" :srcdoc="bodyPreview"></iframe>
        <small>При отправке сервис добавит фирменное оформление.</small>
      </details>
      <div class="modal-grid">
        <label v-if="!isSingleRecipient"><span>Интервал между письмами, секунд</span><input v-model.number="intervalSeconds" type="number" min="5" max="86400" required></label>
        <label><span>Отправить через, минут</span><input v-model.number="startDelayMinutes" type="number" min="0" max="1440" required><small>0 — поставить в очередь сразу.</small></label>
      </div>
      <div class="modal-actions">
        <button class="primary-button" :disabled="isSaving || !recipients?.totalCount">{{ isSaving ? 'Сохранение…' : startDelayMinutes > 0 ? 'Запланировать' : 'Отправить' }}</button>
        <button class="secondary-button" type="button" @click="router.back()">Отмена</button>
      </div>
    </form>
  </section>
</template>
