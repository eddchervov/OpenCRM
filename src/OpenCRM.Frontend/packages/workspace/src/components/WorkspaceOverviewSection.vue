<script setup lang="ts">
import type { WorkspaceOverviewData } from '../api/workspaceAdmin'

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
        <span>Организация</span>
        <strong>{{ organizationName }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>Сценарий</span>
        <strong>{{ scenarioTitle }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>Участники</span>
        <strong>{{ overview.organization.membersCount }} / {{ overview.workspaceMembersLimit }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>Автомобили</span>
        <strong>{{ overview.activeVehiclesCount }} / {{ overview.workspaceVehicleLimit }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>Клиенты</span>
        <strong>{{ overview.clientsCount }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>Архив авто</span>
        <strong>{{ overview.archivedVehiclesCount }}</strong>
      </article>
    </div>

    <div class="workspace-common-grid">
      <article class="workspace-common-tile">
        <span>Услуг всего</span>
        <strong>{{ overview.metrics.maintenanceTotalCount }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>Запланировано</span>
        <strong>{{ overview.metrics.maintenancePlannedCount }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>В работе</span>
        <strong>{{ overview.metrics.maintenanceInProgressCount }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>Выполнено</span>
        <strong>{{ overview.metrics.maintenanceCompletedCount }}</strong>
      </article>
      <article v-if="!isDetailingScenario" class="workspace-common-tile">
        <span>Неисправности</span>
        <strong>{{ overview.metrics.openDefectsCount }}</strong>
      </article>
      <article class="workspace-common-tile">
        <span>Сигналы</span>
        <strong>{{ overview.planning.attentionCount }}</strong>
      </article>
    </div>
  </div>
</template>
