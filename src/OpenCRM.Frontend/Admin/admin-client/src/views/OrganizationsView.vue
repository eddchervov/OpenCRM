<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ApiError } from '@/api/ApiError'
import { getAdminOrganizations } from '@/api/adminOrganizationsApi'
import type { AdminOrganizationsPage } from '@/models/AdminOrganization'
import { usesFreePro } from '@/utils/subscriptionPolicy'
import { getOrganizationScenarioLabel, getOrganizationStatusLabel } from '@/utils/adminSystemLabels'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminOperationsMessages'

const { t, locale } = useLocalization()
const currentUserStore = useAdminCurrentUserStore()
const commercialEnabled = computed(() => currentUserStore.currentUser?.commercialEnabled === true)
const result = ref<AdminOrganizationsPage | null>(null)
const search = ref('')
const user = ref('')
const page = ref(1)
const isLoading = ref(false)
const errorMessage = ref<string | null>(null)
const pages = computed(() => Math.max(1, Math.ceil((result.value?.totalCount ?? 0) / 25)))
let requestId = 0

onMounted(() => void load())

async function load(resetPage = false): Promise<void> {
  if (resetPage) {
    page.value = 1
  }

  const currentRequest = ++requestId
  isLoading.value = true
  errorMessage.value = null
  try {
    const response = await getAdminOrganizations(search.value.trim(), user.value.trim(), page.value)
    if (currentRequest === requestId) {
      result.value = response
    }
  } catch (error) {
    if (currentRequest === requestId) {
      result.value = null
      errorMessage.value = error instanceof ApiError ? error.message : t('adminOperations.loadOrganizationsError')
    }
  } finally {
    if (currentRequest === requestId) {
      isLoading.value = false
    }
  }
}

function changePage(offset: number): void {
  page.value += offset
  void load()
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat(formattingCulture.value).format(new Date(value))
}

function statusLabel(organization: NonNullable<AdminOrganizationsPage['organizations']>[number]): string {
  return getOrganizationStatusLabel(organization, locale.value)
}

function scenarioLabel(organization: NonNullable<AdminOrganizationsPage['organizations']>[number]): string {
  return getOrganizationScenarioLabel(organization.primaryScenarioCode, organization.scenarioName, locale.value)
}
</script>

<template>
  <section class="finance-page">
    <h1>{{ t('adminOperations.organizations') }}</h1>
    <p v-if="commercialEnabled" class="muted">{{ t('adminOperations.orgIntro') }}</p>

    <form class="finance-toolbar" @submit.prevent="load(true)">
      <label><span>{{ t('adminOperations.orgOrTaxId') }}</span><input v-model="search" :placeholder="t('adminOperations.orgSearchPlaceholder')"></label>
      <label><span>{{ t('adminOperations.ownerOrMember') }}</span><input v-model="user" :placeholder="t('adminOperations.userSearchPlaceholder')"></label>
      <button class="primary-button" type="submit" :disabled="isLoading">{{ t('adminOperations.find') }}</button>
    </form>

    <div v-if="errorMessage" class="form-message form-message--error" role="alert">{{ errorMessage }}</div>
    <div v-if="isLoading" class="panel-state" role="status">{{ t('adminOperations.loading') }}</div>
    <template v-else-if="result">
      <p class="muted">{{ t('adminOperations.found', { total: result.totalCount, active: result.activeCount, archived: result.archivedCount, deleted: result.deletedCount }) }}</p>
      <div class="table-wrap organizations-table">
        <table class="data-table finance-table">
          <thead><tr><th>{{ t('adminOperations.organization') }}</th><th>{{ t('adminOperations.owner') }}</th><th>{{ t('adminOperations.status') }}</th><th v-if="commercialEnabled">{{ t('adminOperations.accessAndPlan') }}</th><th>{{ t('adminOperations.clientsVehicles') }}</th><th>{{ t('adminOperations.members') }}</th><th></th></tr></thead>
          <tbody>
            <tr v-for="organization in result.organizations" :key="organization.id">
              <td><strong>{{ organization.name }}</strong><small>{{ scenarioLabel(organization) }}</small></td>
              <td><RouterLink :to="`/users/${encodeURIComponent(organization.ownerUserId)}`">{{ organization.ownerEmail || organization.owner }}</RouterLink></td>
              <td><span class="status-badge" :class="organization.isActive && !organization.isArchived && !organization.isDeleted ? 'status-badge--success' : ''">{{ statusLabel(organization) }}</span></td>
              <td v-if="commercialEnabled">
                <template v-if="organization.activeSubscriptionEndDate">
                  <strong>{{ organization.activeSubscriptionName }}</strong>
                  <small>{{ t('adminOperations.until', { date: formatDate(organization.activeSubscriptionEndDate) }) }}</small>
                </template>
                <template v-else-if="organization.isActive && !organization.isArchived && !organization.isDeleted && usesFreePro(organization.primaryScenarioCode)"><strong>Free</strong><small>{{ t('adminOperations.freeForever') }}</small></template>
                <template v-else>{{ organization.activeSubscriptionName || t('adminOperations.noAccess') }}</template>
              </td>
              <td>{{ organization.clientsCount }} / {{ organization.vehiclesCount }}</td>
              <td>{{ organization.membersCount }}</td>
              <td><RouterLink class="table-action" :to="{ path: `/users/${encodeURIComponent(organization.ownerUserId)}`, query: { tab: 'organizations' } }">{{ commercialEnabled ? t('adminOperations.organizationsAndSubscriptions') : t('adminOperations.organizations') }}</RouterLink></td>
            </tr>
            <tr v-if="result.organizations.length === 0"><td :colspan="commercialEnabled ? 7 : 6">{{ t('adminOperations.notFound') }}</td></tr>
          </tbody>
        </table>
      </div>
      <div class="organization-cards organizations-mobile">
        <article v-for="organization in result.organizations" :key="organization.id" class="record-card">
          <div class="record-card__top"><strong>{{ organization.name }}</strong><span class="status-badge">{{ statusLabel(organization) }}</span></div>
          <span>{{ scenarioLabel(organization) }}</span>
          <dl>
            <div v-if="commercialEnabled"><dt>{{ t('adminOperations.plan') }}</dt><dd>{{ organization.activeSubscriptionName || t('adminOperations.noAccess') }}</dd></div>
            <div v-if="commercialEnabled && organization.activeSubscriptionEndDate"><dt>{{ t('adminOperations.untilLabel') }}</dt><dd>{{ formatDate(organization.activeSubscriptionEndDate) }}</dd></div>
            <div v-else-if="commercialEnabled && organization.isActive && !organization.isArchived && !organization.isDeleted && usesFreePro(organization.primaryScenarioCode)"><dt>{{ t('adminOperations.freeTerm') }}</dt><dd>{{ t('adminOperations.noExpiry') }}</dd></div>
            <div><dt>{{ t('adminOperations.customerVehicles') }}</dt><dd>{{ organization.clientsCount }} / {{ organization.vehiclesCount }}</dd></div>
            <div><dt>{{ t('adminOperations.members') }}</dt><dd>{{ organization.membersCount }}</dd></div>
            <div><dt>{{ t('adminOperations.owner') }}</dt><dd><RouterLink :to="`/users/${encodeURIComponent(organization.ownerUserId)}`">{{ organization.ownerEmail || organization.owner }}</RouterLink></dd></div>
          </dl>
          <div class="record-card__actions"><RouterLink class="table-action" :to="{ path: `/users/${encodeURIComponent(organization.ownerUserId)}`, query: { tab: 'organizations' } }">{{ commercialEnabled ? t('adminOperations.organizationsAndSubscriptions') : t('adminOperations.organizations') }}</RouterLink></div>
        </article>
        <p v-if="result.organizations.length === 0">{{ t('adminOperations.notFound') }}</p>
      </div>
      <div class="finance-actions" :aria-label="t('adminOperations.pages')">
        <button class="secondary-button" type="button" :disabled="page <= 1" @click="changePage(-1)">{{ t('adminOperations.previous') }}</button>
        <span>{{ t('adminOperations.page', { page, pages }) }}</span>
        <button class="secondary-button" type="button" :disabled="page >= pages" @click="changePage(1)">{{ t('adminOperations.next') }}</button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.organizations-mobile {
  display: none;
}

.finance-toolbar label {
  display: grid;
  gap: 6px;
  min-width: 0;
}

.finance-toolbar {
  align-items: end;
}

@media (max-width: 620px) {
  .organizations-table {
    display: none;
  }

  .organizations-mobile {
    display: grid;
  }

  .finance-toolbar label,
  .finance-toolbar input {
    width: 100%;
  }

  .record-card dd {
    overflow-wrap: anywhere;
  }
}
</style>
