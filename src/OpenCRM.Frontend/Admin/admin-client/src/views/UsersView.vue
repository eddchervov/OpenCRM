<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { ApiError } from '@/api/ApiError'
import { getAdminUsers } from '@/api/adminUsersApi'
import type { AdminUserListItem } from '@/models/AdminUserListItem'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'

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

const sortLabels: Record<SortKey, string> = {
  date: 'Регистрация',
  email: 'Пользователь',
  vehicles: 'Техника',
  maintenance: 'ТО',
  organizations: 'Организации',
  plan: 'Личный тариф · прежняя модель'
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
    errorMessage.value = error instanceof ApiError
      ? error.message
      : 'Не удалось загрузить пользователей.'
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

  return new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric'
  }).format(new Date(value))
}

</script>

<template>
  <section class="users-page">
    <h1>Пользователи</h1>

    <form class="users-toolbar users-toolbar--list" @submit.prevent="submitSearch">
      <input
        v-model="search"
        type="search"
        placeholder="Email или телефон…"
        aria-label="Поиск по email или телефону"
      >
      <label class="deleted-filter">
        <input v-model="includeDeleted" type="checkbox" @change="loadUsers(true)">
        <span>Показывать удалённых</span>
      </label>
      <button class="primary-button" type="submit" :disabled="isLoading">
        Найти
      </button>
      <button class="secondary-button" type="button" :disabled="isLoading" @click="resetSearch">
        Сбросить
      </button>
      <span class="users-toolbar__count">Найдено: {{ allCount }}</span>
    </form>

    <div v-if="isLoading && users.length === 0" class="panel-state">
      Загружаем пользователей…
    </div>

    <div v-else-if="errorMessage" class="panel-state panel-state--error">
      {{ errorMessage }}
    </div>

    <div v-else-if="users.length === 0" class="panel-state">
      Пользователи не найдены.
    </div>

    <div v-else class="users-table-panel" :class="{ 'is-loading': isLoading }">
      <label class="users-sort">
        Сортировка
        <select v-model.number="sort" :disabled="isLoading" @change="loadUsers(true)">
          <template v-for="([key, option]) in visibleSortOptions" :key="key">
            <option :value="option.ascending">{{ sortLabels[key] }} ↑</option>
            <option :value="option.descending">{{ sortLabels[key] }} ↓</option>
          </template>
        </select>
      </label>
      <div class="table-wrap">
        <table class="data-table users-table">
          <thead>
            <tr>
              <th scope="col" aria-label="Пользователь" :aria-sort="getSortDirection('email')">
                <button type="button" @click="changeSort('email')">Пользователь {{ getSortMark('email') }}</button>
              </th>
              <th v-if="commercialEnabled" scope="col" aria-label="Тариф" :aria-sort="getSortDirection('plan')">
                <button type="button" @click="changeSort('plan')">Тариф {{ getSortMark('plan') }}</button>
              </th>
              <th scope="col" aria-label="Регистрация" :aria-sort="getSortDirection('date')">
                <button type="button" @click="changeSort('date')">Регистрация {{ getSortMark('date') }}</button>
              </th>
              <th scope="col" aria-label="Техника" class="users-table__number" :aria-sort="getSortDirection('vehicles')">
                <button type="button" @click="changeSort('vehicles')">Техника {{ getSortMark('vehicles') }}</button>
              </th>
              <th scope="col" aria-label="ТО" class="users-table__number" :aria-sort="getSortDirection('maintenance')">
                <button type="button" @click="changeSort('maintenance')">ТО {{ getSortMark('maintenance') }}</button>
              </th>
              <th scope="col" aria-label="Организации" class="users-table__number" :aria-sort="getSortDirection('organizations')">
                <button type="button" @click="changeSort('organizations')">Организации {{ getSortMark('organizations') }}</button>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="user in users" :key="user.id" :class="{ 'users-table__deleted': user.isDeleted }">
              <td data-label="Пользователь">
                <div class="users-table__identity">
                  <RouterLink :to="{ name: 'user-overview', params: { userId: user.id } }" class="users-table__email">
                    {{ user.email || user.phone || 'Открыть карточку' }}
                  </RouterLink>
                  <span>{{ user.phone || 'Телефон не указан' }}</span>
                  <span v-if="!user.emailConfirmed" class="confirmation">Email не подтверждён</span>
                  <span v-if="user.isDeleted" class="deleted-label">Удалён</span>
                </div>
              </td>
              <td v-if="commercialEnabled" data-label="Тариф">
                <strong>{{ user.effectivePlanName || 'Не назначен' }}</strong>
                <small v-if="user.currentSubscriptionEndDate">до {{ formatDate(user.currentSubscriptionEndDate) }}</small>
              </td>
              <td data-label="Регистрация">{{ formatDate(user.datetimeCreation) }}</td>
              <td class="users-table__number" data-label="Техника">{{ user.countVehicles }}</td>
              <td class="users-table__number" data-label="ТО">{{ user.countMaintenance }}</td>
              <td class="users-table__number" data-label="Организации">{{ user.organizationsCount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="allCount > 0 && !errorMessage" class="users-pagination">
      <span>Показаны {{ firstItem }}–{{ lastItem }} из {{ allCount }}</span>
      <div class="users-pagination__controls" aria-label="Страницы пользователей">
        <button type="button" :disabled="page === 1 || isLoading" @click="goToPage(page - 1)">Назад</button>
        <button
          v-for="pageNumber in visiblePages"
          :key="pageNumber"
          type="button"
          :class="{ 'is-current': pageNumber === page }"
          :aria-current="pageNumber === page ? 'page' : undefined"
          :disabled="isLoading"
          @click="goToPage(pageNumber)"
        >{{ pageNumber }}</button>
        <button type="button" :disabled="page === totalPages || isLoading" @click="goToPage(page + 1)">Вперёд</button>
      </div>
      <label class="users-pagination__size">
        На странице
        <select v-model.number="pageSize" @change="loadUsers(true)">
          <option :value="25">25</option>
          <option :value="50">50</option>
          <option :value="100">100</option>
        </select>
      </label>
    </div>
  </section>
</template>
