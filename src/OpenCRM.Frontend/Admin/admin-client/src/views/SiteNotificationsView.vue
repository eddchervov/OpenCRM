<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  deleteSiteNotification,
  getSiteNotifications,
  getSiteNotificationUsers,
  sendSiteNotification,
  updateSiteNotificationTemplate
} from '@/api/adminSiteNotificationsApi'
import type { AdminSiteNotification, SiteNotificationUser } from '@/models/AdminSiteNotification'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'
import { ApiError } from '@/api/ApiError'
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminOperationsMessages'

const { t } = useLocalization()
const typeLabelKeys: Record<string, string> = {
  'organization-invitation': 'notificationOrganizationInvitation',
  'invitation-accepted': 'notificationInvitationAccepted',
  'organization-subscription-expiring': 'notificationSubscriptionExpiring',
  'organization-subscription-expired': 'notificationSubscriptionExpired',
  welcome: 'notificationWelcome'
}
const typeLabels = computed(() => Object.fromEntries(Object.entries(typeLabelKeys).map(([code, key]) => [code, t(`adminOperations.${key}`)])))
const currentUserStore = useAdminCurrentUserStore()
const canManage = computed(() => currentUserStore.isSystem || currentUserStore.currentUser?.permissions.includes('Admin.Communications.Manage') === true)
const items = ref<AdminSiteNotification[]>([])
const templates = computed(() => {
  const latest = new Map<string, AdminSiteNotification>()
  for (const item of items.value) {
    if (item.isTemplate && !latest.has(item.typeCode)) {
      latest.set(item.typeCode, item)
    }
  }

  return [...latest.values()]
})
const sentItems = computed(() => items.value.filter(item => !item.isTemplate))
const editingTemplate = ref<AdminSiteNotification | null>(null)
const templateTitle = ref('')
const templateBody = ref('')
const sendTitle = ref('')
const sendBody = ref('')
const recipientScope = ref<'all' | 'selected'>('all')
const roleScope = ref<'all' | 'restricted'>('all')
const selectedRoles = ref<string[]>([])
const selectedUsers = ref<SiteNotificationUser[]>([])
const userSearch = ref('')
const foundUsers = ref<SiteNotificationUser[]>([])
const isLoading = ref(false)
const isSaving = ref(false)
const isSearching = ref(false)
const error = ref('')
const notice = ref('')

onMounted(loadItems)

async function loadItems(): Promise<void> {
  isLoading.value = true
  error.value = ''
  try {
    items.value = await getSiteNotifications()
  } catch {
    error.value = t('adminOperations.notificationLoadError')
  } finally {
    isLoading.value = false
  }
}

function editTemplate(item: AdminSiteNotification): void {
  editingTemplate.value = item
  templateTitle.value = item.title
  templateBody.value = item.body
  error.value = ''
  notice.value = ''
}

async function saveTemplate(): Promise<void> {
  if (!editingTemplate.value || !templateTitle.value.trim() || !templateBody.value.trim()) {
    error.value = t('adminOperations.templateRequired')
    return
  }

  isSaving.value = true
  error.value = ''
  notice.value = ''
  try {
    await updateSiteNotificationTemplate(editingTemplate.value.id, {
      title: templateTitle.value.trim(),
      body: templateBody.value.trim()
    })
    editingTemplate.value = null
    notice.value = t('adminOperations.templateSaved')
    await loadItems()
  } catch {
    error.value = t('adminOperations.templateSaveError')
  } finally {
    isSaving.value = false
  }
}

async function searchUsers(): Promise<void> {
  isSearching.value = true
  error.value = ''
  try {
    foundUsers.value = await getSiteNotificationUsers(userSearch.value.trim())
  } catch {
    error.value = t('adminOperations.usersSearchError')
  } finally {
    isSearching.value = false
  }
}

function toggleUser(user: SiteNotificationUser): void {
  selectedUsers.value = selectedUsers.value.some(item => item.id === user.id)
    ? selectedUsers.value.filter(item => item.id !== user.id)
    : [...selectedUsers.value, user]
}

function toggleRole(role: string): void {
  selectedRoles.value = selectedRoles.value.includes(role)
    ? selectedRoles.value.filter(item => item !== role)
    : [...selectedRoles.value, role]
}

async function send(): Promise<void> {
  if (!sendTitle.value.trim() || !sendBody.value.trim()) {
    error.value = t('adminOperations.notificationRequired')
    return
  }
  if (recipientScope.value === 'selected' && selectedUsers.value.length === 0) {
    error.value = t('adminOperations.selectAtLeastOneUser')
    return
  }
  if (roleScope.value === 'restricted' && selectedRoles.value.length === 0) {
    error.value = t('adminOperations.selectAtLeastOneRole')
    return
  }

  isSaving.value = true
  error.value = ''
  notice.value = ''
  try {
    await sendSiteNotification({
      title: sendTitle.value.trim(),
      body: sendBody.value.trim(),
      recipientScope: recipientScope.value,
      userIds: recipientScope.value === 'selected' ? selectedUsers.value.map(user => user.id) : [],
      audiences: roleScope.value === 'all' ? ['all'] : selectedRoles.value
    })
    sendTitle.value = ''
    sendBody.value = ''
    selectedUsers.value = []
    notice.value = t('adminOperations.notificationSent')
    await loadItems()
  } catch (cause) {
    error.value = cause instanceof ApiError ? cause.message : t('adminOperations.notificationSendError')
  } finally {
    isSaving.value = false
  }
}

async function remove(item: AdminSiteNotification): Promise<void> {
  if (!canManage.value || !window.confirm(t('adminOperations.deleteNotificationConfirm', { title: item.title }))) {
    return
  }

  error.value = ''
  try {
    await deleteSiteNotification(item.id)
    await loadItems()
  } catch {
    error.value = t('adminOperations.notificationDeleteError')
  }
}

function date(value: string): string {
  return new Intl.DateTimeFormat(formattingCulture.value, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
}
</script>

<template>
  <section class="operations-page site-notifications-page">
    <h1>{{ t('adminOperations.siteNotifications') }}</h1>
    <div v-if="error" class="form-message form-message--error">{{ error }}</div>
    <div v-if="notice" class="form-message form-message--success">{{ notice }}</div>

    <section class="site-notifications-section">
      <div class="site-notifications-section__header">
        <h2>{{ t('adminOperations.automaticTemplates') }}</h2>
        <button class="secondary-button" type="button" :disabled="isLoading" @click="loadItems">{{ t('adminOperations.refresh') }}</button>
      </div>
      <p class="site-notifications-hint">{{ t('adminOperations.automaticHint') }}</p>
      <div v-if="isLoading" class="empty-state">{{ t('adminOperations.loading') }}</div>
      <div v-else class="site-notifications-templates">
        <article v-for="item in templates" :key="item.id" class="site-notifications-template">
          <strong>{{ typeLabels[item.typeCode] ?? item.typeCode }}</strong>
          <span>{{ item.title }}</span>
          <small>{{ item.body }}</small>
          <button v-if="canManage" class="table-action" type="button" @click="editTemplate(item)">{{ t('adminOperations.edit') }}</button>
        </article>
      </div>

      <form v-if="canManage && editingTemplate" class="site-notifications-form" @submit.prevent="saveTemplate">
        <div class="site-notifications-section__header">
          <h3>{{ t('adminOperations.template', { name: typeLabels[editingTemplate.typeCode] ?? editingTemplate.typeCode }) }}</h3>
          <button class="secondary-button" type="button" @click="editingTemplate = null">{{ t('adminOperations.cancelEdit') }}</button>
        </div>
        <label><span>{{ t('adminOperations.title') }}</span><input v-model="templateTitle" required maxlength="200"></label>
        <label><span>{{ t('adminOperations.text') }}</span><textarea v-model="templateBody" required rows="4" maxlength="4000"></textarea></label>
        <p class="site-notifications-hint">{{ t('adminOperations.templateVariables') }}</p>
        <button class="primary-button" type="submit" :disabled="isSaving">{{ t('adminOperations.saveTemplate') }}</button>
      </form>
    </section>

    <section class="site-notifications-section">
      <h2>{{ t('adminOperations.oneTimeNotification') }}</h2>
      <form v-if="canManage" class="site-notifications-form" @submit.prevent="send">
        <label><span>{{ t('adminOperations.title') }}</span><input v-model="sendTitle" required maxlength="200"></label>
        <label><span>{{ t('adminOperations.text') }}</span><textarea v-model="sendBody" required rows="4" maxlength="4000"></textarea></label>

        <fieldset>
          <legend>{{ t('adminOperations.users') }}</legend>
          <label><input v-model="recipientScope" type="radio" value="all"> {{ t('adminOperations.allUsers') }}</label>
          <label><input v-model="recipientScope" type="radio" value="selected"> {{ t('adminOperations.selectedUsers') }}</label>
        </fieldset>
        <div v-if="recipientScope === 'selected'" class="site-notifications-users">
          <div class="site-notifications-search">
            <input v-model="userSearch" type="search" :placeholder="t('adminOperations.userEmail')" @keyup.enter.prevent="searchUsers">
            <button class="secondary-button" type="button" :disabled="isSearching" @click="searchUsers">{{ t('adminOperations.find') }}</button>
          </div>
          <label v-for="user in foundUsers" :key="user.id">
            <input type="checkbox" :checked="selectedUsers.some(item => item.id === user.id)" @change="toggleUser(user)">
            {{ user.email }}
          </label>
          <p v-if="selectedUsers.length" class="site-notifications-hint">{{ t('adminOperations.selectedList', { users: selectedUsers.map(user => user.email).join(', ') }) }}</p>
        </div>

        <fieldset>
          <legend>{{ t('adminOperations.recipientRoles') }}</legend>
          <label><input v-model="roleScope" type="radio" value="all"> {{ t('adminOperations.allRoles') }}</label>
          <label><input v-model="roleScope" type="radio" value="restricted"> {{ t('adminOperations.restrictedRoles') }}</label>
          <div v-if="roleScope === 'restricted'">
            <label><input type="checkbox" :checked="selectedRoles.includes('admins')" @change="toggleRole('admins')"> {{ t('adminOperations.administrators') }}</label>
            <label><input type="checkbox" :checked="selectedRoles.includes('organization-owners')" @change="toggleRole('organization-owners')"> {{ t('adminOperations.orgOwners') }}</label>
          </div>
        </fieldset>
        <button class="primary-button" type="submit" :disabled="isSaving">{{ t('adminOperations.sendNotification') }}</button>
      </form>
    </section>

    <section class="site-notifications-section">
      <h2>{{ t('adminOperations.notificationHistory') }}</h2>
      <div v-if="!isLoading && sentItems.length === 0" class="empty-state">{{ t('adminOperations.noNotifications') }}</div>
      <div v-else-if="!isLoading" class="table-wrap">
        <table class="data-table">
          <thead><tr><th>{{ t('adminOperations.type') }}</th><th>{{ t('adminOperations.titleAndText') }}</th><th>{{ t('adminOperations.recipientAndRoles') }}</th><th>{{ t('adminOperations.created') }}</th><th></th></tr></thead>
          <tbody>
            <tr v-for="item in sentItems" :key="item.id">
              <td>{{ item.typeCode === 'manual' ? t('adminOperations.oneTime') : typeLabels[item.typeCode] ?? item.typeCode }}</td>
              <td><strong>{{ item.title }}</strong><small class="site-notifications-list__body">{{ item.body }}</small></td>
              <td>{{ item.recipientEmail ?? t('adminOperations.allUsers') }} · {{ item.audiences.map(role => role === 'all' ? t('adminOperations.allRoles') : role === 'admins' ? t('adminOperations.administrators') : t('adminOperations.orgOwners')).join(', ') }}</td>
              <td>{{ date(item.createdAt) }}</td>
              <td><button v-if="canManage" class="table-action danger-link" type="button" @click="remove(item)">{{ t('adminOperations.delete') }}</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </section>
</template>

<style scoped>
.site-notifications-section {
  margin: 24px 0;
}
.site-notifications-section__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}
.site-notifications-section h2,
.site-notifications-section h3 {
  margin: 0 0 12px;
  font-size: 18px;
}
.site-notifications-hint {
  color: #667085;
  font-size: 13px;
}
.site-notifications-templates {
  display: grid;
  gap: 10px;
}
.site-notifications-template,
.site-notifications-form {
  display: grid;
  gap: 12px;
  max-width: 820px;
  padding: 18px;
  border: 1px solid #e1e5ec;
  border-radius: 10px;
  background: #fff;
}
.site-notifications-template small {
  white-space: pre-line;
  color: #667085;
}
.site-notifications-template button {
  justify-self: start;
}
.site-notifications-form {
  margin-top: 16px;
}
.site-notifications-form > label {
  display: grid;
  gap: 6px;
  font-size: 13px;
  font-weight: 600;
}
.site-notifications-form > label > input,
.site-notifications-form textarea,
.site-notifications-search input {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid #d6dbe5;
  border-radius: 7px;
  color: #202939;
  font: inherit;
}
.site-notifications-form fieldset {
  display: grid;
  gap: 8px;
  padding: 12px;
  border: 1px solid #d6dbe5;
  border-radius: 7px;
}
.site-notifications-form legend {
  float: none;
  width: auto;
  padding: 0 4px;
  font-size: 13px;
  font-weight: 600;
}
.site-notifications-form fieldset label,
.site-notifications-users label {
  display: block;
  font-size: 13px;
}
.site-notifications-search {
  display: flex;
  gap: 8px;
  max-width: 600px;
}
.site-notifications-users {
  display: grid;
  gap: 8px;
}
.site-notifications-list__body {
  display: block;
  max-width: 520px;
  margin-top: 4px;
  color: #667085;
  white-space: pre-line;
}
@media (max-width: 720px) {
  .site-notifications-search {
    flex-direction: column;
  }
  .site-notifications-page .table-wrap {
    overflow-x: auto;
  }
}
</style>
