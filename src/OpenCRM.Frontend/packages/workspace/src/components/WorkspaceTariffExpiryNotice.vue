<script setup lang="ts">
import { formattingCulture, translate as t } from '@opencrm/ui/i18n'
import '../accessMessages'
import { computed } from 'vue'
import type { WorkspaceContext } from '../types'

const props = defineProps<{
  context: WorkspaceContext
}>()

const millisecondsPerDay = 24 * 60 * 60 * 1000

const subscription = computed(() => props.context.currentSubscription)
const plan = computed(() => subscription.value?.subscriptionPlan)
const expiryDate = computed(() => {
  const rawValue = subscription.value?.endDate
  if (!rawValue) {
    return null
  }

  const value = new Date(rawValue)
  return Number.isNaN(value.getTime()) ? null : value
})
const daysRemaining = computed(() => {
  if (!expiryDate.value) {
    return null
  }

  return Math.max(0, Math.ceil((expiryDate.value.getTime() - Date.now()) / millisecondsPerDay))
})
const isVisible = computed(() =>
  props.context.organization.canManageBilling === true
  && Boolean(subscription.value)
  && Boolean(expiryDate.value)
  && plan.value?.isDefaultPlan !== true
  && plan.value?.isDemoPlan !== true
  && daysRemaining.value !== null
  && daysRemaining.value <= 7
)
const formattedExpiryDate = computed(() =>
  expiryDate.value?.toLocaleDateString(formattingCulture.value, {
    timeZone: props.context.organization.timeZoneId || 'UTC',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric'
  }) ?? ''
)
const remainingText = computed(() => {
  const days = daysRemaining.value ?? 0
  return days === 0 ? t('access.expiresToday') : t('access.daysRemaining', { count: days })
})
</script>

<template>
  <aside v-if="isVisible" class="workspace-tariff-expiry" role="status">
    <div class="workspace-tariff-expiry__icon" aria-hidden="true">!</div>
    <div class="workspace-tariff-expiry__content">
      <strong>{{ t('access.expiresTitle') }}</strong>
      <span>
        {{ t('access.expires', { name: plan?.name ?? '', date: formattedExpiryDate }) }} {{ remainingText }}
        {{ t('access.baseMode') }}
      </span>
    </div>
    <a class="workspace-tariff-expiry__action" href="#billing">{{ t('access.renew') }}</a>
  </aside>
</template>
