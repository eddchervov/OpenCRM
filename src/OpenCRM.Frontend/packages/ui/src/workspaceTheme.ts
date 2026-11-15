import { computed, readonly, ref } from 'vue'
import './workspaceTheme.css'

type WorkspaceTheme = 'light' | 'dark'
const storageKey = 'opencrm.workspace.theme'
const currentTheme = ref<WorkspaceTheme>('light')
export const workspaceTheme = readonly(currentTheme)
export const workspaceChartColors = computed(() => currentTheme.value === 'dark'
  ? { text: '#b6bfce', grid: 'rgba(182, 191, 206, 0.16)', surface: '#1b2029' }
  : { text: '#667085', grid: 'rgba(17, 24, 39, 0.08)', surface: '#ffffff' })

export function syncWorkspaceThemeFromDocument() {
  currentTheme.value = document.documentElement.dataset.crmTheme === 'dark' ? 'dark' : 'light'
}

function applyTheme(value: string | null) {
  currentTheme.value = value === 'dark' ? 'dark' : 'light'
  document.documentElement.dataset.crmTheme = currentTheme.value
}

export function initializeWorkspaceTheme() {
  try {
    applyTheme(window.localStorage.getItem(storageKey))
  } catch {
    // Ограничение хранилища не должно мешать открытию рабочего пространства.
    applyTheme(null)
  }
  window.addEventListener('storage', event => {
    if (event.key === storageKey || event.key === null) applyTheme(event.newValue)
  })
}

export function toggleWorkspaceTheme() {
  applyTheme(currentTheme.value === 'dark' ? 'light' : 'dark')
  try {
    window.localStorage.setItem(storageKey, currentTheme.value)
  } catch {
    // В приватном режиме тема продолжает работать до закрытия страницы.
  }
}
