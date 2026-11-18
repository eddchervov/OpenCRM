<script setup lang="ts">
import { translate as t } from '@opencrm/ui/i18n'
import '@opencrm/workspace/workJournalMessages'
import { computed, ref } from 'vue'
import { UiButton } from '@opencrm/ui'
import { MaintenanceJournalSection, type WorkspaceContext } from '@opencrm/workspace'
import WorkEventsSection from './WorkEventsSection.vue'
const props = defineProps<{ context: WorkspaceContext }>()
const showEvents = ref(false)
const canViewMaintenances = computed(() => props.context.shell.menu?.some(item => ['maintenance', 'workspace'].includes(item.code) && item.isVisible))
</script>

<template>
  <div v-if="canViewMaintenances" class="journal-mode" role="group" :aria-label="t('workJournal.section')">
    <UiButton :variant="showEvents ? 'secondary' : 'primary'" :aria-pressed="!showEvents" @click="showEvents = false">{{ t('workJournal.maintenances') }}</UiButton>
    <UiButton :variant="showEvents ? 'primary' : 'secondary'" :aria-pressed="showEvents" @click="showEvents = true">{{ t('workJournal.events') }}</UiButton>
  </div>
  <MaintenanceJournalSection v-if="canViewMaintenances && !showEvents" :context="context" />
  <WorkEventsSection v-else :context="context" />
</template>

<style scoped>
.journal-mode { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }
.journal-mode :deep(.ui-button--primary) { background: #e87532; border-color: #e87532; color: #fff; }
</style>
