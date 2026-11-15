<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { UiButton, UiTooltip } from '@opencrm/ui'
import type { WorkspaceBillingData, WorkspacePlan } from '../api/workspaceAdmin'
import WorkspacePromoCodeSection from './WorkspacePromoCodeSection.vue'
import WorkspaceProOverview from './WorkspaceProOverview.vue'

const props = defineProps<{
  historyOnly?: boolean
  billing: WorkspaceBillingData
  isSaving: boolean
}>()

const emit = defineEmits<{
  extendPlan: []
  purchasePlan: [plan: WorkspacePlan]
  promoApplied: []
}>()

const availablePlans = computed(() => props.billing.availableBusinessPlans
  .filter(plan => !plan.isDemoPlan || props.billing.demoBusinessAvailable))
const isGiftAccess = computed(() => props.billing.currentSubscription?.sourceCode === 'promo')
const isFreePlan = computed(() => props.billing.effectivePlan?.group === 'workspace.free.v1')
const clientsLimit = computed(() => props.billing.workspaceFeatures.find(feature => feature.code === 'workspace.clients.limit')?.intValue)
const overviewOpen = ref(false)
const overviewPlan = ref<WorkspacePlan | null>(null)
const featuredPlan = computed(() => availablePlans.value.find(plan => !plan.isDefaultPlan && !plan.isDemoPlan))
const canPurchaseOverviewPlan = computed(() => Boolean(overviewPlan.value && props.billing.canManageBilling
  && canShowPurchaseButton(overviewPlan.value) && !purchaseUnavailableMessage.value))
// Подарочный доступ не считается активным платным тарифом — покупка доступна.
const hasActiveRegularPlan = computed(() => Boolean(props.billing.currentSubscription
  && props.billing.effectivePlan
  && !isGiftAccess.value
  && !props.billing.effectivePlan.isDemoPlan
  && !props.billing.effectivePlan.isDefaultPlan))
const testSubscriptionMessage = 'Тарифы для этого аккаунта назначаются в административной панели.'
const extensionUnavailableMessage = computed(() => {
  if (props.billing.canUseTestSubscriptions) {
    return testSubscriptionMessage
  }

  return props.billing.canExtendBusinessSubscription
    ? ''
    : props.billing.businessExtensionMessage || 'Продление откроется за 7 дней до окончания тарифа.'
})
const purchaseUnavailableMessage = computed(() => {
  if (props.billing.canUseTestSubscriptions) {
    return testSubscriptionMessage
  }

  return hasActiveRegularPlan.value
    ? `Другой тариф можно подключить после окончания текущего: ${formatDate(props.billing.currentSubscription?.endDate)}.`
    : ''
})

function formatDate(value?: string | null) {
  if (!value) {
    return ''
  }

  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString('ru-RU')
}

function formatMoney(value?: number | null) {
  if (!value) {
    return ''
  }

  return new Intl.NumberFormat('ru-RU').format(value)
}

function getPlanPrice(plan: { effectivePrice?: number, pricePerMonth?: number, price?: number }) {
  return plan.effectivePrice ?? plan.pricePerMonth ?? plan.price ?? null
}

function getPlanDurationText(durationDays?: number) {
  if (!durationDays) {
    return ''
  }

  if (durationDays >= 365) {
    return 'за год'
  }

  if (durationDays >= 28 && durationDays <= 31) {
    return 'в месяц'
  }

  return `на ${durationDays} дн.`
}

function canShowPurchaseButton(plan: WorkspacePlan) {
  // При подарочном доступе показываем кнопку даже для того же тарифа.
  if (isGiftAccess.value) {
    return true
  }

  return props.billing.effectivePlan?.id !== plan.id
}

function showPlanOverview(plan: WorkspacePlan) {
  overviewPlan.value = plan
  overviewOpen.value = true
}

function purchaseFromOverview(plan: WorkspacePlan) {
  if (!canPurchaseOverviewPlan.value || props.isSaving || plan.id !== overviewPlan.value?.id) return
  overviewOpen.value = false
  emit('purchasePlan', plan)
}

function pluralize(value: number, forms: [string, string, string]) {
  const count = Math.abs(value) % 100
  return forms[count >= 11 && count <= 14 ? 2 : count % 10 === 1 ? 0 : count % 10 >= 2 && count % 10 <= 4 ? 1 : 2]
}

function getPlanHighlights(plan: WorkspacePlan) {
  const enabled = new Set(plan.features?.filter(feature => feature.boolValue === true).map(feature => feature.code))
  return [
    ['workspace.maintenance.intake', 'Приёмка со схемой повреждений'],
    ['workspace.module.members', 'Команда и смены'],
    ['workspace.client_messaging', 'Сообщения и боты Telegram / MAX'],
    ['workspace.module.analytics', 'Статистика работы'],
    ['workspace.assistant', 'ИИ-помощник'],
    ['workspace.documents.styling', 'Оформление документов'],
    ['workspace.data.import', 'Импорт данных']
  ].filter(([code]) => enabled.has(code)).map(([, title]) => title).slice(0, 3)
}

watch(() => props.billing.organization.id, () => { overviewOpen.value = false; overviewPlan.value = null })
watch(() => props.historyOnly, () => { overviewOpen.value = false })
</script>

<template>
  <template v-if="historyOnly">
    <WorkspacePromoCodeSection v-if="billing.canManageBilling" :organization-id="billing.organization.id" :disabled="isSaving" history-only />
    <section v-else class="workspace-common-subpanel">
      <p role="status">Недостаточно прав для просмотра истории промокодов.</p>
      <a href="#billing">← К тарифам</a>
    </section>
  </template>
  <div v-else class="workspace-common-stack workspace-billing">
    <section class="workspace-billing-current">
      <div>
        <span>{{ isGiftAccess ? 'Подарочный доступ' : 'Текущий пакет' }}</span>
        <strong>{{ isFreePlan ? 'Бесплатный' : billing.effectivePlan?.name ?? 'Не подключен' }}</strong>
        <div v-if="billing.currentSubscription?.endDate" class="workspace-billing-expiry">
          <small>Активен до {{ formatDate(billing.currentSubscription.endDate) }}</small>
          <UiTooltip
            v-if="hasActiveRegularPlan"
            :text="['Продление вручную за 7 дней до окончания. Автопродления нет.', billing.businessExtensionMessage].filter(Boolean).join(' ')"
          >
            <button type="button" class="ui-field-label__help" aria-label="Условия продления тарифа">?</button>
          </UiTooltip>
        </div>
        <small v-if="isGiftAccess">При покупке тарифа оставшиеся подарочные дни добавятся к оплаченному сроку.</small>
      </div>
      <div class="workspace-billing-limits" aria-label="Лимиты тарифа">
        <span v-if="clientsLimit != null"><b>{{ clientsLimit >= 2147483647 ? 'Без ограничения' : clientsLimit }}</b> {{ clientsLimit >= 2147483647 ? 'клиентов' : pluralize(clientsLimit, ['клиент', 'клиента', 'клиентов']) }}</span>
        <span><b>{{ billing.workspaceVehicleLimit }}</b> авто</span>
        <span><b>{{ billing.workspaceMembersLimit }}</b> {{ pluralize(billing.workspaceMembersLimit, ['участник', 'участника', 'участников']) }}</span>
      </div>
    </section>

    <WorkspacePromoCodeSection
      v-if="billing.canManageBilling"
      :organization-id="billing.organization.id"
      :disabled="isSaving"
      @applied="$emit('promoApplied')"
    />

    <section class="workspace-common-subpanel">
      <header class="ui-section-header workspace-billing__plans-heading">
        <h3>Тарифы</h3>
        <UiButton v-if="featuredPlan" variant="secondary" @click="showPlanOverview(featuredPlan)">Что даёт PRO</UiButton>
      </header>
      <div class="workspace-plan-grid">
        <article
          v-for="plan in availablePlans"
          :key="plan.id ?? plan.name"
          class="workspace-plan-card"
          :class="{ 'workspace-plan-card--active': billing.effectivePlan?.id === plan.id }"
        >
          <span class="workspace-plan-card__eyebrow">Тариф организации</span>
          <div class="workspace-plan-card__head">
            <div>
              <strong>{{ plan.name }}</strong>
              <span v-if="plan.isDemoPlan">Демо-доступ</span>
              <span v-else-if="plan.isDefaultPlan">Базовый пакет</span>
            </div>
            <b v-if="billing.effectivePlan?.id === plan.id">Сейчас</b>
          </div>

          <div class="workspace-plan-card__price">
            <span
              v-if="plan.hasPromoDiscount && plan.originalEffectivePrice"
              class="workspace-plan-card__old-price"
            >
              <s>{{ formatMoney(plan.originalEffectivePrice) }} руб.</s>
            </span>
            <strong v-if="getPlanPrice(plan)">{{ formatMoney(getPlanPrice(plan)) }} руб.</strong>
            <strong v-else>Бесплатно</strong>
            <span v-if="getPlanPrice(plan)">{{ getPlanDurationText(plan.durationDays) }}</span>
          </div>

          <div v-if="plan.hasPromoDiscount" class="workspace-plan-card__meta">
            <span class="workspace-plan-card__promo">Промокод {{ plan.promoCode }}</span>
          </div>

          <ul v-if="getPlanHighlights(plan).length" class="workspace-plan-card__benefits">
            <li v-for="highlight in getPlanHighlights(plan)" :key="highlight">{{ highlight }}</li>
          </ul>
          <button type="button" class="workspace-plan-card__details" @click="showPlanOverview(plan)">Подробнее о тарифе</button>

          <div
            v-if="billing.canManageBilling && hasActiveRegularPlan && billing.effectivePlan?.id === plan.id"
            class="workspace-plan-card__action"
          >
            <UiTooltip
              :text="extensionUnavailableMessage"
            >
              <span :tabindex="extensionUnavailableMessage ? 0 : undefined">
                <UiButton
                  variant="secondary"
                  :disabled="isSaving || Boolean(extensionUnavailableMessage)"
                  @click="$emit('extendPlan')"
                >
                  Продлить
                </UiButton>
              </span>
            </UiTooltip>
          </div>

          <div
            v-if="billing.canManageBilling && canShowPurchaseButton(plan)"
            class="workspace-plan-card__action"
          >
            <UiTooltip
              :text="purchaseUnavailableMessage"
            >
              <span :tabindex="purchaseUnavailableMessage ? 0 : undefined">
                <UiButton
                  variant="secondary"
                  :disabled="isSaving || Boolean(purchaseUnavailableMessage)"
                  @click="$emit('purchasePlan', plan)"
                >
                  {{ plan.isDemoPlan ? 'Подключить демо' : 'Подключить' }}
                </UiButton>
              </span>
            </UiTooltip>
          </div>
        </article>
      </div>
    </section>

    <WorkspaceProOverview
      :open="overviewOpen"
      :plan="overviewPlan"
      :can-purchase="canPurchaseOverviewPlan"
      :is-saving="isSaving"
      @close="overviewOpen = false"
      @purchase-plan="purchaseFromOverview"
    />
  </div>
</template>

<style scoped>
.workspace-billing__plans-heading { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
.workspace-billing__plans-heading h3 { margin: 0; font-size: 15px; }
.workspace-billing__plans-heading :deep(.ui-button) { min-height: 32px; padding: 6px 12px; font-size: 12px; }
.workspace-plan-card__benefits { display: grid; gap: 9px; margin: 0; padding: 0; list-style: none; color: var(--crm-text, #172536); font-size: 13px; }
.workspace-plan-card__benefits li { display: flex; align-items: baseline; gap: 8px; }
.workspace-plan-card__benefits li::before { content: '✓'; color: var(--crm-accent, #e87532); font-weight: 700; }
.workspace-plan-card__details { justify-self: start; display: inline-flex; align-items: center; gap: 8px; padding: 0; border: 0; background: transparent; color: var(--crm-accent, #b84c14); font: inherit; font-size: 13px; cursor: pointer; }
.workspace-plan-card__details:hover { text-decoration: underline; }
.workspace-plan-card__details:focus-visible { outline: 2px solid var(--crm-accent, #e87532); outline-offset: 4px; }
</style>
