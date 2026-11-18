<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { ApiError } from '@/api/ApiError'
import { getAdminUsers } from '@/api/adminUsersApi'
import type { AdminUserListItem } from '@/models/AdminUserListItem'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminUsersMessages'

type SortKey = 'date' | 'email' | 'vehicles' | 'maintenance' | 'organizations' | 'plan'

interface SortOption {
  ascending: number
  descending: number
  defaultDirection: 'ascending' | 'descending'
}

const sortOptions: Record<SortKey, SortOption> = {
  date: { ascending: 2, descending: 1, defaultDirection: 'descending' },
  email: { ascending: 3, descending: 4, defaultDirection: 'ascending' },
  vehicles: { ascending: 8, descending: 7, defaultDirection: 'descending' },
  maintenance: { ascending: 10, descending: 9, defaultDirection: 'descending' },
  organizations: { ascending: 12, descending: 11, defaultDirection: 'descending' },
  plan: { ascending: 13, descending: 14, defaultDirection: 'ascending' }
}

const sortLabelKeys: Record<SortKey, string> = {
  date: 'adminUsers.users.registration',
  email: 'adminUsers.users.user',
  vehicles: 'adminUsers.users.vehicles',
  maintenance: 'adminUsers.users.maintenance',
  organizations: 'adminUsers.users.organizations',
  plan: 'adminUsers.users.sortPlan'
}

const users = ref<AdminUserListItem[]>([])
const allCount = ref(0)
const search = ref('')
const sort = ref(1)
const includeDeleted = ref(false)
const page = ref(1)
const pageSize = ref(25)
const isLoading = ref(false)
const errorMessage = ref<string | null>(null)
const totalPages = computed(() => Math.max(1, Math.ceil(allCount.value / pageSize.value)))
const firstItem = computed(() => allCount.value === 0 ? 0 : (page.value - 1) * pageSize.value + 1)
const lastItem = computed(() => Math.min(page.value * pageSize.value, allCount.value))
const visiblePages = computed(() => {
  const first = Math.max(1, Math.min(page.value - 2, totalPages.value - 4))
  return Array.from({ length: Math.min(5, totalPages.value - first + 1) }, (_, index) => first + index)
})
const currentUserStore = useAdminCurrentUserStore()
const { t } = useLocalization()
const commercialEnabled = computed(() => currentUserStore.currentUser?.commercialEnabled === true)
const visibleSortOptions = computed(() => (Object.keys(sortOptions) as SortKey[])
  .filter(key => commercialEnabled.value || key !== 'plan')
  .map(key => [key, sortOptions[key]] as const))
let searchTimer: ReturnType<typeof setTimeout> | undefined
let latestRequest = 0

onMounted(() => {
  void loadUsers()
})

onUnmounted(() => {
  clearTimeout(searchTimer)
  latestRequest++
})

watch(search, () => {
  page.value = 1
  latestRequest++
  isLoading.value = true
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => void loadUsers(true), 350)
}, { flush: 'sync' })

async function loadUsers(resetPage = false): Promise<void> {
  clearTimeout(searchTimer)
  if (resetPage) {
    page.value = 1
  }

  const requestId = ++latestRequest
  isLoading.value = true
  errorMessage.value = null

  try {
    const response = await getAdminUsers(
      search.value.trim(), sort.value, includeDeleted.value, page.value, pageSize.value
    )
    if (requestId !== latestRequest) return
    users.value = response.users
    allCount.value = response.allCount
    page.value = response.page
  } catch (error) {
    if (requestId !== latestRequest) return
    errorMessage.value = adminErrorMessage(error, 'adminUsers.users.loadError')
  } finally {
    if (requestId === latestRequest) isLoading.value = false
  }
}

function resetSearch(): void {
  search.value = ''
  sort.value = 1
  includeDeleted.value = false
  void loadUsers(true)
}

function changeSort(key: SortKey): void {
  const option = sortOptions[key]
  if (sort.value === option.ascending) {
    sort.value = option.descending
  } else if (sort.value === option.descending) {
    sort.value = option.ascending
  } else {
    sort.value = option.defaultDirection === 'ascending' ? option.ascending : option.descending
  }

  void loadUsers(true)
}

function goToPage(nextPage: number): void {
  if (nextPage < 1 || nextPage > totalPages.value || nextPage === page.value) return
  page.value = nextPage
  void loadUsers()
}

function submitSearch(): void {
  void loadUsers(true)
}

function getSortDirection(key: SortKey): 'ascending' | 'descending' | 'none' {
  const option = sortOptions[key]
  if (sort.value === option.ascending) {
    return 'ascending'
  }

  return sort.value === option.descending ? 'descending' : 'none'
}

function getSortMark(key: SortKey): string {
  const direction = getSortDirection(key)
  if (direction === 'ascending') {
    return '↑'
  }

  return direction === 'descending' ? '↓' : '↕'
}

function formatDate(value: string | null): string {
  if (!value) {
    return '—'
  }

  return new Intl.DateTimeFormat(formattingCulture.value, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric'
  }).format(new Date(value))
}

function sortLabel(key: SortKey): string {
  return t(sortLabelKeys[key])
}

function adminErrorMessage(error: unknown, fallbackKey: string): string {
  if (error instanceof ApiError) {
    return error.message.startsWith('API request failed with status ')
      ? t('adminUsers.api.requestFailed', { status: error.status })
      : error.message
  }

  return t(fallbackKey)
}

</script>

<template>
  <section class="users-page">
    <h1>{{ t('adminUsers.users.title') }}</h1>

    <form class="users-toolbar users-toolbar--list" @submit.prevent="submitSearch">
      <input
        v-model="search"
        type="search"
        :placeholder="t('adminUsers.users.searchPlaceholder')"
        :aria-label="t('adminUsers.users.searchLabel')"
      >
      <label class="deleted-filter">
        <input v-model="includeDeleted" type="checkbox" @change="loadUsers(true)">
        <span>{{ t('adminUsers.users.includeDeleted') }}</span>
      </label>
      <button class="primary-button" type="submit" :disabled="isLoading">
        {{ t('adminUsers.users.find') }}
      </button>
      <button class="secondary-button" type="button" :disabled="isLoading" @click="resetSearch">
        {{ t('adminUsers.users.reset') }}
      </button>
      <span class="users-toolbar__count">{{ t('adminUsers.users.found', { count: allCount }) }}</span>
    </form>

    <div v-if="isLoading && users.length === 0" class="panel-state">
      {{ t('adminUsers.users.loading') }}
    </div>

    <div v-else-if="errorMessage" class="panel-state panel-state--error">
      {{ errorMessage }}
    </div>

    <div v-else-if="users.length === 0" class="panel-state">
      {{ t('adminUsers.users.empty') }}
    </div>

    <div v-else class="users-table-panel" :class="{ 'is-loading': isLoading }">
      <label class="users-sort">
        {{ t('adminUsers.users.sort') }}
        <select v-model.number="sort" :disabled="isLoading" @change="loadUsers(true)">
          <template v-for="([key, option]) in visibleSortOptions" :key="key">
            <option :value="option.ascending">{{ sortLabel(key) }} ↑</option>
            <option :value="option.descending">{{ sortLabel(key) }} ↓</option>
          </template>
        </select>
      </label>
      <div class="table-wrap">
        <table class="data-table users-table">
          <thead>
            <tr>
              <th scope="col" :aria-label="t('adminUsers.users.user')" :aria-sort="getSortDirection('email')">
                <button type="button" @click="changeSort('email')">{{ t('adminUsers.users.user') }} {{ getSortMark('email') }}</button>
              </th>
              <th v-if="commercialEnabled" scope="col" :aria-label="t('adminUsers.users.plan')" :aria-sort="getSortDirection('plan')">
                <button type="button" @click="changeSort('plan')">{{ t('adminUsers.users.plan') }} {{ getSortMark('plan') }}</button>
              </th>
              <th scope="col" :aria-label="t('adminUsers.users.registration')" :aria-sort="getSortDirection('date')">
                <button type="button" @click="changeSort('date')">{{ t('adminUsers.users.registration') }} {{ getSortMark('date') }}</button>
              </th>
              <th scope="col" :aria-label="t('adminUsers.users.vehicles')" class="users-table__number" :aria-sort="getSortDirection('vehicles')">
                <button type="button" @click="changeSort('vehicles')">{{ t('adminUsers.users.vehicles') }} {{ getSortMark('vehicles') }}</button>
              </th>
              <th scope="col" :aria-label="t('adminUsers.users.maintenance')" class="users-table__number" :aria-sort="getSortDirection('maintenance')">
                <button type="button" @click="changeSort('maintenance')">{{ t('adminUsers.users.maintenance') }} {{ getSortMark('maintenance') }}</button>
              </th>
              <th scope="col" :aria-label="t('adminUsers.users.organizations')" class="users-table__number" :aria-sort="getSortDirection('organizations')">
                <button type="button" @click="changeSort('organizations')">{{ t('adminUsers.users.organizations') }} {{ getSortMark('organizations') }}</button>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="user in users" :key="user.id" :class="{ 'users-table__deleted': user.isDeleted }">
              <td :data-label="t('adminUsers.users.user')">
                <div class="users-table__identity">
                  <RouterLink :to="{ name: 'user-overview', params: { userId: user.id } }" class="users-table__email">
                    {{ user.email || user.phone || t('adminUsers.users.openCard') }}
                  </RouterLink>
                  <span>{{ user.phone || t('adminUsers.users.phoneNotProvided') }}</span>
                  <span v-if="!user.emailConfirmed" class="confirmation">{{ t('adminUsers.users.emailUnconfirmed') }}</span>
                  <span v-if="user.isDeleted" class="deleted-label">{{ t('adminUsers.users.deleted') }}</span>
                </div>
              </td>
              <td v-if="commercialEnabled" :data-label="t('adminUsers.users.plan')">
                <strong>{{ user.effectivePlanName || t('adminUsers.users.notAssigned') }}</strong>
                <small v-if="user.currentSubscriptionEndDate">{{ t('adminUsers.users.until', { date: formatDate(user.currentSubscriptionEndDate) }) }}</small>
              </td>
              <td :data-label="t('adminUsers.users.registration')">{{ formatDate(user.datetimeCreation) }}</td>
              <td class="users-table__number" :data-label="t('adminUsers.users.vehicles')">{{ user.countVehicles }}</td>
              <td class="users-table__number" :data-label="t('adminUsers.users.maintenance')">{{ user.countMaintenance }}</td>
              <td class="users-table__number" :data-label="t('adminUsers.users.organizations')">{{ user.organizationsCount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="allCount > 0 && !errorMessage" class="users-pagination">
      <span>{{ t('adminUsers.users.range', { first: firstItem, last: lastItem, total: allCount }) }}</span>
      <div class="users-pagination__controls" :aria-label="t('adminUsers.users.pagination')">
        <button type="button" :disabled="page === 1 || isLoading" @click="goToPage(page - 1)">{{ t('adminUsers.users.previous') }}</button>
        <button
          v-for="pageNumber in visiblePages"
          :key="pageNumber"
          type="button"
          :class="{ 'is-current': pageNumber === page }"
          :aria-current="pageNumber === page ? 'page' : undefined"
          :disabled="isLoading"
          @click="goToPage(pageNumber)"
        >{{ pageNumber }}</button>
        <button type="button" :disabled="page === totalPages || isLoading" @click="goToPage(page + 1)">{{ t('adminUsers.users.next') }}</button>
      </div>
      <label class="users-pagination__size">
        {{ t('adminUsers.users.pageSize') }}
        <select v-model.number="pageSize" @change="loadUsers(true)">
          <option :value="25">25</option>
          <option :value="50">50</option>
          <option :value="100">100</option>
        </select>
      </label>
    </div>
  </section>
</template>
