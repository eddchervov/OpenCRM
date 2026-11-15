<script setup lang="ts">
import { WorkspaceWorkdayBoard } from '@opencrm/workspace'
import type { AutoServiceWorkspaceContext } from '@/api/autoserviceBff'

defineProps<{ context: AutoServiceWorkspaceContext }>()
</script>

<template>
  <WorkspaceWorkdayBoard :context="context" show-setup />
</template>