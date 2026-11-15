<script setup lang="ts">
import { WorkspaceWorkdayBoard } from '@opencrm/workspace'
import type { FleetWorkspaceContext } from '@/api/fleetBff'

defineProps<{ context: FleetWorkspaceContext }>()
</script>

<template>
  <WorkspaceWorkdayBoard :context="context" />
</template>