<script setup lang="ts">
import WorkspaceNotificationChannel from './WorkspaceNotificationChannel.vue'

defineProps<{
  organizationId: string
  canWrite: boolean
}>()

defineEmits<{
  'busy-changed': [isBusy: boolean]
}>()
</script>

<template>
  <WorkspaceNotificationChannel
    channel="max"
    :organization-id="organizationId"
    :can-write="canWrite"
    @busy-changed="$emit('busy-changed', $event)"
  />
</template>
