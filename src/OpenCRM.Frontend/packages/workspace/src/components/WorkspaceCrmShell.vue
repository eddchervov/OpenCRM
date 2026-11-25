<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, provide, ref, watch } from 'vue'
import { formatLocaleDate, getProjectBrand, pageBreadcrumbsKey, shouldShowProjectAttribution, toggleWorkspaceTheme, useLocalization, workspaceTheme, type ProfileLink } from '@opencrm/ui'
import type { WorkspaceContext } from '../types'
import { provideWorkspaceCurrency } from '../workspaceCurrency'
import type { WorkspaceBreadcrumbItem } from './WorkspaceBreadcrumbs.vue'
import { provideConversationUpdates } from '../composables/conversationUpdates'
import WorkspaceCrmNavigation, { type CrmSection } from './WorkspaceCrmNavigation.vue'
import InquiryActionReminders from './InquiryActionReminders.vue'
import WorkspaceAssistantButton from './WorkspaceAssistantButton.vue'
import WorkspaceProLock from './WorkspaceProLock.vue'
import WorkspaceProSectionPreview from './WorkspaceProSectionPreview.vue'
import { commercialModuleEnabledKey, proFeatureBillingAccessKey } from '../proFeatureAccess'
import type { ProFeatureKey } from '../proFeatureDetails'
import { withPathBase } from '../utils/pathBase.mjs'
import '../workspaceChromeMessages'

const projectBrand = getProjectBrand()
const showAttribution = shouldShowProjectAttribution()
const { t } = useLocalization()

const props = defineProps<{
  context: WorkspaceContext | null
  sections: CrmSection[]
  activeSection: string
  breadcrumbs: WorkspaceBreadcrumbItem[]
  profileLinks: ProfileLink[]
  displayName: string
}>()
provide(pageBreadcrumbsKey, computed(() => props.breadcrumbs.slice(1, -1)))
provideWorkspaceCurrency(computed(() => props.context?.organization.currencyCode ?? 'RUB'))
const commercialEnabled = computed(() => props.context?.workspaceAccess?.commercialEnabled === true)
provide(commercialModuleEnabledKey, commercialEnabled)
provide(proFeatureBillingAccessKey, computed(() => props.context?.organization.canManageBilling === true
  && props.context.shell.menu?.some(item => item.code === 'billing' && item.isVisible) === true))

const lockedSectionFeature = computed<ProFeatureKey | undefined>(() => {
  const features: Record<string, ProFeatureKey> = {
    statistics: 'statistics', employees: 'team', members: 'roles', shifts: 'shifts', messages: 'messages', 'my-work': 'myWork'
  }
  return features[props.activeSection]
})

const conversationUpdates = provideConversationUpdates(computed(() => props.context?.shell.scenario?.modules?.messaging !== false
  && props.sections.some(section => section.code === 'messages') ? props.context?.organization.id ?? null : null))
const isFreePlan = computed(() => commercialEnabled.value && props.context?.workspaceAccess?.isFreePlan === true)
const showMessageHistory = ref(false)
const navigationSections = computed(() => props.sections.map(section => {
  const access = props.context?.shell.menu?.find(item => item.code === section.code)
  return { ...section, isAvailable: access?.isAvailable, unavailableMessage: access?.unavailableMessage,
    unreadCount: section.code === 'messages' ? conversationUpdates.unreadCount.value : section.unreadCount }
}))
const lockedSection = computed(() => props.context?.shell.menu?.find(item =>
  item.isVisible && item.isAvailable === false
  && (item.code === props.activeSection || item.sectionCodes?.includes(props.activeSection))))
const lockedSectionMessage = computed(() => {
  if (!commercialEnabled.value) return t('workspaceChrome.featureDisabled')
  if (!isFreePlan.value) return t('workspaceChrome.proUnavailable')
  const messageKeys: Record<string, string> = {
    statistics: 'workspaceChrome.statisticsPro',
    employees: 'workspaceChrome.employeesPro',
    members: 'workspaceChrome.rolesPro',
    shifts: 'workspaceChrome.shiftsPro',
    messages: 'workspaceChrome.messagesPro',
    'my-work': 'workspaceChrome.myWorkPro'
  }
  return t(messageKeys[props.activeSection] ?? 'workspaceChrome.available')
})
const canSeeInquiryActions = computed(() => props.sections.some(section => section.code === 'inquiries'))

const emit = defineEmits<{ select: [code: string] }>()
const shell = ref<HTMLElement | null>(null)
const mobileMenu = ref<HTMLDialogElement | null>(null)
const menuButton = ref<HTMLButtonElement | null>(null)
const isMenuOpen = ref(false)
const isSidebarCompact = ref(false)
const sidebarStorageKey = 'opencrm.workspace.sidebar.compact'
const organizations = computed(() => props.context?.organizationSwitcher?.organizations ?? [])
const organizationName = computed(() => props.context?.organization.name ?? t('workspaceChrome.workspace'))
const organizationsUrl = computed(() => props.context?.organizationSwitcher?.manageUrl ?? withPathBase('/organizations'))
const avatar = computed(() => props.displayName.trim().slice(0, 1).toUpperCase())
const accountLinks = computed(() => props.profileLinks.filter(link => link.href !== withPathBase('/organizations')))
const requestToken = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
type SiteNotification = { id: string; title: string; body: string; createdAt: string; closedAt: string | null }
const notifications = ref<SiteNotification[]>([])
const activeNotifications = computed(() => notifications.value.filter(notification => !notification.closedAt))
let notificationTimer: ReturnType<typeof setInterval> | null = null
let previousOverflow = ''
let desktopQuery: MediaQueryList | null = null

async function loadNotifications() {
  try {
    const response = await fetch(withPathBase('/api/v1/notifications'), { credentials: 'same-origin' })
    if (!response.ok) return
    notifications.value = await response.json() as SiteNotification[]
  } catch {
    // При временной ошибке сети оставляем уже загруженные уведомления.
  }
}

async function closeNotification(notification: SiteNotification) {
  if (!requestToken) return
  const response = await fetch(withPathBase('/api/v1/notifications/' + encodeURIComponent(notification.id) + '/close'), {
    method: 'POST',
    credentials: 'same-origin',
    headers: { RequestVerificationToken: requestToken }
  })
  if (response.ok) notification.closedAt = new Date().toISOString()
}

function notificationDate(value: string) {
  return formatLocaleDate(value, { dateStyle: 'medium' })
}

function closeDropdowns(event?: Event) {
  shell.value?.querySelectorAll<HTMLDetailsElement>('details.crm-dropdown[open]').forEach(details => {
    if (!(event?.target instanceof Node) || !details.contains(event.target)) details.open = false
  })
}

function focusContent() {
  document.getElementById('crm-content')?.focus()
}

function toggleSidebar() {
  isSidebarCompact.value = !isSidebarCompact.value
  try {
    window.localStorage.setItem(sidebarStorageKey, isSidebarCompact.value ? 'true' : 'false')
  } catch {
    // Компактный режим продолжает работать в текущей вкладке без сохранения.
  }
}

function openMenu() {
  if (!mobileMenu.value || mobileMenu.value.open) return
  closeDropdowns()
  previousOverflow = document.body.style.overflow
  document.body.style.overflow = 'hidden'
  mobileMenu.value.showModal()
  isMenuOpen.value = true
}

function restorePage() {
  if (!isMenuOpen.value) return
  document.body.style.overflow = previousOverflow
  isMenuOpen.value = false
  menuButton.value?.focus()
}

function closeMenu() {
  mobileMenu.value?.close()
  restorePage()
}

function selectSection(code: string) {
  closeMenu()
  closeDropdowns()
  emit('select', code)
}

function handleEscape(event: KeyboardEvent) {
  if (event.key !== 'Escape') return
  const activeDropdown = (document.activeElement as HTMLElement | null)?.closest('details')
  closeDropdowns()
  activeDropdown?.querySelector('summary')?.focus()
}

function handleResize() {
  if (desktopQuery?.matches) closeMenu()
}

watch(() => [props.activeSection, props.context?.organization.id], () => { showMessageHistory.value = false; closeMenu(); closeDropdowns() })
onMounted(() => {
  try {
    isSidebarCompact.value = window.localStorage.getItem(sidebarStorageKey) === 'true'
  } catch {
    isSidebarCompact.value = false
  }
  document.addEventListener('click', closeDropdowns)
  document.addEventListener('keydown', handleEscape)
  window.addEventListener('hashchange', closeDropdowns)
  void loadNotifications()
  notificationTimer = setInterval(() => { void loadNotifications() }, 60_000)
  desktopQuery = window.matchMedia('(min-width: 901px)')
  desktopQuery.addEventListener('change', handleResize)
})
onBeforeUnmount(() => {
  document.removeEventListener('click', closeDropdowns)
  document.removeEventListener('keydown', handleEscape)
  window.removeEventListener('hashchange', closeDropdowns)
  if (notificationTimer) clearInterval(notificationTimer)
  desktopQuery?.removeEventListener('change', handleResize)
  if (isMenuOpen.value) document.body.style.overflow = previousOverflow
})
</script>

<template>
  <div ref="shell" class="crm-shell" :class="{ 'crm-shell--compact': isSidebarCompact, 'crm-shell--single-section': sections.length <= 1 }">
    <a class="crm-skip" href="#crm-content" @click.prevent="focusContent">{{ t('workspaceChrome.skip') }}</a>
    <aside v-if="sections.length > 1" class="crm-sidebar">
      <a class="crm-brand" :href="`#${sections[0]?.code ?? 'workspace'}`"><span class="crm-brand__mark">O</span><span>{{ projectBrand }}<span class="crm-brand__caption">{{ t('workspaceChrome.workspace') }}</span></span></a>
      <button type="button" class="crm-sidebar-toggle" :aria-label="isSidebarCompact ? t('workspaceChrome.help') : t('workspaceChrome.collapse')" :title="isSidebarCompact ? t('workspaceChrome.help') : t('workspaceChrome.collapse')" @click="toggleSidebar">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m14 7-5 5 5 5" /></svg>
      </button>
      <div class="crm-sidebar__scroll">
        <WorkspaceCrmNavigation v-if="context" :sections="navigationSections" :active-section="activeSection" :compact="isSidebarCompact" :show-pro-badges="isFreePlan" @select="selectSection" />
      </div>
      <a class="crm-sidebar__bottom" :href="withPathBase('/personal/support')">{{ t('workspaceChrome.support') }} <span aria-hidden="true">↗</span></a>
    </aside>

    <header class="crm-topbar">
      <button v-if="sections.length > 1" ref="menuButton" type="button" class="crm-icon-button crm-menu-button" :aria-label="t('workspaceChrome.openMenu')" aria-controls="crm-mobile-menu" :aria-expanded="isMenuOpen" @click="openMenu">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16 M4 12h16 M4 18h16" /></svg>
      </button>
      <div class="crm-organization">
        <details v-if="organizations.length > 1" class="crm-dropdown">
          <summary class="crm-organization__name" :title="organizationName"><span>{{ organizationName }}</span><svg class="crm-organization__chevron" viewBox="0 0 16 16" width="12" height="12" fill="none" aria-hidden="true"><path d="m4 6 4 4 4-4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" /></svg></summary>
          <div class="crm-dropdown__panel crm-dropdown__panel--left">
            <a v-for="organization in organizations" :key="organization.id" :href="organization.url" :aria-current="organization.id === context?.organization.id ? 'true' : undefined">
              <span>{{ organization.name }}<small>{{ organization.scenarioName }}</small></span>
              <span v-if="organization.id === context?.organization.id" aria-hidden="true">✓</span>
            </a>
          </div>
        </details>
        <span v-else class="crm-organization__name" :title="organizationName">{{ organizationName }}</span>
        <span class="crm-organization__scenario">{{ context?.shell.scenario?.title }}</span>
      </div>
      <div class="crm-topbar__actions">
        <WorkspaceAssistantButton
          v-if="context && context.shell.scenario?.modules?.assistantVisible !== false"
          :organization-id="context.organization.id"
          :scenario-code="context.organization.primaryScenarioCode || context.shell.scenario?.code"
          :is-locked="context.shell.scenario?.modules?.assistant === false"
        />
        <InquiryActionReminders
          v-if="context && canSeeInquiryActions"
          :organization-id="context.organization.id"
          :user-id="context.user.id"
        />
        <details class="crm-dropdown crm-notifications">
          <summary class="crm-icon-button crm-notifications__bell" :aria-label="t('workspaceChrome.notifications')" :title="t('workspaceChrome.notifications')">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4" /></svg>
            <span v-if="activeNotifications.length" class="crm-notifications__badge">{{ activeNotifications.length > 99 ? '99+' : activeNotifications.length }}</span>
          </summary>
          <div class="crm-dropdown__panel crm-notifications__panel">
            <strong>{{ t('workspaceChrome.notifications') }}</strong>
            <p v-if="!activeNotifications.length" class="crm-notifications__empty">{{ t('workspaceChrome.noNotifications') }}</p>
            <div v-else class="crm-notifications__list">
              <div v-for="notification in activeNotifications" :key="notification.id" class="crm-notifications__item">
                <b>{{ notification.title }}</b>
                <p>{{ notification.body }}</p>
                <small>{{ notificationDate(notification.createdAt) }}</small>
                <button type="button" @click="closeNotification(notification)">{{ t('workspaceChrome.close') }}</button>
              </div>
            </div>
            <a :href="withPathBase('/notifications')">{{ t('workspaceChrome.notificationHistory') }}</a>
          </div>
        </details>
        <details class="crm-dropdown crm-account">
          <summary class="crm-avatar" :aria-label="t('workspaceChrome.profile')" :title="displayName">{{ avatar }}</summary>
          <div class="crm-dropdown__panel">
            <div class="crm-account__name">{{ displayName }}</div>
            <a :href="organizationsUrl">{{ t('workspaceChrome.organizations') }}</a>
            <button type="button" @click="toggleWorkspaceTheme">
              {{ workspaceTheme === 'dark' ? t('workspaceChrome.lightTheme') : t('workspaceChrome.darkTheme') }}
            </button>
            <hr>
            <template v-for="link in accountLinks" :key="`${link.label}-${link.href}`">
              <hr v-if="link.kind === 'divider'">
              <form v-else-if="link.kind === 'logout'" method="post" :action="link.href">
                <input v-if="requestToken" type="hidden" name="__RequestVerificationToken" :value="requestToken">
                <button type="submit">{{ link.label }}</button>
              </form>
              <a v-else :href="link.href">{{ link.label }}</a>
            </template>
          </div>
        </details>
      </div>
    </header>

    <main id="crm-content" class="crm-content" tabindex="-1">
      <div v-if="lockedSection && activeSection === 'messages' && isFreePlan" class="crm-message-history">
        <button type="button" @click="showMessageHistory = !showMessageHistory">{{ showMessageHistory ? t('workspaceChrome.closeHistory') : t('workspaceChrome.savedMessages') }}</button>
      </div>
      <WorkspaceProLock v-if="lockedSection && context && !(activeSection === 'messages' && showMessageHistory)" :key="context.organization.id + ':' + activeSection" locked :message="lockedSectionMessage" :feature="lockedSectionFeature">
        <WorkspaceProSectionPreview :context="context" :section-code="activeSection" :title="lockedSection.title" />
      </WorkspaceProLock>
      <slot v-else />
      <footer v-if="showAttribution" class="crm-attribution">На базе <a href="https://github.com/eddchervov/OpenCRM" target="_blank" rel="noopener noreferrer">OpenCRM</a></footer>
    </main>

    <dialog v-if="sections.length > 1" id="crm-mobile-menu" ref="mobileMenu" class="crm-mobile-menu" :aria-label="t('workspaceChrome.mobileNavigation')" @close="restorePage" @click="($event.target === mobileMenu) && closeMenu()">
      <div class="crm-mobile-menu__head"><strong>{{ projectBrand }}</strong><button type="button" class="crm-icon-button" :aria-label="t('workspaceChrome.closeMenu')" autofocus @click="closeMenu">×</button></div>
      <div class="crm-mobile-menu__organization">{{ organizationName }}</div>
      <WorkspaceCrmNavigation v-if="context" :sections="navigationSections" :active-section="activeSection" :show-pro-badges="isFreePlan" @select="selectSection" />
      <a class="crm-sidebar__bottom" :href="organizationsUrl">{{ t('workspaceChrome.organizations') }} <span aria-hidden="true">↗</span></a>
    </dialog>
  </div>
</template>

<style scoped>
.crm-attribution { margin-top: 24px; padding-block: 16px; text-align: center; color: var(--crm-text-muted, #687386); font-size: 12px; }
.crm-attribution a { color: inherit; text-underline-offset: 3px; }
.crm-message-history { display: flex; justify-content: flex-end; margin-bottom: 10px; }
.crm-message-history button { padding: 6px 0; border: 0; background: transparent; color: var(--crm-text-muted, #687386); font: inherit; font-size: 13px; text-decoration: underline; cursor: pointer; }
.crm-shell {
  --crm-sidebar-width: 232px;
  --crm-header-height: 66px;
  --crm-border: var(--crm-line, #e8eaee);
  min-height: 100dvh;
  background: var(--crm-canvas, #f6f7f9);
  color: var(--crm-text, #242b36);
}

.crm-shell--compact {
  --crm-sidebar-width: 72px;
}

.crm-shell--single-section {
  --crm-sidebar-width: 0px;
}

.crm-sidebar, .crm-topbar, .crm-content {
  transition: width .18s ease, margin-left .18s ease;
}

.crm-sidebar-toggle {
  position: absolute;
  top: 72px;
  right: -14px;
  z-index: 2;
  display: grid;
  place-items: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: 1px solid var(--crm-border);
  border-radius: 50%;
  background: var(--crm-surface, #fff);
  color: var(--crm-text-muted, #667080);
  box-shadow: 0 3px 10px var(--crm-shadow, #27354b12);
  cursor: pointer;
}

.crm-sidebar-toggle svg {
  width: 15px;
  height: 15px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.8;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.crm-shell--compact .crm-sidebar-toggle svg {
  transform: rotate(180deg);
}

.crm-shell--compact .crm-brand {
  justify-content: center;
  padding-inline: 0;
}

.crm-shell--compact .crm-brand > span:last-child,
.crm-shell--compact .crm-sidebar__bottom {
  display: none;
}

.crm-shell *, .crm-shell *::before, .crm-shell *::after {
  box-sizing: border-box;
}

.crm-shell a, .crm-shell summary, .crm-shell button {
  -webkit-tap-highlight-color: transparent;
}

.crm-shell a:focus-visible, .crm-shell button:focus-visible, .crm-shell summary:focus-visible {
  outline: 2px solid #b8551e;
  outline-offset: 3px;
}

.crm-sidebar {
  position: fixed;
  inset: 0 auto 0 0;
  z-index: 30;
  display: flex;
  flex-direction: column;
  width: var(--crm-sidebar-width);
  background: var(--crm-surface, #fff);
  border-right: 1px solid var(--crm-border);
}

.crm-brand {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 89px;
  flex-shrink: 0;
  padding: 0 24px;
  color: var(--crm-text, #222934);
  text-decoration: none;
  font-size: 15px;
  font-weight: 800;
  letter-spacing: -.03em;
}

.crm-brand__mark {
  display: grid;
  place-items: center;
  width: 30px;
  height: 34px;
  border-radius: 9px;
  background: #e67337;
  color: #fff;
  font-size: 25px;
  font-weight: 800;
  font-style: italic;
}

.crm-brand__caption {
  display: block;
  margin-top: 3px;
  font-size: 9px;
  font-weight: 500;
  color: var(--crm-text-muted, #87909b);
  letter-spacing: .01em;
}

.crm-sidebar__scroll {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
}

.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:hover),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:focus-within) {
  width: 232px;
}

.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:hover) :deep(.crm-navigation__label),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:focus-within) :deep(.crm-navigation__label),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:hover) :deep(.crm-navigation a span),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:focus-within) :deep(.crm-navigation a span),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:hover) :deep(.crm-navigation summary span),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:focus-within) :deep(.crm-navigation summary span),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:hover) :deep(.crm-navigation__chevron),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:focus-within) :deep(.crm-navigation__chevron) {
  display: initial;
}

.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:hover) :deep(.crm-navigation a),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:focus-within) :deep(.crm-navigation a),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:hover) :deep(.crm-navigation summary),
.crm-shell--compact .crm-sidebar:has(.crm-sidebar__scroll:focus-within) :deep(.crm-navigation summary) {
  justify-content: flex-start;
  padding-inline: 12px;
}

.crm-sidebar__bottom {
  display: flex;
  justify-content: space-between;
  margin: 0 18px;
  padding: 18px 6px;
  border-top: 1px solid var(--crm-border);
  color: var(--crm-text-muted, #6a7380);
  font-size: 12px;
  text-decoration: none;
}

.crm-topbar {
  position: sticky;
  top: 0;
  z-index: 25;
  height: var(--crm-header-height);
  margin-left: var(--crm-sidebar-width);
  padding: 0 28px;
  display: flex;
  align-items: center;
  gap: 16px;
  background: var(--crm-surface, #fff);
  border-bottom: 1px solid var(--crm-border);
}

.crm-organization {
  min-width: 0;
  margin-right: auto;
}

.crm-organization__name {
  display: flex;
  align-items: center;
  gap: 12px;
  max-width: 40vw;
  min-width: 0;
  color: var(--crm-text, #252d39);
  font-size: 14px;
  font-weight: 700;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.crm-organization__name > span:first-child {
  overflow: hidden;
  text-overflow: ellipsis;
}

.crm-organization__name .crm-organization__chevron {
  width: 12px;
  height: 12px;
  display: block;
  flex-shrink: 0;
}

.crm-organization__scenario {
  display: block;
  margin-top: 3px;
  color: var(--crm-text-muted, #87909b);
  font-size: 11px;
}

.crm-topbar__actions {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
}

.crm-topbar svg {
  width: 17px;
  height: 17px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.7;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.crm-avatar {
  display: grid;
  place-items: center;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: color-mix(in srgb, var(--crm-action-accent) 14%, var(--crm-surface, #fff));
  color: var(--crm-action-accent);
  font-size: 13px;
  font-weight: 700;
}

.crm-dropdown {
  position: relative;
}

.crm-dropdown summary {
  list-style: none;
  cursor: pointer;
}

.crm-dropdown summary::-webkit-details-marker {
  display: none;
}

.crm-dropdown__panel {
  position: absolute;
  right: 0;
  top: calc(100% + 12px);
  z-index: 50;
  width: 248px;
  max-width: calc(100vw - 32px);
  max-height: min(440px, 75dvh);
  overflow: auto;
  padding: 6px;
  border: 1px solid var(--crm-border);
  border-radius: 12px;
  background: var(--crm-surface, #fff);
  box-shadow: 0 14px 40px var(--crm-shadow, #202b401f);
}

.crm-dropdown__panel--left {
  right: auto;
  left: 0;
  width: 300px;
}

.crm-dropdown__panel a, .crm-dropdown__panel button {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: 11px 12px;
  border: 0;
  border-radius: 7px;
  color: var(--crm-text, #333d4b);
  background: transparent;
  font: inherit;
  font-size: 13px;
  text-align: left;
  text-decoration: none;
  cursor: pointer;
  overflow-wrap: anywhere;
}

.crm-dropdown__panel a:hover, .crm-dropdown__panel button:hover {
  background: var(--crm-surface-soft, #f4f5f7);
}

.crm-dropdown__panel a[aria-current] {
  background: var(--crm-surface, #fff0e5);
  color: #a94b16;
}

.crm-dropdown__panel small {
  display: block;
  margin-top: 4px;
  color: var(--crm-text-muted, #7b8390);
  font-size: 11px;
}

.crm-dropdown__panel hr {
  margin: 5px 8px;
  border: 0;
  border-top: 1px solid var(--crm-border);
}

.crm-account__name {
  padding: 12px;
  color: var(--crm-text-muted, #828995);
  font-size: 12px;
  overflow-wrap: anywhere;
}

.crm-content {
  min-width: 0;
  margin-left: var(--crm-sidebar-width);
  padding: 22px 28px 36px;
  outline: none;
}

.crm-content :deep(.ui-card) {
  border-color: var(--crm-border);
  border-radius: 12px;
  box-shadow: 0 2px 5px var(--crm-shadow, #27354b03);
}

.crm-content :deep(.ui-card__header) {
  min-height: 56px;
  padding: 12px 18px;
  border-color: var(--crm-border);
  border-radius: 12px 12px 0 0;
  background: var(--crm-section-header-bg);
}

.crm-content :deep(.ui-card--page) {
  background: var(--crm-surface, #fff);
  border-color: var(--crm-line, #dfe3e8);
  box-shadow: 0 8px 24px var(--crm-shadow, #27354b0a);
}

.crm-content :deep(.ui-card--page > .ui-card__header) {
  background: var(--crm-form-header-bg);
  border-bottom-color: var(--crm-form-header-border);
  border-inline-start-color: var(--crm-header-accent);
}

.crm-content :deep(.ui-card--page > .ui-card__body) {
  background: var(--crm-surface, #fff);
  border-radius: 0 0 12px 12px;
}

.crm-content :deep(.ui-card__title h2) {
  color: var(--crm-text, #26303d);
  font-size: 18px;
  letter-spacing: -.02em;
}

.crm-content :deep(.ui-card--page > .ui-card__header h2) {
  font-size: 22px;
}

.crm-content :deep(.ui-card__title p) {
  color: var(--crm-text-muted, #7b8491);
}

.crm-content :deep(.ui-card__body) {
  padding: 18px;
}

.crm-content :deep(.ui-card__header .ui-button) {
  border-color: var(--crm-line, #e3e6eb);
}

.crm-content :deep(.scenario-analytics__panel) {
  box-shadow: none;
  border-color: var(--crm-border);
}

.crm-content :deep(.workspace-tariff-expiry) {
  margin-bottom: 18px;
}

.crm-icon-button {
  display: grid;
  place-items: center;
  flex-shrink: 0;
  width: 40px;
  height: 40px;
  padding: 0;
  border: 1px solid var(--crm-border);
  border-radius: 8px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text-muted, #535e6c);
  cursor: pointer;
  font-size: 27px;
}

.crm-menu-button {
  display: none;
}

.crm-mobile-menu {
  position: fixed;
  inset: 0 auto 0 0;
  width: min(310px, 88vw);
  max-width: none;
  height: 100dvh;
  max-height: none;
  margin: 0;
  padding: 0;
  border: 0;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #252d39);
}

.crm-mobile-menu::backdrop {
  background: var(--crm-backdrop, #16203380);
}

.crm-mobile-menu__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
}

.crm-mobile-menu__organization {
  padding: 0 24px 20px;
  font-size: 13px;
  color: var(--crm-text-muted, #6a7380);
  overflow-wrap: anywhere;
}

.crm-skip {
  position: fixed;
  top: -100px;
  left: 250px;
  z-index: 100;
  padding: 12px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #222);
}

.crm-skip:focus {
  top: 10px;
}

.crm-notifications__bell {
  position: relative;
  color: var(--crm-action-info);
  background: color-mix(in srgb, var(--crm-action-info) 9%, var(--crm-surface, #fff));
  border-color: color-mix(in srgb, var(--crm-action-info) 28%, var(--crm-border));
}
.crm-notifications__bell:hover, .crm-notifications[open] > summary {
  background: color-mix(in srgb, var(--crm-action-info) 17%, var(--crm-surface, #fff));
}
.crm-notifications__badge {
  position: absolute;
  top: -6px;
  right: -8px;
  min-width: 18px;
  padding: 2px 4px;
  border-radius: 12px;
  background: #d65322;
  color: #fff;
  font-size: 10px;
  line-height: 14px;
}
.crm-notifications__panel { width: min(360px, calc(100vw - 24px)); padding: 12px; }
.crm-notifications__panel > strong { display: block; padding: 6px 8px 12px; }
.crm-notifications__empty { padding: 8px; color: var(--crm-text-muted, #667080); }
.crm-notifications__list { max-height: min(320px, 50dvh); overflow-y: auto; overscroll-behavior: contain; }
.crm-notifications__item { padding: 12px 8px; border-top: 1px solid var(--crm-border); }
.crm-notifications__item b { overflow-wrap: anywhere; }
.crm-notifications__item p { margin: 6px 0; white-space: pre-wrap; overflow-wrap: anywhere; }
.crm-notifications__item small { color: var(--crm-text-muted, #667080); }
.crm-notifications__item button { margin-top: 8px; color: #b8551e; }
@media (max-width: 560px) {
  .crm-notifications__panel {
    position: fixed;
    top: calc(var(--crm-header-height) + 8px);
    right: 12px;
    left: 12px;
    width: auto;
    max-width: none;
    max-height: calc(100dvh - var(--crm-header-height) - 20px);
  }
}
@media (min-width: 1800px) {
  .crm-content {
    padding: 26px 36px 40px;
  }
}

@media (max-width: 1100px) {
  .crm-topbar {
    padding: 0 20px;
  }
  .crm-content {
    padding: 20px;
  }
}

@media (min-width: 901px) {
  .crm-brand {
    height: 68px;
  }

  .crm-sidebar__bottom {
    padding-block: 10px;
  }

  .crm-sidebar :deep(.crm-navigation) {
    gap: 10px;
    padding: 4px 10px 12px;
  }

  .crm-sidebar :deep(.crm-navigation a),
  .crm-sidebar :deep(.crm-navigation summary) {
    min-height: 34px;
    padding-block: 6px;
  }

  .crm-sidebar :deep(.crm-navigation__label) {
    margin-bottom: 3px;
  }
}

@media (min-width: 901px) and (max-height: 700px) {
  .crm-sidebar :deep(.crm-navigation__label) {
    display: none;
  }

  .crm-sidebar :deep(.crm-navigation__group) {
    gap: 1px;
  }

  .crm-sidebar :deep(.crm-navigation a),
  .crm-sidebar :deep(.crm-navigation summary) {
    min-height: 30px;
    padding-block: 5px;
  }
}

@media (max-width: 900px) {
  .crm-topbar, .crm-content {
    margin-left: 0;
  }
  .crm-shell {
    --crm-sidebar-width: 0px;
    --crm-header-height: 62px;
  }
  .crm-sidebar {
    display: none;
  }
  .crm-sidebar-toggle {
    display: none;
  }
  .crm-menu-button {
    display: grid;
  }
  .crm-topbar {
    gap: 12px;
    padding: 0 16px;
  }
  .crm-organization__name {
    max-width: 40vw;
  }
  .crm-skip {
    left: 16px;
  }
}

@media (max-width: 560px) {
  .crm-topbar {
    gap: 8px;
    padding: 0 12px;
  }
  .crm-topbar__actions {
    gap: 8px;
    margin-left: auto;
  }
  .crm-organization {
    display: none;
  }
  .crm-organization__name {
    max-width: 100%;
    font-size: 12px;
  }
  .crm-organization__scenario {
    display: none;
  }
  .crm-content {
    padding: 16px 12px 28px;
  }
  .crm-content :deep(.ui-card__header) {
    padding: 15px;
  }
  .crm-content :deep(.ui-card__body) {
    padding: 15px;
  }
  .crm-content :deep(.ui-card__title h2) {
    font-size: 17px;
  }
  .crm-content :deep(.ui-card--page > .ui-card__header h2) {
    font-size: 20px;
  }
  .crm-dropdown__panel--left {
    position: fixed;
    top: 62px;
    left: 12px;
    right: 12px;
    width: auto;
  }
}
</style>
