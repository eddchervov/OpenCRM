<script setup lang="ts">
import { WorkspaceWorkdayBoard } from '@opencrm/workspace'
import type { DetailingWorkspaceContext } from '@/api/detailingBff'

defineProps<{ context: DetailingWorkspaceContext }>()
</script>

<template>
  <WorkspaceWorkdayBoard :context="context" show-setup />
</template>