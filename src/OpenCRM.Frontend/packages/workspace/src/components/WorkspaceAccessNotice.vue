<script setup lang="ts">
import { UiButton } from '@opencrm/ui'

withDefaults(defineProps<{
  message: string
  canManageBilling: boolean
  href?: string
  actionLabel?: string
}>(), {
  href: '#billing',
  actionLabel: 'Выбрать тариф'
})
</script>

<template>
  <div class="workspace-access-notice" role="status">
    <span class="workspace-access-notice__icon" aria-hidden="true">i</span>
    <div class="workspace-access-notice__content">
      <strong>Режим просмотра</strong>
      <span>{{ message }}</span>
    </div>
    <UiButton v-if="canManageBilling" class="workspace-access-notice__action" :href="href" variant="ghost">
      {{ actionLabel }}
    </UiButton>
  </div>
</template>
