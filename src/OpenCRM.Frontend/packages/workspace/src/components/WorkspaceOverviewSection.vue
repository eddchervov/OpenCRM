<script setup lang="ts">
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../sectionPagesMessages'
import type { WorkspaceOverviewData } from '../api/workspaceAdmin'

const { t } = useLocalization()

function formatCount(value: number) {
  return new Intl.NumberFormat(formattingCulture.value).format(value)
}

defineProps<{
  overview: WorkspaceOverviewData
  organizationName: string
  scenarioTitle: string
  isDetailingScenario?: boolean
}>()
</script>

<template>
  <div class="workspace-common-stack">
    <div class="workspace-common-grid">
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.organization') }}</span>
        <strong>{{ organizationName }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.scenario') }}</span>
        <strong>{{ scenarioTitle }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.members') }}</span>
        <strong>{{ formatCount(overview.organization.membersCount) }} / {{ formatCount(overview.workspaceMembersLimit) }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.vehicles') }}</span>
        <strong>{{ formatCount(overview.activeVehiclesCount) }} / {{ formatCount(overview.workspaceVehicleLimit) }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.clients') }}</span>
        <strong>{{ formatCount(overview.clientsCount) }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.archivedVehicles') }}</span>
        <strong>{{ formatCount(overview.archivedVehiclesCount) }}</strong>
      </article>
    </div>

    <div class="workspace-common-grid">
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.totalServices') }}</span>
        <strong>{{ formatCount(overview.metrics.maintenanceTotalCount) }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.planned') }}</span>
        <strong>{{ formatCount(overview.metrics.maintenancePlannedCount) }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.inProgress') }}</span>
        <strong>{{ formatCount(overview.metrics.maintenanceInProgressCount) }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.done') }}</span>
        <strong>{{ formatCount(overview.metrics.maintenanceCompletedCount) }}</strong>
      </article>
      <article v-if="!isDetailingScenario" class="workspace-common-tile">
        <span>{{ t('sectionPages.defects') }}</span>
        <strong>{{ formatCount(overview.metrics.openDefectsCount) }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>{{ t('sectionPages.alerts') }}</span>
        <strong>{{ formatCount(overview.planning.attentionCount) }}</strong>
      </article>
    </div>
  </div>
</template>
