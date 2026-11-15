<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { getAdminBillingAttention } from '@/api/adminBillingApi'
import { logoutAdminSession } from '@/api/adminSessionApi'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'
import { getProjectBrand } from '../../../packages/ui/src/projectBrand'

const currentUserStore = useAdminCurrentUserStore()
const projectBrand = getProjectBrand()
const isLoggingOut = ref(false)
const logoutError = ref('')
const billingAttention = ref<{ overdueReceiptCount: number; failedActivationCount: number } | null>(null)
let attentionRefreshInterval: ReturnType<typeof setInterval> | undefined
const desktopShellStorageKey = 'opencrm-admin-desktop-shell'
const themeStorageKey = 'opencrm-admin-theme'
const isDesktopShell = new URLSearchParams(window.location.search).get('desktop') === '1'
const savedTheme = window.localStorage.getItem(themeStorageKey)
const theme = ref<'light' | 'dark'>(savedTheme === 'light' || savedTheme === 'dark'
  ? savedTheme
  : window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
document.documentElement.dataset.adminTheme = theme.value
document.title = `${projectBrand} — Администрирование`

if (isDesktopShell) {
  window.sessionStorage.setItem(desktopShellStorageKey, '1')
}

const navigationItems = [
  { label: 'Дашборд', to: '/', permission: 'Admin.Dashboard.View' },
  { label: 'Пользователи', to: '/users', permission: 'Admin.Users.View' },
  { label: 'Организации', to: '/organizations', permission: 'Admin.Users.View' }
]

const visibleNavigationItems = computed(() => navigationItems.filter(item =>
  currentUserStore.isSystem || currentUserStore.currentUser?.permissions.includes(item.permission) === true
))

interface AdminProductSection {
  title: string
  url: string
  permission: string
}

function readProductSections(): AdminProductSection[] {
  const content = document.querySelector<HTMLMetaElement>('meta[name="admin-product-sections"]')?.content
  if (!content) {
    return []
  }

  try {
    const sections: unknown = JSON.parse(content)
    if (!Array.isArray(sections)) {
      return []
    }

    return sections.filter((section): section is AdminProductSection =>
      typeof section === 'object' && section !== null &&
      typeof section.title === 'string' && typeof section.url === 'string' &&
      typeof section.permission === 'string' && section.title.length > 0 &&
      section.url.startsWith('/') && !section.url.startsWith('//') &&
      !section.url.includes('\\') && !section.url.includes('?') && !section.url.includes('#') &&
      !/[\u0000-\u001f\u007f]/.test(section.url) && section.permission.length > 0
    )
  } catch {
    return []
  }
}

const productSections = readProductSections()
const visibleProductSections = computed(() => productSections.filter(section => canSee(section.permission)))

const financeNavigationItems = [
  { label: 'Платежи и подписки', to: '/finance/billing', permission: 'Admin.Billing.View', commercial: true }
]

const configurationNavigationItems: { label: string, to: string, permission: string, commercial?: boolean }[] = [
  { label: 'Тарифы', to: '/finance/subscription-plans', permission: 'Admin.Configuration.Manage', commercial: true },
  { label: 'Промокоды', to: '/finance/promo-codes', permission: 'Admin.Configuration.Manage', commercial: true },
  { label: 'Возможности', to: '/configuration/features', permission: 'Admin.Configuration.Manage' },
  { label: 'ИИ-помощник', to: '/assistant', permission: 'Admin.Configuration.Manage' },
  { label: 'Каталог техники', to: '/configuration/vehicle-catalog', permission: 'Admin.Content.Manage' }
]

const visibleFinanceNavigationItems = computed(() => financeNavigationItems.filter(item =>
  currentUserStore.currentUser?.commercialEnabled === true &&
  (currentUserStore.isSystem || currentUserStore.currentUser?.permissions.includes(item.permission) === true)
))

const visibleConfigurationNavigationItems = computed(() => configurationNavigationItems.filter(item =>
  (!item.commercial || currentUserStore.currentUser?.commercialEnabled === true) &&
  (currentUserStore.isSystem || currentUserStore.currentUser?.permissions.includes(item.permission) === true)
))

const contentNavigationItems = [
  { label: 'Обращения', to: '/content/support', permission: 'Admin.Content.View' },
  { label: 'Предложения в каталог', to: '/content/catalog-suggestions', permission: 'Admin.Content.View' }
]

const communicationsNavigationItems = [
  { label: 'История email', to: '/communications/emails', permission: 'Admin.Communications.View' },
  { label: 'Уведомления сайта', to: '/communications/site-notifications', permission: 'Admin.Communications.View' },
  { label: 'Email-рассылки', to: '/communications/email-campaigns', permission: 'Admin.Communications.Manage' },
  { label: 'История SMS', to: '/communications/sms', permission: 'Admin.Communications.View' }
]

function canSee(permission: string): boolean {
  return currentUserStore.isSystem || currentUserStore.currentUser?.permissions.includes(permission) === true
}

function toggleTheme(): void {
  theme.value = theme.value === 'light' ? 'dark' : 'light'
  document.documentElement.dataset.adminTheme = theme.value
  window.localStorage.setItem(themeStorageKey, theme.value)
}

async function logout(): Promise<void> {
  isLoggingOut.value = true
  logoutError.value = ''

  try {
    await logoutAdminSession()
    window.location.replace('/auth/login?returnUrl=%2Fadmin-panel')
  } catch {
    logoutError.value = 'Не удалось выйти.'
    isLoggingOut.value = false
  }
}

function openMainSite(event: MouseEvent): void {
  if (window.sessionStorage.getItem(desktopShellStorageKey) !== '1') {
    return
  }

  event.preventDefault()
  window.open('/', '_blank', 'noopener')
}

async function refreshBillingAttention(): Promise<void> {
  if (currentUserStore.currentUser?.commercialEnabled !== true || !canSee('Admin.Billing.View')) {
    billingAttention.value = null
    return
  }

  try {
    billingAttention.value = await getAdminBillingAttention()
  } catch {
    billingAttention.value = null
  }
}

onMounted(async () => {
  await currentUserStore.load()
  if (currentUserStore.currentUser?.commercialEnabled === true) {
    await refreshBillingAttention()
    attentionRefreshInterval = setInterval(() => void refreshBillingAttention(), 60_000)
  }
})

onUnmounted(() => {
  if (attentionRefreshInterval) {
    clearInterval(attentionRefreshInterval)
  }
})
</script>

<template>
  <div class="admin-shell">
    <aside class="admin-sidebar">
      <a class="admin-brand" href="/" title="Вернуться на основной сайт" @click="openMainSite">
        <span class="admin-brand__mark">O</span>
        <div>
          <strong>{{ projectBrand }}</strong>
          <small>Админ-панель</small>
        </div>
      </a>

      <div
        v-if="currentUserStore.currentUser"
        class="admin-user"
      >
        <span>{{ currentUserStore.currentUser.email }}</span>
        <small>{{ currentUserStore.currentUser.roles.join(', ') }}</small>
        <button
          class="admin-user__logout"
          type="button"
          :disabled="isLoggingOut"
          @click="logout"
        >
          {{ isLoggingOut ? 'Выход…' : 'Выйти' }}
        </button>
        <small v-if="logoutError" class="admin-user__error">{{ logoutError }}</small>
      </div>

      <button class="admin-theme-toggle" type="button" :aria-pressed="theme === 'dark'" @click="toggleTheme">
        {{ theme === 'dark' ? '☀ Светлая тема' : '☾ Тёмная тема' }}
      </button>

      <nav class="admin-navigation" aria-label="Основная навигация">
        <RouterLink
          v-for="item in visibleNavigationItems"
          :key="item.to"
          :to="item.to"
          class="admin-navigation__link"
        >
          {{ item.label }}
        </RouterLink>

        <template v-if="visibleFinanceNavigationItems.length > 0">
          <div class="admin-navigation__section">Финансы</div>
          <RouterLink
            v-for="item in visibleFinanceNavigationItems"
            :key="item.to"
            :to="item.to"
            class="admin-navigation__link"
          >
            {{ item.label }}
          </RouterLink>
        </template>

        <template v-if="visibleConfigurationNavigationItems.length > 0">
          <div class="admin-navigation__section">Конфигурация продукта</div>
          <RouterLink v-for="item in visibleConfigurationNavigationItems" :key="item.to" :to="item.to" class="admin-navigation__link">{{ item.label }}</RouterLink>
        </template>

        <template v-if="contentNavigationItems.some(item => canSee(item.permission))">
          <div class="admin-navigation__section">Контент</div>
          <RouterLink v-for="item in contentNavigationItems.filter(item => canSee(item.permission))" :key="item.to" :to="item.to" class="admin-navigation__link">{{ item.label }}</RouterLink>
        </template>

        <template v-if="communicationsNavigationItems.some(item => canSee(item.permission))">
          <div class="admin-navigation__section">Коммуникации</div>
          <RouterLink v-for="item in communicationsNavigationItems.filter(item => canSee(item.permission))" :key="item.to" :to="item.to" class="admin-navigation__link">{{ item.label }}</RouterLink>
        </template>

        <template v-if="visibleProductSections.length > 0">
          <div class="admin-navigation__section">Разделы продукта</div>
          <a
            v-for="section in visibleProductSections"
            :key="section.url"
            :href="section.url"
            class="admin-navigation__link"
          >
            {{ section.title }}
          </a>
        </template>

        <template v-if="currentUserStore.isSystem">
          <div class="admin-navigation__section">Система</div>
          <RouterLink
            to="/system/admin-roles"
            class="admin-navigation__link"
          >
            Административные роли
          </RouterLink>
          <RouterLink to="/system/test-data" class="admin-navigation__link">Тестовые данные</RouterLink>
          <RouterLink to="/system/crm-import" class="admin-navigation__link">Импорт из CRM</RouterLink>
        </template>

        <div
          v-else-if="currentUserStore.errorMessage"
          class="admin-navigation__error"
        >
          {{ currentUserStore.errorMessage }}
        </div>
      </nav>
    </aside>

    <main class="admin-content">
      <RouterLink
        v-if="currentUserStore.currentUser?.commercialEnabled === true && billingAttention && (billingAttention.overdueReceiptCount || billingAttention.failedActivationCount)"
        to="/finance/billing?tab=purchases"
        class="admin-billing-attention"
      >
        <strong>Финансы требуют внимания</strong>
        <span v-if="billingAttention.overdueReceiptCount">
          Без отметки о чеке более 24 часов: {{ billingAttention.overdueReceiptCount }}.
        </span>
        <span v-if="billingAttention.failedActivationCount">
          Оплаты с ошибкой активации: {{ billingAttention.failedActivationCount }}.
        </span>
        <span class="admin-billing-attention__action">Открыть покупки и чеки НПД →</span>
      </RouterLink>
      <RouterView />
    </main>
  </div>
</template>
