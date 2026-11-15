<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ApiError } from '@/api/ApiError'
import { getAdminOrganizations } from '@/api/adminOrganizationsApi'
import type { AdminOrganizationsPage } from '@/models/AdminOrganization'
import { usesFreePro } from '@/utils/subscriptionPolicy'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'

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
      errorMessage.value = error instanceof ApiError ? error.message : 'Не удалось загрузить организации.'
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
  return new Intl.DateTimeFormat('ru-RU').format(new Date(value))
}
</script>

<template>
  <section class="finance-page">
    <h1>Организации</h1>
    <p v-if="commercialEnabled" class="muted">Детейлинг и автосервис работают на Free без ограничения срока. PRO подключается отдельно для каждой организации. Прежние организации и их данные сохранены.</p>

    <form class="finance-toolbar" @submit.prevent="load(true)">
      <label><span>Организация или ИНН</span><input v-model="search" placeholder="Название, юридическое имя, ИНН"></label>
      <label><span>Владелец или участник</span><input v-model="user" placeholder="Email, телефон или ID"></label>
      <button class="primary-button" type="submit" :disabled="isLoading">Найти</button>
    </form>

    <div v-if="errorMessage" class="form-message form-message--error" role="alert">{{ errorMessage }}</div>
    <div v-if="isLoading" class="panel-state" role="status">Загрузка…</div>
    <template v-else-if="result">
      <p class="muted">Найдено: {{ result.totalCount }} · Активных: {{ result.activeCount }} · В архиве: {{ result.archivedCount }} · Удалённых: {{ result.deletedCount }}</p>
      <div class="table-wrap organizations-table">
        <table class="data-table finance-table">
          <thead><tr><th>Организация</th><th>Владелец</th><th>Статус</th><th v-if="commercialEnabled">Доступ и тариф</th><th>Клиенты / авто</th><th>Участники</th><th></th></tr></thead>
          <tbody>
            <tr v-for="organization in result.organizations" :key="organization.id">
              <td><strong>{{ organization.name }}</strong><small>{{ organization.scenarioName }}</small></td>
              <td><RouterLink :to="`/users/${encodeURIComponent(organization.ownerUserId)}`">{{ organization.ownerEmail || organization.owner }}</RouterLink></td>
              <td><span class="status-badge" :class="organization.isActive && !organization.isArchived && !organization.isDeleted ? 'status-badge--success' : ''">{{ organization.statusName }}</span></td>
              <td v-if="commercialEnabled">
                <template v-if="organization.activeSubscriptionEndDate">
                  <strong>{{ organization.activeSubscriptionName }}</strong>
                  <small>До {{ formatDate(organization.activeSubscriptionEndDate) }}</small>
                </template>
                <template v-else-if="organization.isActive && !organization.isArchived && !organization.isDeleted && usesFreePro(organization.primaryScenarioCode)"><strong>Free</strong><small>Бесплатно · бессрочно</small></template>
                <template v-else>{{ organization.activeSubscriptionName || 'Нет активного доступа' }}</template>
              </td>
              <td>{{ organization.clientsCount }} / {{ organization.vehiclesCount }}</td>
              <td>{{ organization.membersCount }}</td>
              <td><RouterLink class="table-action" :to="{ path: `/users/${encodeURIComponent(organization.ownerUserId)}`, query: { tab: 'organizations' } }">Организации{{ commercialEnabled ? ' и подписки' : '' }}</RouterLink></td>
            </tr>
            <tr v-if="result.organizations.length === 0"><td :colspan="commercialEnabled ? 7 : 6">Организации не найдены.</td></tr>
          </tbody>
        </table>
      </div>
      <div class="organization-cards organizations-mobile">
        <article v-for="organization in result.organizations" :key="organization.id" class="record-card">
          <div class="record-card__top"><strong>{{ organization.name }}</strong><span class="status-badge">{{ organization.statusName }}</span></div>
          <span>{{ organization.scenarioName }}</span>
          <dl>
            <div v-if="commercialEnabled"><dt>Тариф</dt><dd>{{ organization.activeSubscriptionName || 'Нет активного доступа' }}</dd></div>
            <div v-if="commercialEnabled && organization.activeSubscriptionEndDate"><dt>До</dt><dd>{{ formatDate(organization.activeSubscriptionEndDate) }}</dd></div>
            <div v-else-if="commercialEnabled && organization.isActive && !organization.isArchived && !organization.isDeleted && usesFreePro(organization.primaryScenarioCode)"><dt>Срок Free</dt><dd>Бессрочно</dd></div>
            <div><dt>Клиенты / авто</dt><dd>{{ organization.clientsCount }} / {{ organization.vehiclesCount }}</dd></div>
            <div><dt>Участники</dt><dd>{{ organization.membersCount }}</dd></div>
            <div><dt>Владелец</dt><dd><RouterLink :to="`/users/${encodeURIComponent(organization.ownerUserId)}`">{{ organization.ownerEmail || organization.owner }}</RouterLink></dd></div>
          </dl>
          <div class="record-card__actions"><RouterLink class="table-action" :to="{ path: `/users/${encodeURIComponent(organization.ownerUserId)}`, query: { tab: 'organizations' } }">{{ commercialEnabled ? 'Организации и подписки' : 'Организации' }}</RouterLink></div>
        </article>
        <p v-if="result.organizations.length === 0">Организации не найдены.</p>
      </div>
      <div class="finance-actions" aria-label="Страницы организаций">
        <button class="secondary-button" type="button" :disabled="page <= 1" @click="changePage(-1)">Назад</button>
        <span>{{ page }} из {{ pages }}</span>
        <button class="secondary-button" type="button" :disabled="page >= pages" @click="changePage(1)">Далее</button>
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
