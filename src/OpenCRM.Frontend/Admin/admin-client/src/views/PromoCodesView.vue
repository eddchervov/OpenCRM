<script setup lang="ts">
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminCatalogMessages'
import { computed, onMounted, ref } from 'vue'
import { ApiError } from '@/api/ApiError'
import {
  createAdminPromoCode,
  deleteAdminPromoCode,
  getAdminPromoCodes,
  getAdminPromoCodeUsages,
  setAdminPromoCodeStatus,
  updateAdminPromoCode
} from '@/api/adminPromoCodesApi'
import { getAdminSubscriptionPlans } from '@/api/adminSubscriptionPlansApi'
import type { AdminPromoCode } from '@/models/AdminPromoCode'
import type { AdminPromoCodeForm } from '@/models/AdminPromoCodeForm'
import type { AdminPromoCodeUsage } from '@/models/AdminPromoCodeUsage'
import type { AdminSubscriptionPlan } from '@/models/AdminSubscriptionPlan'

const { t } = useLocalization()

// Доступные типы бонуса для новых организационных промокодов.
const benefitOptions = computed(() => [
  { value: 'bonus_days', label: t('adminCatalog.giftPro') },
  { value: 'percentage', label: t('adminCatalog.percentDiscount') },
  { value: 'fixed_amount', label: t('adminCatalog.rubDiscount') }
])

// HIDDEN_FLEET: служебный редактор сохраняет область старых промокодов; это не выбор нового типа организации. См. docs/hidden-fleet-scenario.md.
const scenarioOptions = computed(() => [
  { value: 'detailing', label: t('adminCatalog.detailing') },
  { value: 'service-station', label: t('adminCatalog.service') },
  { value: 'fleet', label: t('adminCatalog.fleet') }
])

const promoCodes = ref<AdminPromoCode[]>([])
const plans = ref<AdminSubscriptionPlan[]>([])
const usages = ref<AdminPromoCodeUsage[]>([])
const selectedPromoCode = ref<AdminPromoCode | null>(null)
const form = ref<AdminPromoCodeForm>(createEmptyForm())
const search = ref('')
const status = ref('')
const isCreating = ref(false)
const isLoading = ref(false)
const isSaving = ref(false)
const errorMessage = ref<string | null>(null)
const successMessage = ref<string | null>(null)

// Тарифы для подарочных дней и выбора скидки: только реальные платные бизнес-тарифы.
const availablePaidPlans = computed(() => plans.value.filter(plan =>
  plan.planTypeCode === 'business' && plan.isActive && !plan.isDemoPlan && !plan.isDefaultPlan && plan.group !== 'workspace.free.v1'))

// Запись прежнего формата: не организационный код, редактировать бонусы нельзя.
const isLegacySelected = computed(() =>
  !isCreating.value && selectedPromoCode.value !== null && !selectedPromoCode.value.isOrganizationCode)

// Промокод уже применён организациями: тип бонуса, тарифы и направления заморожены.
const hasOrganizationApplications = computed(() =>
  !isCreating.value && selectedPromoCode.value?.isOrganizationCode === true
  && selectedPromoCode.value.organizationApplicationCount > 0)

const filteredPromoCodes = computed(() => {
  const query = search.value.trim().toLowerCase()

  return promoCodes.value.filter(promoCode => {
    const matchesStatus = status.value === '' || promoCode.isActive === (status.value === 'active')
    const matchesSearch = !query || promoCode.code.toLowerCase().includes(query) ||
      promoCode.name.toLowerCase().includes(query)

    return matchesStatus && matchesSearch
  })
})

onMounted(() => {
  void loadData()
})

async function loadData(selectedId?: number): Promise<void> {
  isLoading.value = true
  errorMessage.value = null

  try {
    const [loadedPromoCodes, loadedPlans] = await Promise.all([
      getAdminPromoCodes(),
      getAdminSubscriptionPlans()
    ])
    promoCodes.value = loadedPromoCodes
    plans.value = loadedPlans

    const nextPromoCode = promoCodes.value.find(item => item.id === selectedId)
    if (nextPromoCode) {
      await selectPromoCode(nextPromoCode, false)
    } else {
      closeEditor()
    }
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminCatalog.loadPromoError'))
  } finally {
    isLoading.value = false
  }
}

async function selectPromoCode(promoCode: AdminPromoCode, clearStatus = true): Promise<void> {
  selectedPromoCode.value = promoCode
  isCreating.value = false
  form.value = {
    code: promoCode.code,
    name: promoCode.name,
    description: promoCode.description,
    isActive: promoCode.isActive,
    validFrom: toInputDateTime(promoCode.validFrom),
    validUntil: toInputDateTime(promoCode.validUntil),
    maximumRedemptions: promoCode.maximumRedemptions,
    demoDurationDays: promoCode.demoDurationDays,
    discountTypeCode: promoCode.discountTypeCode,
    discountValue: promoCode.discountValue,
    discountValidityDays: promoCode.discountValidityDays ?? null,
    discountPaymentLimitPerUser: promoCode.discountPaymentLimitPerUser,
    appliesToAllPaidPlans: promoCode.appliesToAllPaidPlans,
    subscriptionPlanIds: [...promoCode.subscriptionPlanIds],
    bonusDays: promoCode.bonusDays,
    maximumOrganizationApplications: promoCode.maximumOrganizationApplications,
    appliesToAllScenarios: promoCode.appliesToAllScenarios,
    scenarioCodes: [...promoCode.scenarioCodes],
    giftTemplatePlanId: promoCode.giftTemplatePlanId
  }
  usages.value = []
  if (clearStatus) {
    clearMessages()
  }

  try {
    usages.value = await getAdminPromoCodeUsages(promoCode.id)
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminCatalog.loadUsageError'))
  }
}

function createPromoCode(): void {
  selectedPromoCode.value = null
  isCreating.value = true
  form.value = createEmptyForm()
  usages.value = []
  clearMessages()
}

function closeEditor(): void {
  selectedPromoCode.value = null
  isCreating.value = false
  usages.value = []
}

function validateForm(): string | null {
  if (isLegacySelected.value) {
    return null
  }

  if (form.value.discountTypeCode === 'bonus_days') {
    if (form.value.giftTemplatePlanId == null || Number(form.value.giftTemplatePlanId) < 1) {
      return t('adminCatalog.giftPlanRequired')
    }
    if (form.value.bonusDays == null || form.value.bonusDays < 1 || form.value.bonusDays > 3650) {
      return t('adminCatalog.giftDaysInvalid')
    }
  } else {
    if (form.value.discountValue == null || form.value.discountValue <= 0) {
      return t('adminCatalog.positiveDiscount')
    }
    if (form.value.discountTypeCode === 'percentage' && form.value.discountValue > 99) {
      return t('adminCatalog.percentInvalid')
    }
    if (form.value.subscriptionPlanIds.length === 0) {
      return t('adminCatalog.discountPlanRequired')
    }
    const validityDays = form.value.discountValidityDays
    if (validityDays != null && String(validityDays).trim() !== ''
      && (!Number.isInteger(Number(validityDays)) || Number(validityDays) < 1 || Number(validityDays) > 3650)) {
      return t('adminCatalog.discountDaysInvalid')
    }
  }

  if (!form.value.appliesToAllScenarios && form.value.scenarioCodes.length === 0) {
    return t('adminCatalog.scenarioRequired')
  }

  if (form.value.maximumOrganizationApplications && Number(form.value.maximumOrganizationApplications) < 1) {
    return t('adminCatalog.applicationLimitInvalid')
  }

  return null
}

async function savePromoCode(): Promise<void> {
  const validationError = validateForm()
  if (validationError) {
    errorMessage.value = validationError
    return
  }

  isSaving.value = true
  clearMessages()

  try {
    if (isCreating.value) {
      await createAdminPromoCode(normalizeForm())
    } else {
      await updateAdminPromoCode(selectedPromoCode.value!.id, normalizeForm())
    }

    successMessage.value = t('adminCatalog.promoSaved')
    await loadData()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminCatalog.savePromoError'))
  } finally {
    isSaving.value = false
  }
}

async function toggleStatus(promoCode: AdminPromoCode): Promise<void> {
  clearMessages()

  try {
    await setAdminPromoCodeStatus(promoCode.id, !promoCode.isActive)
    successMessage.value = t('adminCatalog.promoStatusUpdated')
    await loadData()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminCatalog.togglePromoError'))
  }
}

async function deletePromoCode(): Promise<void> {
  if (!selectedPromoCode.value || !window.confirm(t('adminCatalog.deletePromoConfirm', { code: selectedPromoCode.value.code }))) {
    return
  }

  isSaving.value = true
  clearMessages()

  try {
    await deleteAdminPromoCode(selectedPromoCode.value.id)
    selectedPromoCode.value = null
    successMessage.value = t('adminCatalog.promoDeleted')
    await loadData()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminCatalog.deletePromoError'))
  } finally {
    isSaving.value = false
  }
}

function normalizeForm(): AdminPromoCodeForm {
  if (isLegacySelected.value) {
    return normalizeLegacyForm()
  }

  const isGift = form.value.discountTypeCode === 'bonus_days'

  return {
    ...form.value,
    code: form.value.code.trim().toUpperCase(),
    name: form.value.name.trim(),
    description: form.value.description?.trim() || null,
    validFrom: toApiDateTime(form.value.validFrom),
    validUntil: toApiDateTime(form.value.validUntil),
    // Лимиты регистрации не настраиваются для организационных промокодов.
    maximumRedemptions: null,
    demoDurationDays: null,
    discountPaymentLimitPerUser: null,
    discountTypeCode: form.value.discountTypeCode,
    discountValue: isGift ? null : form.value.discountValue || null,
    // Пустое поле означает бессрочную скидку; введённый срок проверяется перед сохранением.
    discountValidityDays: isGift
      ? null
      : form.value.discountValidityDays != null && form.value.discountValidityDays > 0
        ? Number(form.value.discountValidityDays)
        : null,
    bonusDays: isGift ? form.value.bonusDays || null : null,
    giftTemplatePlanId: isGift ? Number(form.value.giftTemplatePlanId) : null,
    // Для подарка выбор тарифов не применяется, для скидки обязателен конкретный список.
    appliesToAllPaidPlans: isGift,
    subscriptionPlanIds: isGift ? [] : [...form.value.subscriptionPlanIds],
    maximumOrganizationApplications: form.value.maximumOrganizationApplications || null,
    appliesToAllScenarios: form.value.appliesToAllScenarios,
    scenarioCodes: form.value.appliesToAllScenarios ? [] : [...form.value.scenarioCodes]
  }
}

// Запись прежнего формата: сохраняем исходные поля бонусов без изменений.
function normalizeLegacyForm(): AdminPromoCodeForm {
  const source = selectedPromoCode.value!

  return {
    ...form.value,
    code: form.value.code.trim().toUpperCase(),
    name: form.value.name.trim(),
    description: form.value.description?.trim() || null,
    validFrom: toApiDateTime(form.value.validFrom),
    validUntil: toApiDateTime(form.value.validUntil),
    maximumRedemptions: source.maximumRedemptions,
    demoDurationDays: source.demoDurationDays,
    discountTypeCode: source.discountTypeCode,
    discountValue: source.discountValue,
    discountValidityDays: source.discountValidityDays ?? null,
    discountPaymentLimitPerUser: source.discountPaymentLimitPerUser,
    appliesToAllPaidPlans: source.appliesToAllPaidPlans,
    subscriptionPlanIds: [...source.subscriptionPlanIds],
    bonusDays: source.bonusDays,
    maximumOrganizationApplications: source.maximumOrganizationApplications,
    appliesToAllScenarios: source.appliesToAllScenarios,
    scenarioCodes: [...source.scenarioCodes],
    giftTemplatePlanId: source.giftTemplatePlanId
  }
}

function createEmptyForm(): AdminPromoCodeForm {
  return {
    code: '',
    name: '',
    description: null,
    isActive: true,
    validFrom: null,
    validUntil: null,
    maximumRedemptions: null,
    demoDurationDays: null,
    discountTypeCode: 'bonus_days',
    discountValue: null,
    discountValidityDays: null,
    discountPaymentLimitPerUser: null,
    appliesToAllPaidPlans: true,
    subscriptionPlanIds: [],
    bonusDays: 30,
    maximumOrganizationApplications: null,
    appliesToAllScenarios: true,
    scenarioCodes: [],
    giftTemplatePlanId: null
  }
}

function clearMessages(): void {
  errorMessage.value = null
  successMessage.value = null
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.message : fallback
}

function toInputDateTime(value: string | null): string | null {
  if (!value) {
    return null
  }

  const date = new Date(value)
  const offset = date.getTimezoneOffset() * 60_000

  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

function toApiDateTime(value: string | null): string | null {
  return value ? new Date(value).toISOString() : null
}

function formatDate(value: string | null): string {
  if (!value) {
    return t('adminCatalog.noLimit')
  }

  return new Intl.DateTimeFormat(formattingCulture.value, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value))
}

// Человекочитаемый статус применения организационного промокода.
function getUsageStatus(usage: AdminPromoCodeUsage): string {
  if (usage.applicationStatus === 'consumed') {
    return t('adminCatalog.benefitReceived')
  }

  if (usage.applicationStatus) {
    return t('adminCatalog.applied')
  }

  return '—'
}

// Доступность скидки для нового платежа: только денежные организационные промокоды.
function getUsageDiscountText(usage: AdminPromoCodeUsage): string {
  if (isLegacySelected.value || selectedPromoCode.value?.discountTypeCode === 'bonus_days') {
    return '—'
  }

  if (usage.applicationStatus === 'consumed') {
    return t('adminCatalog.used')
  }

  if (usage.applicationStatus === 'claimed') {
    return t('adminCatalog.inPayment')
  }

  return usage.discountValidUntil ? formatDate(usage.discountValidUntil) : t('adminCatalog.noExpiry')
}

// Краткое описание бонуса legacy-промокода (только для чтения).
function getLegacyBenefitText(promoCode: AdminPromoCode): string {
  if (promoCode.demoDurationDays != null) {
    return t('adminCatalog.legacyDemo', { count: promoCode.demoDurationDays })
  }

  if (promoCode.discountTypeCode === 'percentage') {
    return t('adminCatalog.discount', { amount: new Intl.NumberFormat(formattingCulture.value, { style: 'percent', maximumFractionDigits: 2 }).format((promoCode.discountValue ?? 0) / 100) })
  }

  if (promoCode.discountTypeCode === 'fixed_amount' || promoCode.discountTypeCode === 'fixed_price') {
    return t('adminCatalog.discount', { amount: new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: 'RUB', currencyDisplay: 'code' }).format(promoCode.discountValue ?? 0) })
  }

  return t('adminCatalog.noBenefit')
}

function getPlanLabel(plan: AdminSubscriptionPlan): string {
  return t('adminCatalog.planLabel', { name: plan.name, count: plan.durationDays })
}
</script>

<template>
  <section class="finance-page">
    <h1>{{ t('adminCatalog.promoTitle') }}</h1>
    <p class="muted">{{ t('adminCatalog.promoIntro') }}</p>

    <div class="finance-toolbar">
      <input v-model="search" type="search" :placeholder="t('adminCatalog.searchPromo')">
      <SearchSelect v-model="status" :options="[{ value: '', label: t('adminCatalog.allStatuses') }, { value: 'active', label: t('adminCatalog.activePlural') }, { value: 'inactive', label: t('adminCatalog.inactivePlural') }]" />
      <button class="primary-button" type="button" @click="createPromoCode">{{ t('adminCatalog.newPromo') }}</button>
    </div>

    <div v-if="errorMessage" class="form-message form-message--error">{{ errorMessage }}</div>
    <div v-if="successMessage" class="form-message form-message--success">{{ successMessage }}</div>
    <div v-if="isLoading && promoCodes.length === 0" class="panel-state">{{ t('adminCatalog.loading') }}</div>

    <div v-else class="finance-workspace">
      <div class="finance-table-panel">
        <div class="table-wrap">
          <table class="data-table finance-table">
            <thead><tr><th>{{ t('adminCatalog.promo') }}</th><th>{{ t('adminCatalog.applyUntil') }}</th><th>{{ t('adminCatalog.applications') }}</th><th>{{ t('adminCatalog.status') }}</th><th></th></tr></thead>
            <tbody>
              <tr v-for="promoCode in filteredPromoCodes" :key="promoCode.id" :class="{ selected: selectedPromoCode?.id === promoCode.id }" @click="selectPromoCode(promoCode)">
                <td><strong>{{ promoCode.code }}</strong><small>{{ promoCode.name }}</small></td>
                <td>{{ formatDate(promoCode.validUntil) }}</td>
                <td>
                  <template v-if="promoCode.isOrganizationCode">{{ promoCode.organizationApplicationCount }}<small v-if="promoCode.maximumOrganizationApplications">{{ t('adminCatalog.outOf', { count: promoCode.maximumOrganizationApplications }) }}</small></template>
                  <template v-else>{{ promoCode.redemptionCount }}<small v-if="promoCode.maximumRedemptions">{{ t('adminCatalog.outOf', { count: promoCode.maximumRedemptions }) }}</small><small>{{ t('adminCatalog.registrations') }}</small></template>
                </td>
                <td><span class="status-badge" :class="promoCode.isActive ? 'status-badge--success' : 'status-badge--problem'">{{ promoCode.isActive ? t('adminCatalog.active') : t('adminCatalog.inactive') }}</span></td>
                <td><button class="table-action" type="button" @click.stop="toggleStatus(promoCode)">{{ promoCode.isActive ? t('adminCatalog.disable') : t('adminCatalog.enable') }}</button></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div v-if="isCreating || selectedPromoCode" class="finance-editor finance-editor--wide">
        <form class="finance-form" @submit.prevent="savePromoCode">
          <div class="finance-editor__heading">
            <strong>{{ isCreating ? t('adminCatalog.newPromo') : selectedPromoCode?.code }}</strong>
            <span v-if="!isCreating && !isLegacySelected" class="status-badge status-badge--success">{{ t('adminCatalog.oncePerOrg') }}</span>
            <button type="button" :aria-label="t('adminCatalog.close')" @click="closeEditor">×</button>
          </div>
          <fieldset :disabled="isSaving || isLegacySelected">
            <label><span>{{ t('adminCatalog.code') }}</span><input v-model="form.code" maxlength="64" required></label>
            <label><span>{{ t('adminCatalog.name') }}</span><input v-model="form.name" maxlength="160" required></label>
            <label class="span-2"><span>{{ t('adminCatalog.description') }}</span><textarea v-model="form.description" maxlength="1000" rows="2"></textarea></label>
            <label><span>{{ t('adminCatalog.validFrom') }}</span><input v-model="form.validFrom" type="datetime-local"></label>
            <label><span>{{ t('adminCatalog.validUntil') }}</span><input v-model="form.validUntil" type="datetime-local"></label>

            <template v-if="isLegacySelected">
              <!-- Запись прежнего формата: бонусы только для чтения, без конвертации. -->
              <label><span>{{ t('adminCatalog.bonus') }}</span><input :value="getLegacyBenefitText(selectedPromoCode!)" readonly></label>
              <label><span>{{ t('adminCatalog.redemptionLimit') }}</span><input :value="selectedPromoCode!.maximumRedemptions ?? t('adminCatalog.noLimit')" readonly></label>
              <label class="span-2"><small>{{ t('adminCatalog.legacyPromo') }}</small></label>
            </template>

            <template v-else>
              <label><span>{{ t('adminCatalog.benefitType') }}</span><SearchSelect v-model="form.discountTypeCode" :options="benefitOptions" :disabled="hasOrganizationApplications" /></label>

              <template v-if="form.discountTypeCode === 'bonus_days'">
                <label><span>{{ t('adminCatalog.giftDays') }}</span><input v-model.number="form.bonusDays" type="number" min="1" max="3650" required></label>
                <label><span>{{ t('adminCatalog.giftPlan') }}</span><select v-model.number="form.giftTemplatePlanId" :disabled="hasOrganizationApplications" required><option :value="null" disabled>{{ t('adminCatalog.selectPlan') }}</option><option v-for="plan in availablePaidPlans" :key="plan.id" :value="plan.id">{{ getPlanLabel(plan) }}</option></select></label>
                <label class="span-2"><small>{{ t('adminCatalog.giftHelp') }}</small></label>
              </template>

              <template v-else>
                <label><span>{{ t('adminCatalog.discountValue') }}</span><input v-model.number="form.discountValue" type="number" min="0.01" step="0.01" :max="form.discountTypeCode === 'percentage' ? 99 : undefined" required></label>
                <label><span>{{ t('adminCatalog.discountDays') }}</span><input v-model.number="form.discountValidityDays" type="number" min="1" max="3650" step="1" :placeholder="t('adminCatalog.noLimit')"></label>
                <div class="span-2 plan-options">
                  <strong>{{ t('adminCatalog.discountPlans') }}</strong>
                  <label v-for="plan in availablePaidPlans" :key="plan.id" class="checkbox-field">
                    <input v-model="form.subscriptionPlanIds" type="checkbox" :value="plan.id" :disabled="hasOrganizationApplications"><span>{{ getPlanLabel(plan) }}</span>
                  </label>
                  <small>{{ t('adminCatalog.discountPlansHelp') }}</small>
                </div>
                <label class="span-2"><small>{{ t('adminCatalog.discountExpiryHelp') }}</small></label>
              </template>

              <label><span>{{ t('adminCatalog.applicationLimit') }}</span><input v-model.number="form.maximumOrganizationApplications" type="number" min="1"></label>
              <label class="span-2"><small>{{ t('adminCatalog.applicationLimitHelp') }}</small></label>

              <label class="checkbox-field span-2"><input v-model="form.appliesToAllScenarios" type="checkbox" :disabled="hasOrganizationApplications"><span>{{ t('adminCatalog.allScenarios') }}</span></label>
              <small v-if="hasOrganizationApplications" class="span-2">{{ t('adminCatalog.lockedPromoHelp') }}</small>
              <div v-if="!form.appliesToAllScenarios" class="span-2 plan-options">
                <label v-for="option in scenarioOptions" :key="option.value" class="checkbox-field">
                  <input v-model="form.scenarioCodes" type="checkbox" :value="option.value" :disabled="hasOrganizationApplications"><span>{{ option.label }}</span>
                </label>
              </div>
            </template>

            <label class="checkbox-field"><input v-model="form.isActive" type="checkbox"><span>{{ t('adminCatalog.active') }}</span></label>
          </fieldset>

          <div class="finance-actions">
            <button class="primary-button" type="submit" :disabled="isSaving || isLegacySelected">{{ t('adminCatalog.savePromo') }}</button>
            <button class="secondary-button" type="button" :disabled="isSaving" @click="closeEditor">{{ t('adminCatalog.cancel') }}</button>
            <button v-if="selectedPromoCode" class="danger-button" type="button" :disabled="isSaving" @click="deletePromoCode">{{ t('adminCatalog.delete') }}</button>
          </div>
        </form>

        <div v-if="selectedPromoCode" class="promo-usages">
          <strong>{{ t('adminCatalog.usageCount', { count: usages.length }) }}</strong>
          <div v-if="usages.length" class="table-wrap">
            <table class="data-table"><thead><tr><th>{{ t('adminCatalog.user') }}</th><th>{{ t('adminCatalog.organization') }}</th><th>{{ t('adminCatalog.date') }}</th><th>{{ t('adminCatalog.status') }}</th><th>{{ t('adminCatalog.discountAvailable') }}</th></tr></thead><tbody><tr v-for="usage in usages" :key="`${usage.sourceCode}-${usage.id}`"><td>{{ usage.email }}</td><td>{{ usage.organizationName || '—' }}</td><td>{{ formatDate(usage.redeemedAt) }}</td><td>{{ getUsageStatus(usage) }}</td><td>{{ getUsageDiscountText(usage) }}</td></tr></tbody></table>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
