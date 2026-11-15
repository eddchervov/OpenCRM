<script setup lang="ts">
import { MaintenanceJournalSection, type WorkspaceContext } from '@opencrm/workspace'
defineProps<{ context: WorkspaceContext }>()
</script>

<template>
  <MaintenanceJournalSection :context="context" />
</template>
