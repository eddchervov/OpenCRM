<script setup lang="ts">
import { useLocalization } from '@opencrm/ui/i18n'
import '../pageHelpMessages'
// HIDDEN_FLEET: общие подсказки используют нейтральную терминологию техники. См. docs/hidden-fleet-scenario.md.
import { computed, ref } from 'vue'
import { UiButton, UiModal, UiTooltip } from '@opencrm/ui'
import { resolveWorkspaceWikiHref } from '../commonSections'

const props = withDefaults(defineProps<{
  sectionCode: string
  entityId?: string
  action?: string
  title?: string
}>(), {
  entityId: '',
  action: '',
  title: ''
})

type HelpContent = {
  title: string
  summary: string
  bullets: string[]
}

const { t } = useLocalization()

const isOpen = ref(false)

const wikiHref = computed(() => resolveWorkspaceWikiHref(props.sectionCode, props.entityId, props.action))
const help = computed<HelpContent>(() => {
  if (props.sectionCode === 'vehicles' && (props.entityId === 'new' || props.action === 'edit')) {
    return {
      title: props.title || t('pageHelp.vehicleForm_title'),
      summary: t('pageHelp.vehicleForm_summary'),
      bullets: [
        t('pageHelp.vehicleForm_bullets_item1'),
        t('pageHelp.vehicleForm_bullets_item2'),
        t('pageHelp.vehicleForm_bullets_item3'),
        t('pageHelp.vehicleForm_bullets_item4')
      ]
    }
  }

  if (props.sectionCode === 'maintenance' && (props.entityId === 'new' || props.action === 'edit' || !props.entityId)) {
    return {
      title: props.title || t('pageHelp.maintenanceForm_title'),
      summary: t('pageHelp.maintenanceForm_summary'),
      bullets: [
        t('pageHelp.maintenanceForm_bullets_item1'),
        t('pageHelp.maintenanceForm_bullets_item2'),
        t('pageHelp.maintenanceForm_bullets_item3'),
        t('pageHelp.maintenanceForm_bullets_item4')
      ]
    }
  }

  const bySection: Record<string, HelpContent> = {
    workspace: {
      title: props.title || t('pageHelp.workspace_title'),
      summary: t('pageHelp.workspace_summary'),
      bullets: [t('pageHelp.workspace_bullets_item1'), t('pageHelp.workspace_bullets_item2'), t('pageHelp.workspace_bullets_item3')]
    },
    visualization: {
      title: t('pageHelp.visualization_title'),
      summary: t('pageHelp.visualization_summary'),
      bullets: [t('pageHelp.visualization_bullets_item1'), t('pageHelp.visualization_bullets_item2'), t('pageHelp.visualization_bullets_item3')]
    },
    finance: {
      title: props.title || t('pageHelp.finance_title'),
      summary: t('pageHelp.finance_summary'),
      bullets: [t('pageHelp.finance_bullets_item1'), t('pageHelp.finance_bullets_item2'), t('pageHelp.finance_bullets_item3')]
    },
    statistics: {
      title: t('pageHelp.statistics_title'),
      summary: t('pageHelp.statistics_summary'),
      bullets: [t('pageHelp.statistics_bullets_item1'), t('pageHelp.statistics_bullets_item2'), t('pageHelp.statistics_bullets_item3')]
    },
    employees: {
      title: t('pageHelp.employees_title'),
      summary: t('pageHelp.employees_summary'),
      bullets: [t('pageHelp.employees_bullets_item1'), t('pageHelp.employees_bullets_item2'), t('pageHelp.employees_bullets_item3')]
    },
    'my-work': {
      title: t('pageHelp.my_work_title'),
      summary: t('pageHelp.my_work_summary'),
      bullets: [t('pageHelp.my_work_bullets_item1'), t('pageHelp.my_work_bullets_item2'), t('pageHelp.my_work_bullets_item3')]
    },
    shifts: {
      title: t('pageHelp.shifts_title'),
      summary: t('pageHelp.shifts_summary'),
      bullets: [t('pageHelp.shifts_bullets_item1'), t('pageHelp.shifts_bullets_item2'), t('pageHelp.shifts_bullets_item3')]
    },
    scheduler: {
      title: props.title || t('pageHelp.scheduler_title'),
      summary: t('pageHelp.scheduler_summary'),
      bullets: [t('pageHelp.scheduler_bullets_item1'), t('pageHelp.scheduler_bullets_item2'), t('pageHelp.scheduler_bullets_item3')]
    },
    clients: {
      title: props.title || t('pageHelp.clients_title'),
      summary: t('pageHelp.clients_summary'),
      bullets: [t('pageHelp.clients_bullets_item1'), t('pageHelp.clients_bullets_item2'), t('pageHelp.clients_bullets_item3')]
    },
    vehicles: {
      title: props.title || t('pageHelp.vehicles_title'),
      summary: t('pageHelp.vehicles_summary'),
      bullets: [t('pageHelp.vehicles_bullets_item1'), t('pageHelp.vehicles_bullets_item2'), t('pageHelp.vehicles_bullets_item3')]
    },
    defects: {
      title: props.title || t('pageHelp.defects_title'),
      summary: t('pageHelp.defects_summary'),
      bullets: [t('pageHelp.defects_bullets_item1'), t('pageHelp.defects_bullets_item2'), t('pageHelp.defects_bullets_item3')]
    },
    counters: {
      title: props.title || t('pageHelp.counters_title'),
      summary: t('pageHelp.counters_summary'),
      bullets: [t('pageHelp.counters_bullets_item1'), t('pageHelp.counters_bullets_item2'), t('pageHelp.counters_bullets_item3')]
    },
    materials: {
      title: props.title || t('pageHelp.materials_title'),
      summary: t('pageHelp.materials_summary'),
      bullets: [t('pageHelp.materials_bullets_item1'), t('pageHelp.materials_bullets_item2'), t('pageHelp.materials_bullets_item3')]
    },
    'maintenance-journal': {
      title: t('pageHelp.maintenance_journal_title'),
      summary: t('pageHelp.maintenance_journal_summary'),
      bullets: [
        t('pageHelp.maintenance_journal_bullets_item1'),
        t('pageHelp.maintenance_journal_bullets_item2'),
        t('pageHelp.maintenance_journal_bullets_item3'),
        t('pageHelp.maintenance_journal_bullets_item4')
      ]
    },
    'work-journal': {
      title: props.title || t('pageHelp.work_journal_title'),
      summary: t('pageHelp.work_journal_summary'),
      bullets: [t('pageHelp.work_journal_bullets_item1'), t('pageHelp.work_journal_bullets_item2'), t('pageHelp.work_journal_bullets_item3')]
    },
    organization: {
      title: props.title || t('pageHelp.organization_title'),
      summary: t('pageHelp.organization_summary'),
      bullets: [t('pageHelp.organization_bullets_item1'), t('pageHelp.organization_bullets_item2'), t('pageHelp.organization_bullets_item3')]
    },
    members: {
      title: props.title || t('pageHelp.members_title'),
      summary: t('pageHelp.members_summary'),
      bullets: [t('pageHelp.members_bullets_item1'), t('pageHelp.members_bullets_item2'), t('pageHelp.members_bullets_item3')]
    },
    catalog: {
      title: props.title || t('pageHelp.catalog_title'),
      summary: t('pageHelp.catalog_summary'),
      bullets: [t('pageHelp.catalog_bullets_item1'), t('pageHelp.catalog_bullets_item2'), t('pageHelp.catalog_bullets_item3')]
    },
    settings: {
      title: props.title || t('pageHelp.settings_title'),
      summary: t('pageHelp.settings_summary'),
      bullets: [t('pageHelp.settings_bullets_item1'), t('pageHelp.settings_bullets_item2'), t('pageHelp.settings_bullets_item3')]
    },
    billing: {
      title: props.title || t('pageHelp.billing_title'),
      summary: t('pageHelp.billing_summary'),
      bullets: [t('pageHelp.billing_bullets_item1'), t('pageHelp.billing_bullets_item2'), t('pageHelp.billing_bullets_item3')]
    },
    wiki: {
      title: props.title || t('pageHelp.wiki_title'),
      summary: t('pageHelp.wiki_summary'),
      bullets: [t('pageHelp.wiki_bullets_item1'), t('pageHelp.wiki_bullets_item2'), t('pageHelp.wiki_bullets_item3')]
    }
  }

  return bySection[props.sectionCode] ?? bySection.workspace
})
</script>

<template>
  <UiTooltip :text="t('pageHelp.tooltip')" placement="left">
    <button class="workspace-page-help" type="button" :aria-label="t('pageHelp.open')" @click="isOpen = true">?</button>
  </UiTooltip>

  <UiModal :open="isOpen" :title="help.title" @close="isOpen = false">
    <div class="workspace-page-help-modal">
      <p>{{ help.summary }}</p>
      <ul>
        <li v-for="item in help.bullets" :key="item">{{ item }}</li>
      </ul>
    </div>
    <template #footer>
      <UiButton variant="secondary" :href="wikiHref" @click="isOpen = false">{{ t('pageHelp.wiki') }}</UiButton>
      <UiButton type="button" @click="isOpen = false">{{ t('pageHelp.understood') }}</UiButton>
    </template>
  </UiModal>
</template>
