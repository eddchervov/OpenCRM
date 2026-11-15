<script setup lang="ts">
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
  expiryDate.value?.toLocaleDateString('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric'
  }) ?? ''
)
const remainingText = computed(() => {
  const days = daysRemaining.value ?? 0
  if (days === 0) {
    return 'Оплаченный период заканчивается сегодня.'
  }

  if (days === 1) {
    return 'Остался 1 день.'
  }

  if (days >= 2 && days <= 4) {
    return `Осталось ${days} дня.`
  }

  return `Осталось ${days} дней.`
})
</script>

<template>
  <aside v-if="isVisible" class="workspace-tariff-expiry" role="status">
    <div class="workspace-tariff-expiry__icon" aria-hidden="true">!</div>
    <div class="workspace-tariff-expiry__content">
      <strong>Срок тарифа заканчивается</strong>
      <span>
        {{ plan?.name }} действует до {{ formattedExpiryDate }}. {{ remainingText }}
        После этой даты рабочее пространство перейдёт в базовый режим.
      </span>
    </div>
    <a class="workspace-tariff-expiry__action" href="#billing">Продлить тариф</a>
  </aside>
</template>