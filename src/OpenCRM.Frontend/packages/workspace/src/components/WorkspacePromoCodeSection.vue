<script setup lang="ts">
import { useLocalization, formattingCulture } from '@opencrm/ui/i18n'
import '../billingMessages'
import { computed, ref, watch } from 'vue'
import { UiButton, UiInput, UiPagination, UiLoader } from '@opencrm/ui'
import {
  applyWorkspacePromoCode,
  loadWorkspacePromoHistory,
  type WorkspacePromoApplication
} from '../api/workspacePromotions'

const { t } = useLocalization()

const props = defineProps<{
  organizationId: string
  timeZoneId?: string | null
  disabled: boolean
  historyOnly?: boolean
}>()

const emit = defineEmits<{
  applied: []
}>()

const codeValue = ref('')
const applyBusy = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
const history = ref<WorkspacePromoApplication[]>([])
const historyError = ref('')
const historyPage = ref(1)
const totalCount = ref(0)
const historyLoading = ref(false)
let organizationVersion = 0
let historyRequestVersion = 0

const trimmedCode = computed(() => codeValue.value.trim())
const canApply = computed(() => !props.disabled && !applyBusy.value && trimmedCode.value.length >= 3 && trimmedCode.value.length <= 64)

function formatDate(value?: string | null, includeTime = false) {
  if (!value) {
    return ''
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  return includeTime
    ? date.toLocaleString(formattingCulture.value, { dateStyle: 'short', timeStyle: 'short', timeZone: props.timeZoneId || 'UTC' })
    : date.toLocaleDateString(formattingCulture.value, { timeZone: props.timeZoneId || 'UTC' })
}

function getBenefitText(item: WorkspacePromoApplication) {
  if (item.benefitTypeCode === 'bonus_days' && item.bonusDays) {
    return t('billing.bonusDays', { count: item.bonusDays })
  }

  if (item.benefitTypeCode === 'percentage' && item.discountValue != null) {
    return new Intl.NumberFormat(formattingCulture.value, { style: 'percent', maximumFractionDigits: 2 }).format(item.discountValue / 100)
  }

  if (item.benefitTypeCode === 'fixed_amount' && item.discountValue != null) {
    return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: 'RUB', currencyDisplay: 'code' }).format(item.discountValue)
  }

  return ''
}

function getStatusText(item: WorkspacePromoApplication) {
  if (item.status === 'consumed') {
    return item.benefitTypeCode === 'bonus_days' ? t('billing.giftCredited') : t('billing.discountUsed')
  }

  if (item.status === 'claimed') {
    // Срок не отменяет уже созданный платеж.
    return t('billing.inPayment')
  }

  if (item.status === 'assigned' && item.benefitTypeCode !== 'bonus_days') {
    const deadline = Date.parse(item.discountValidUntil ?? '')
    if (Number.isFinite(deadline) && deadline <= Date.now()) {
      return t('billing.discountExpired')
    }
  }

  if (item.status === 'assigned') {
    return t('billing.assigned')
  }

  return item.status
}

function getSuccessMessage(item: WorkspacePromoApplication) {
  if (item.benefitTypeCode === 'bonus_days') {
    const days = item.bonusDays ? t('billing.bonusDays', { count: item.bonusDays }) : ''
    const until = item.accessUntil ? t('billing.accessUntil', { date: formatDate(item.accessUntil) }) : ''
    return t('billing.giftSuccess', { name: item.name, days, until })
  }

  const deadlineText = item.discountValidUntil
    ? t('billing.discountDeadline', { date: formatDate(item.discountValidUntil, true) })
    : t('billing.noDiscountDeadline')

  return t('billing.discountSuccess', { deadline: deadlineText })
}

async function loadHistory() {
  const organizationId = props.organizationId
  const currentOrganizationVersion = organizationVersion
  const currentHistoryRequestVersion = ++historyRequestVersion
  historyError.value = ''
  historyLoading.value = true

  try {
    const result = await loadWorkspacePromoHistory(organizationId, historyPage.value)

    if (currentOrganizationVersion !== organizationVersion || currentHistoryRequestVersion !== historyRequestVersion) {
      return
    }

    history.value = result.items
    totalCount.value = result.totalCount
    historyPage.value = result.page
  }
  catch (error) {
    if (currentOrganizationVersion !== organizationVersion || currentHistoryRequestVersion !== historyRequestVersion) {
      return
    }

    historyError.value = error instanceof Error ? error.message : t('billing.promoHistoryError')
  }
  finally {
    if (currentOrganizationVersion === organizationVersion && currentHistoryRequestVersion === historyRequestVersion) {
      historyLoading.value = false
    }
  }
}

async function applyPromoCode() {
  if (!canApply.value) {
    return
  }

  const organizationId = props.organizationId
  const currentOrganizationVersion = organizationVersion
  ++historyRequestVersion
  const code = trimmedCode.value
  applyBusy.value = true
  errorMessage.value = ''
  successMessage.value = ''

  try {
    const item = await applyWorkspacePromoCode(organizationId, code)

    if (currentOrganizationVersion !== organizationVersion) {
      return
    }

    successMessage.value = getSuccessMessage(item)
    codeValue.value = ''

    emit('applied')
  }
  catch (error) {
    if (currentOrganizationVersion !== organizationVersion) {
      return
    }

    errorMessage.value = error instanceof Error ? error.message : t('billing.promoError')
  }
  finally {
    if (currentOrganizationVersion === organizationVersion) {
      applyBusy.value = false
    }
  }
}

watch(() => props.organizationId, () => {
  ++organizationVersion
  codeValue.value = ''
  errorMessage.value = ''
  successMessage.value = ''
  history.value = []
  historyError.value = ''
  historyPage.value = 1
  totalCount.value = 0
  historyLoading.value = false
  applyBusy.value = false
}, { immediate: true })

watch(() => [props.organizationId, props.historyOnly, historyPage.value] as const, () => {
  if (props.historyOnly) void loadHistory()
}, { immediate: true })
</script>

<template>
  <section class="workspace-common-subpanel workspace-promo">
    <template v-if="!historyOnly">
      <header class="ui-section-header workspace-promo__heading">
        <h3><label for="workspace-promo-code">{{ t('billing.promo') }}</label></h3>
        <UiButton href="#billing?tab=promocodes" variant="secondary">{{ t('billing.history') }}</UiButton>
      </header>

      <form class="workspace-promo__form" @submit.prevent="applyPromoCode">
        <UiInput
          v-model="codeValue"
          id="workspace-promo-code"
          :placeholder="t('billing.enterCode')"
          :disabled="disabled || applyBusy"
          required
        />
        <UiButton type="submit" variant="secondary" :disabled="!canApply">
          {{ applyBusy ? t('billing.applying') : t('billing.apply') }}
        </UiButton>
      </form>
      <p class="workspace-promo__rule">
        <a href="/promo-code-rules" target="_blank" rel="noopener">{{ t('billing.promoRules') }}</a>
      </p>

      <p v-if="errorMessage" class="workspace-promo__error" role="alert" aria-live="assertive">{{ errorMessage }}</p>
      <p v-if="successMessage" class="workspace-promo__success" role="status" aria-live="polite">{{ successMessage }}</p>

    </template>
    <template v-else>
      <p><a href="#billing">{{ t('billing.backToPlans') }}</a></p>
      <UiLoader v-if="historyLoading" compact :text="t('billing.loadingHistory')" />
      <div v-else-if="historyError" role="alert"><p class="workspace-promo__error">{{ historyError }}</p><UiButton variant="secondary" @click="loadHistory">{{ t('billing.retry') }}</UiButton></div>
      <p v-else-if="!history.length">{{ t('billing.noPromos') }}</p>
      <div v-else class="workspace-promo__history">
        <div v-for="item in history" :key="item.id" class="workspace-promo__history-row">
          <span class="workspace-promo__history-code">{{ item.code }}</span>
          <span v-if="getBenefitText(item)">{{ getBenefitText(item) }}</span>
          <span>{{ getStatusText(item) }}</span>
          <span class="workspace-promo__history-date">{{ formatDate(item.appliedAt) }}</span>
          <span v-if="item.benefitTypeCode !== 'bonus_days'" class="workspace-promo__history-date">{{ item.discountValidUntil ? t('billing.newPaymentUntil', { date: formatDate(item.discountValidUntil, true) }) : t('billing.noUseDeadline') }}</span>
          <span v-if="item.benefitTypeCode === 'bonus_days' && item.accessUntil" class="workspace-promo__history-date">{{ t('billing.until', { date: formatDate(item.accessUntil) }) }}</span>
        </div>
      </div>
      <UiPagination v-model="historyPage" :total-items="totalCount" :items-per-page="20" :disabled="historyLoading" :label="t('billing.promoHistory')" />
    </template>
  </section>
</template>

<style scoped>
.workspace-promo__heading { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
.workspace-promo__heading h3 { margin: 0; font-size: 14px; }
.workspace-promo__heading :deep(.ui-button) { min-height: 30px; padding: 5px 12px; font-size: 12px; }

.workspace-promo__form {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 8px;
  align-items: end;
  max-width: 480px;
}

.workspace-promo__error {
  color: var(--crm-danger-text, #a12b24);
  margin: 8px 0 0;
}

.workspace-promo__success {
  color: var(--crm-text, #243041);
  margin: 8px 0 0;
}

.workspace-promo__history {
  margin-top: 12px;
}

.workspace-promo__history h4 {
  margin: 0 0 6px;
}

.workspace-promo__history-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding: 4px 0;
  border-bottom: 1px solid var(--crm-line, #ddd);
  font-size: 13px;
}

.workspace-promo__history-code {
  font-weight: 600;
  overflow-wrap: anywhere;
}

.workspace-promo__history-date {
  color: var(--crm-text-muted, #777);
}

.workspace-promo__rule {
  margin: 0;
  color: var(--crm-text-muted, #667085);
  font-size: 13px;
}

@media (max-width: 600px) {
  .workspace-promo__form {
    grid-template-columns: minmax(0, 1fr);
    max-width: none;
  }
}
</style>
