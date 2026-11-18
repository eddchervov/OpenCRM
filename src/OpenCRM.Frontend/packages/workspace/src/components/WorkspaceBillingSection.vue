<script setup lang="ts">
import { useLocalization, formattingCulture } from '@opencrm/ui/i18n'
import '../billingMessages'
import { computed, ref, watch } from 'vue'
import { UiButton, UiTooltip } from '@opencrm/ui'
import type { WorkspaceBillingData, WorkspacePlan } from '../api/workspaceAdmin'
import WorkspacePromoCodeSection from './WorkspacePromoCodeSection.vue'
import WorkspaceProOverview from './WorkspaceProOverview.vue'

const { t } = useLocalization()

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
const testSubscriptionMessage = computed(() => t('billing.testAccount'))
const renewalMessage = computed(() => {
  if (!props.billing.currentSubscription) return t('billing.noActiveSubscription')
  if (props.billing.effectivePlan?.isDemoPlan) return t('billing.demoNotRenewable')
  if (props.billing.effectivePlan?.isDefaultPlan) return t('billing.baseNotRenewable')
  if (isGiftAccess.value) return t('billing.giftRenewal')
  const count = props.billing.businessDaysUntilExpiry
  if (Number.isFinite(count)) {
    return props.billing.canExtendBusinessSubscription
      ? t('billing.renewalAvailable', { count })
      : t('billing.renewalUpcoming', { count })
  }

  return props.billing.businessExtensionMessage || t('billing.extensionWindow')
})
const extensionUnavailableMessage = computed(() => {
  if (props.billing.canUseTestSubscriptions) {
    return testSubscriptionMessage.value
  }

  return props.billing.canExtendBusinessSubscription
    ? ''
    : renewalMessage.value
})
const purchaseUnavailableMessage = computed(() => {
  if (props.billing.canUseTestSubscriptions) {
    return testSubscriptionMessage.value
  }

  return hasActiveRegularPlan.value
    ? t('billing.otherPlanAfter', { date: formatDate(props.billing.currentSubscription?.endDate) })
    : ''
})

function formatDate(value?: string | null) {
  if (!value) {
    return ''
  }

  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString(formattingCulture.value, { timeZone: props.billing.organization.timeZoneId || 'UTC' })
}

function formatMoney(value?: number | null) {
  if (!value) {
    return ''
  }

  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: 'RUB', currencyDisplay: 'code', minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(value)
}

function getPlanPrice(plan: { effectivePrice?: number, pricePerMonth?: number, price?: number }) {
  return plan.effectivePrice ?? plan.pricePerMonth ?? plan.price ?? null
}

function getPlanDurationText(durationDays?: number) {
  if (!durationDays) {
    return ''
  }

  if (durationDays >= 365) {
    return t('billing.yearly')
  }

  if (durationDays >= 28 && durationDays <= 31) {
    return t('billing.monthly')
  }

  return t('billing.duration', { count: durationDays })
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

function getPlanHighlights(plan: WorkspacePlan) {
  const enabled = new Set(plan.features?.filter(feature => feature.boolValue === true).map(feature => feature.code))
  return [
    ['workspace.maintenance.intake', t('billing.intake')],
    ['workspace.module.members', t('billing.team')],
    ['workspace.client_messaging', t('billing.messaging')],
    ['workspace.module.analytics', t('billing.analytics')],
    ['workspace.assistant', t('billing.assistant')],
    ['workspace.documents.styling', t('billing.documents')],
    ['workspace.data.import', t('billing.import')]
  ].filter(([code]) => enabled.has(code)).map(([, title]) => title).slice(0, 3)
}

watch(() => props.billing.organization.id, () => { overviewOpen.value = false; overviewPlan.value = null })
watch(() => props.historyOnly, () => { overviewOpen.value = false })
</script>

<template>
  <template v-if="historyOnly">
    <WorkspacePromoCodeSection v-if="billing.canManageBilling" :organization-id="billing.organization.id" :time-zone-id="billing.organization.timeZoneId" :disabled="isSaving" history-only />
    <section v-else class="workspace-common-subpanel">
      <p role="status">{{ t('billing.historyForbidden') }}</p>
      <a href="#billing">{{ t('billing.backToPlans') }}</a>
    </section>
  </template>
  <div v-else class="workspace-common-stack workspace-billing">
    <section class="workspace-billing-current">
      <div>
        <span>{{ isGiftAccess ? t('billing.giftAccess') : t('billing.currentPackage') }}</span>
        <strong>{{ isFreePlan ? t('billing.freePlan') : billing.effectivePlan?.name ?? t('billing.notConnected') }}</strong>
        <div v-if="billing.currentSubscription?.endDate" class="workspace-billing-expiry">
          <small>{{ t('billing.activeUntil', { date: formatDate(billing.currentSubscription.endDate) }) }}</small>
          <UiTooltip
            v-if="hasActiveRegularPlan"
            :text="[t('billing.renewalHint'), renewalMessage].filter(Boolean).join(' ')"
          >
            <button type="button" class="ui-field-label__help" :aria-label="t('billing.renewalTerms')">?</button>
          </UiTooltip>
        </div>
        <small v-if="isGiftAccess">{{ t('billing.giftHint') }}</small>
      </div>
      <div class="workspace-billing-limits" :aria-label="t('billing.limits')">
        <span v-if="clientsLimit != null">{{ clientsLimit >= 2147483647 ? t('billing.unlimitedClients') : t('billing.clients', { count: clientsLimit }) }}</span>
        <span>{{ t('billing.vehicles', { count: billing.workspaceVehicleLimit }) }}</span>
        <span>{{ t('billing.members', { count: billing.workspaceMembersLimit }) }}</span>
      </div>
    </section>

    <WorkspacePromoCodeSection
      v-if="billing.canManageBilling"
      :organization-id="billing.organization.id"
      :time-zone-id="billing.organization.timeZoneId"
      :disabled="isSaving"
      @applied="$emit('promoApplied')"
    />

    <section class="workspace-common-subpanel">
      <header class="ui-section-header workspace-billing__plans-heading">
        <h3>{{ t('billing.plans') }}</h3>
        <UiButton v-if="featuredPlan" variant="secondary" @click="showPlanOverview(featuredPlan)">{{ t('billing.proBenefits') }}</UiButton>
      </header>
      <div class="workspace-plan-grid">
        <article
          v-for="plan in availablePlans"
          :key="plan.id ?? plan.name"
          class="workspace-plan-card"
          :class="{ 'workspace-plan-card--active': billing.effectivePlan?.id === plan.id }"
        >
          <span class="workspace-plan-card__eyebrow">{{ t('billing.organizationPlan') }}</span>
          <div class="workspace-plan-card__head">
            <div>
              <strong>{{ plan.name }}</strong>
              <span v-if="plan.isDemoPlan">{{ t('billing.demo') }}</span>
              <span v-else-if="plan.isDefaultPlan">{{ t('billing.base') }}</span>
            </div>
            <b v-if="billing.effectivePlan?.id === plan.id">{{ t('billing.current') }}</b>
          </div>

          <div class="workspace-plan-card__price">
            <span
              v-if="plan.hasPromoDiscount && plan.originalEffectivePrice"
              class="workspace-plan-card__old-price"
            >
              <s>{{ formatMoney(plan.originalEffectivePrice) }}</s>
            </span>
            <strong v-if="getPlanPrice(plan)">{{ formatMoney(getPlanPrice(plan)) }}</strong>
            <strong v-else>{{ t('billing.free') }}</strong>
            <span v-if="getPlanPrice(plan)">{{ getPlanDurationText(plan.durationDays) }}</span>
          </div>

          <div v-if="plan.hasPromoDiscount" class="workspace-plan-card__meta">
            <span class="workspace-plan-card__promo">{{ t('billing.promoCode', { code: plan.promoCode ?? '' }) }}</span>
          </div>

          <ul v-if="getPlanHighlights(plan).length" class="workspace-plan-card__benefits">
            <li v-for="highlight in getPlanHighlights(plan)" :key="highlight">{{ highlight }}</li>
          </ul>
          <button type="button" class="workspace-plan-card__details" @click="showPlanOverview(plan)">{{ t('billing.details') }}</button>

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
                  {{ t('billing.extend') }}
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
                  {{ plan.isDemoPlan ? t('billing.activateDemo') : t('billing.activate') }}
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
