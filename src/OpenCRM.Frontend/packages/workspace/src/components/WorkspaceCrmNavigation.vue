<script setup lang="ts">
import { computed } from 'vue'
import { useLocalization } from '@opencrm/ui'
import '../workspaceChromeMessages'
import { workspaceGroupKey, workspaceLabelKey } from '../workspaceChromeMessages'

export type CrmSection = { code: string, title: string, groupTitle?: string, targetCode?: string, unreadCount?: number, isAvailable?: boolean, unavailableMessage?: string | null }
const props = defineProps<{ sections: CrmSection[], activeSection: string, compact?: boolean, showProBadges?: boolean }>()
const emit = defineEmits<{ select: [code: string] }>()
const { t, te } = useLocalization()
function sectionTitle(section: CrmSection): string {
  const key = workspaceLabelKey(section.code)
  return te(key) ? t(key) : section.title
}

function groupTitle(title: string): string {
  const key = workspaceGroupKey(title)
  return key && te(key) ? t(key) : title
}

function sectionTooltip(section: CrmSection): string {
  const title = sectionTitle(section)
  if (section.isAvailable === false) {
    return props.showProBadges ? `${title} · ${t('workspaceChrome.availableInPro')}` : section.unavailableMessage || t('workspaceChrome.unavailable')
  }

  return props.compact ? `${title}${section.unreadCount ? ` · ${t('workspaceChrome.unread', { count: section.unreadCount })}` : ''}` : ''
}
const groups = computed(() => {
  const result = new Map<string, CrmSection[]>()
  const sections = props.sections.filter(section => section.code !== 'members')
  for (const section of sections) {
    const title = section.groupTitle ?? ''
    result.set(title, [...(result.get(title) ?? []), section])
  }
  return Array.from(result, ([title, sections]) => ({ title, sections }))
})
const icons: Record<string, string> = {
  workspace: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
  visualization: 'm3 7 9-5 9 5v10l-9 5-9-5V7z M3 7l9 5 9-5 M12 12v10',
  scheduler: 'M5 5h14v16H5z M8 3v4 M16 3v4 M5 10h14 M9 14h2 M14 14h2 M9 17h2',
  messages: 'M3 4h18v13H9l-6 4V4z M7 8h10 M7 12h7',
  clients: 'M16 21v-2a5 5 0 0 0-10 0v2 M11 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M18 4a4 4 0 0 1 0 8 M19 15a5 5 0 0 1 3 4v2',
  vehicles: 'M3 16v-5l3-6h12l3 6v5H3z M3 11h18 M6 16v3 M18 16v3 M6 13h2 M16 13h2',
  maintenance: 'M8 3h8v4H8z M8 5H5v16h14V5h-3 M8 11h8 M8 15h8',
  materials: 'm3 7 9-4 9 4v10l-9 4-9-4V7z M3 7l9 4 9-4 M12 11v10 M8 5l9 4',
  'work-journal': 'M4 5h16v16H4z M8 3v4 M16 3v4 M8 11h8 M8 15h5',
  finance: 'M4 5h15v4 M4 5v14h17V9H4 M16 13h5 M16 16h5',
  statistics: 'M4 20V10h4v10 M10 20V4h4v16 M16 20V7h4v13',
  shifts: 'M5 5h14v16H5z M8 3v4 M16 3v4 M5 10h14 M9 14h2 M14 14h2 M9 17h2',
  employees: 'M5 3h14v18H5z M9 7h6 M12 10a2 2 0 1 0 0 4 2 2 0 0 0 0-4 M8 19a4 4 0 0 1 8 0',
  members: 'M16 21v-2a5 5 0 0 0-10 0v2 M11 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M19 8v6 M16 11h6',
  settings: 'M4 7h16 M4 17h16 M9 4v6 M15 14v6',
  billing: 'M3 5h18v14H3z M3 10h18 M6 15h4',
  wiki: 'M12 6c-3-3-6-3-9-2v15c3-1 6-1 9 2 3-3 6-3 9-2V4c-3-1-6-1-9 2z M12 6v15',
  organization: 'M5 21V3h10v18 M15 9h4v12 M9 7h2 M9 11h2 M9 15h2 M3 21h18',
  defects: 'm12 3 10 18H2L12 3z M12 9v5 M12 17v1',
  counters: 'M3 17a9 9 0 1 1 18 0 M12 14l5-5 M5 17h14',
  catalog: 'M4 4h6v6H4z M14 4h6v6h-6z M4 14h6v6H4z M14 14h6v6h-6z'
}
</script>

<template>
  <nav class="crm-navigation" :class="{ 'crm-navigation--compact': compact }" :aria-label="t('common.workspace')">
    <div v-for="group in groups" :key="group.title" class="crm-navigation__group">
      <p v-if="group.title" class="crm-navigation__label">{{ groupTitle(group.title) }}</p>
      <template v-for="section in group.sections" :key="section.code">
        <a
          :href="`#${section.targetCode ?? section.code}`"
          :aria-current="section.code === activeSection || (section.code === 'employees' && activeSection === 'members') ? 'page' : undefined"
          :title="sectionTooltip(section) || undefined"
          :class="{ 'crm-navigation__locked': section.isAvailable === false }"
          @click.prevent="emit('select', section.targetCode ?? section.code)"
        >
          <svg viewBox="0 0 24 24" aria-hidden="true"><path :d="icons[section.code] ?? icons.catalog" /></svg>
          <span>{{ sectionTitle(section) }}</span>
          <small v-if="showProBadges && section.isAvailable === false" class="crm-navigation__pro" :aria-label="t('workspaceChrome.availableInPro')">PRO</small>
          <strong v-if="section.unreadCount" class="crm-navigation__badge" :aria-label="t('workspaceChrome.unread', { count: section.unreadCount })">{{ section.unreadCount > 99 ? '99+' : section.unreadCount }}</strong>
        </a>
      </template>
    </div>
  </nav>
</template>

<style scoped>
.crm-navigation {
  display: grid;
  gap: 22px;
  padding: 8px 12px 24px;
}
.crm-navigation__group {
  display: grid;
  gap: 3px;
}
.crm-navigation__label {
  margin: 0 12px 7px;
  color: var(--crm-text-muted, #8a919c);
  font-size: 10px;
  font-weight: 700;
  letter-spacing: .09em;
  text-transform: uppercase;
}
.crm-navigation a {
  display: flex;
  align-items: center;
  gap: 12px;
  min-height: 41px;
  padding: 8px 12px;
  box-sizing: border-box;
  border-radius: 8px;
  color: var(--crm-text, #535b68);
  font-size: 13px;
  font-weight: 550;
  line-height: 1.35;
  text-decoration: none;
}
.crm-navigation a:hover {
  color: var(--crm-text, #202631);
  background: var(--crm-surface-soft, #f2f3f5);
}
.crm-navigation a[aria-current] {
  color: #a94b16;
  background: var(--crm-surface, #fff0e5);
  font-weight: 700;
}
.crm-navigation svg {
  flex: 0 0 18px;
  width: 18px;
  height: 18px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.6;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.crm-navigation__badge { margin-left: auto; padding: 2px 6px; border-radius: 9px; color: #fff; background: var(--crm-accent, #b8551e); font-size: 10px; line-height: 1.4; }
.crm-navigation__locked { color: var(--crm-text-muted, #667085); }
.crm-navigation__pro { margin-left: auto; font-size: 10px; color: var(--crm-accent, #b8551e); }
.crm-navigation--compact .crm-navigation__pro { display: none; }
.crm-navigation--compact a { position: relative; }
.crm-navigation--compact .crm-navigation__badge { position: absolute; top: 1px; right: 0; padding: 1px 4px; }
.crm-navigation a:focus-visible {
  outline: 2px solid #b8551e;
  outline-offset: 2px;
}
.crm-navigation--compact {
  gap: 14px;
  padding-inline: 10px;
}
.crm-navigation--compact .crm-navigation__label,
.crm-navigation--compact a span {
  display: none;
}
.crm-navigation--compact a {
  justify-content: center;
  min-height: 44px;
  padding-inline: 0;
}
</style>
