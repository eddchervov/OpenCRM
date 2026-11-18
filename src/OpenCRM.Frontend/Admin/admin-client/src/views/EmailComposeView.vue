<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { getEmailComposeData, previewEmailRecipients, scheduleEmailCampaign } from '@/api/adminOperationsApi'
import { ApiError } from '@/api/ApiError'
import type { AdminEmailComposeData } from '@/models/AdminEmailComposeData'
import { useLocalization } from '@opencrm/ui/i18n'
import '../adminOperationsMessages'

const { t, locale } = useLocalization()
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
const bodyPreview = computed(() => '<html lang="' + locale.value + '"><body style="font:16px/26px Arial,sans-serif;color:#334155;padding:24px;"><h1>' + escapeHtml(subject.value) + '</h1>' + htmlBody.value + '</body></html>')

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
    error.value = t('adminOperations.usersLoadError')
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
    error.value = failure instanceof ApiError ? failure.message : t('adminOperations.recipientsPreviewError')
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
    error.value = failure instanceof ApiError ? failure.message : t('adminOperations.emailQueueError')
  } finally {
    isSaving.value = false
  }
}
</script>

<template>
  <section class="operations-page operation-form-page">
    <h1>{{ t('adminOperations.emailCompose') }}</h1>
    <form class="standalone-form" @submit.prevent="send">
      <div v-if="error" class="form-message form-message--error" role="alert">{{ error }}</div>
      <label>
        <span>{{ t('adminOperations.recipient') }}</span>
        <SearchSelect v-model="targetMode" :options="[
          { value: 'email', label: t('adminOperations.specifyEmail') },
          { value: 'user', label: t('adminOperations.chooseUser') },
          { value: 'organizationType', label: t('adminOperations.organizationType') },
          { value: 'all', label: t('adminOperations.allUsers') }
        ]" />
      </label>
      <label v-if="targetMode === 'email'">
        <span>{{ t('adminOperations.recipientEmail') }}</span>
        <input v-model="email" type="email" maxlength="320" placeholder="contact@example.invalid" required>
        <small>{{ t('adminOperations.unregisteredRecipient') }}</small>
      </label>
      <template v-if="targetMode === 'user'">
        <div class="operations-toolbar">
          <input v-model="userSearch" type="search" :aria-label="t('adminOperations.searchUser')" :placeholder="t('adminOperations.searchByEmailNamePhone')" @keydown.enter.prevent="searchUsers">
          <button class="secondary-button" type="button" :disabled="isSearching" @click="searchUsers">{{ isSearching ? t('adminOperations.searching') : t('adminOperations.findUser') }}</button>
        </div>
        <label>
          <span>{{ t('adminOperations.user') }}</span>
          <SearchSelect v-model="userId" :options="data.users.map(user => ({ value: user.id, label: user.email }))" :placeholder="t('adminOperations.chooseUser')" />
          <small>{{ t('adminOperations.userSearchLimit') }}</small>
        </label>
      </template>
      <label v-if="targetMode === 'organizationType'">
        <span>{{ t('adminOperations.organizationType') }}</span>
        <SearchSelect v-model="organizationTypeCode" :options="data.organizationTypes.map(type => ({ value: type.code, label: type.name }))" :placeholder="t('adminOperations.chooseType')" />
      </label>
      <div v-if="targetMode !== 'email'" class="inline-checks">
        <label><input v-model="onlyConfirmedEmail" type="checkbox"> {{ t('adminOperations.confirmedEmailOnly') }}</label>
        <label><input v-model="onlyAdvertisingConsent" type="checkbox"> {{ t('adminOperations.advertisingConsentOnly') }}</label>
      </div>
      <div class="recipient-preview">
        <button class="secondary-button" type="button" :disabled="isPreviewing" @click="preview">{{ t('adminOperations.previewRecipients') }}</button>
        <div v-if="recipients" aria-live="polite">
          <strong>{{ t('adminOperations.recipientCount', { count: recipients.totalCount }) }}</strong>
          <ul><li v-for="recipient in recipients.items" :key="recipient.email">{{ recipient.email }}</li></ul>
          <small v-if="recipients.totalCount > recipients.items.length">{{ t('adminOperations.firstAddresses', { count: recipients.items.length }) }}</small>
        </div>
      </div>
      <label><span>{{ t('adminOperations.subject') }}</span><input v-model="subject" maxlength="300" required></label>
      <label><span>{{ t('adminOperations.format') }}</span><SearchSelect v-model="bodyFormat" :options="[{ value: 'text', label: t('adminOperations.plainText') }, { value: 'html', label: t('adminOperations.html') }]" /></label>
      <label><span>{{ t('adminOperations.emailBody') }}</span><textarea v-model="body" rows="10" required></textarea></label>
      <details v-if="body">
        <summary>{{ t('adminOperations.previewText') }}</summary>
        <iframe class="email-preview" sandbox="" referrerpolicy="no-referrer" :title="t('adminOperations.emailPreview')" :srcdoc="bodyPreview"></iframe>
        <small>{{ t('adminOperations.brandedEmailNotice') }}</small>
      </details>
      <div class="modal-grid">
        <label v-if="!isSingleRecipient"><span>{{ t('adminOperations.sendInterval') }}</span><input v-model.number="intervalSeconds" type="number" min="5" max="86400" required></label>
        <label><span>{{ t('adminOperations.sendAfter') }}</span><input v-model.number="startDelayMinutes" type="number" min="0" max="1440" required><small>{{ t('adminOperations.queueImmediately') }}</small></label>
      </div>
      <div class="modal-actions">
        <button class="primary-button" :disabled="isSaving || !recipients?.totalCount">{{ isSaving ? t('adminOperations.queueing') : startDelayMinutes > 0 ? t('adminOperations.schedule') : t('adminOperations.send') }}</button>
        <button class="secondary-button" type="button" @click="router.back()">{{ t('adminOperations.cancel') }}</button>
      </div>
    </form>
  </section>
</template>
