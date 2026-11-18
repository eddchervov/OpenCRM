<script setup lang="ts">
import { UiButton } from '@opencrm/ui'
import { translate as t } from '@opencrm/ui/i18n'
import { translateWorkspaceAccessMessage } from '../accessMessages'

withDefaults(defineProps<{
  message: string
  canManageBilling: boolean
  href?: string
  actionLabel?: string
}>(), {
  href: '#billing'
})
</script>

<template>
  <div class="workspace-access-notice" role="status">
    <span class="workspace-access-notice__icon" aria-hidden="true">i</span>
    <div class="workspace-access-notice__content">
      <strong>{{ t('access.readOnly') }}</strong>
      <span>{{ translateWorkspaceAccessMessage(message) }}</span>
    </div>
    <UiButton v-if="canManageBilling" class="workspace-access-notice__action" :href="href" variant="ghost">
      {{ actionLabel ?? t('access.choosePlan') }}
    </UiButton>
  </div>
</template>
