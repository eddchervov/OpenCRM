<script setup lang="ts">
import { defineAsyncComponent } from 'vue'
import { UiCard } from '@opencrm/ui'
import type { WorkspaceWikiData } from '../api/workspaceAdmin'
import type { WorkspaceContext, WorkspaceAnalytics } from '../types'
import WorkspaceEmployeesSection from './WorkspaceEmployeesSection.vue'
import WorkspaceMembersSection from './WorkspaceMembersSection.vue'
import WorkspaceShiftsSection from './WorkspaceShiftsSection.vue'
import WorkspaceStatisticsSection from './WorkspaceStatisticsSection.vue'
import WorkspaceWikiSection from './WorkspaceWikiSection.vue'
import WorkspaceWorkSection from './WorkspaceWorkSection.vue'

const MessagesSection = defineAsyncComponent(() => import('./MessagesSection.vue'))

defineProps<{
  context: WorkspaceContext
  sectionCode: string
  title: string
}>()

const emptyAnalytics: WorkspaceAnalytics = {
  kpis: {
    activeVehicles: 0,
    clients: 0,
    plannedWorks: 0,
    inProgressWorks: 0,
    completedWorks14Days: 0,
    revenue14Days: 0,
    openDefects: 0,
    counterEntries14Days: 0,
    materialsLowStock: 0,
    materialsOutOfStock: 0,
    documents14Days: 0
  },
  maintenanceCreatedSeries: [],
  maintenanceCompletedSeries: [],
  revenueSeries: [],
  statusBreakdown: [],
  materialRiskBreakdown: []
}

const emptyWiki: WorkspaceWikiData = {
  articles: [],
  selectedArticle: null,
  searchQuery: '',
  hasSearchResults: false
}
</script>

<template>
  <MessagesSection v-if="sectionCode === 'messages'" :context="context" preview-only />
  <UiCard v-else page :title="title" class="workspace-pro-section-preview">
    <WorkspaceStatisticsSection
      v-if="sectionCode === 'statistics'"
      :statistics="{ analytics: emptyAnalytics }"
      preview-only
    />
    <WorkspaceEmployeesSection
      v-else-if="sectionCode === 'employees'"
      :context="context"
      preview-only
    />
    <WorkspaceMembersSection
      v-else-if="sectionCode === 'members'"
      :members="{ rolePermissionMatrix: [], canManageRoles: false }"
      preferred-role-code=""
      is-read-only
      :is-saving="false"
      :is-editing-permissions="false"
      :permission-drafts="{}"
      preview-only
    />
    <WorkspaceShiftsSection v-else-if="sectionCode === 'shifts'" :context="context" preview-only />
    <WorkspaceWikiSection
      v-else-if="sectionCode === 'wiki'"
      :wiki="emptyWiki"
      :selected-article="null"
      active-wiki-slug=""
      :is-article-loading="false"
      article-error-message=""
      preview-only
    />
    <WorkspaceWorkSection v-else-if="sectionCode === 'my-work'" :context="context" preview-only />
    <div v-else class="workspace-pro-section-preview__skeleton" aria-hidden="true">
      <span v-for="index in 6" :key="index"></span>
    </div>
  </UiCard>
</template>

<style scoped>
.workspace-pro-section-preview__skeleton { display: grid; gap: 12px; }
.workspace-pro-section-preview__skeleton span { height: 22px; border-radius: 6px; background: var(--crm-border, #dce2e9); opacity: .6; }
</style>
