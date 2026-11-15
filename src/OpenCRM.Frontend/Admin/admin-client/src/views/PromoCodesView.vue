<script setup lang="ts">
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

// Доступные типы бонуса для новых организационных промокодов.
const benefitOptions = [
  { value: 'bonus_days', label: 'Подарочный PRO' },
  { value: 'percentage', label: 'Скидка в процентах' },
  { value: 'fixed_amount', label: 'Скидка в рублях' }
]

// HIDDEN_FLEET: служебный редактор сохраняет область старых промокодов; это не выбор нового типа организации. См. docs/hidden-fleet-scenario.md.
const scenarioOptions = [
  { value: 'detailing', label: 'Детейлинг' },
  { value: 'service-station', label: 'СТО' },
  { value: 'fleet', label: 'Автопарк' }
]

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
    errorMessage.value = getErrorMessage(error, 'Не удалось загрузить промокоды.')
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
    errorMessage.value = getErrorMessage(error, 'Не удалось загрузить применения промокода.')
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
      return 'Выберите тариф для подарочных дней.'
    }
    if (form.value.bonusDays == null || form.value.bonusDays < 1 || form.value.bonusDays > 3650) {
      return 'Подарочные дни должны быть в диапазоне от 1 до 3650.'
    }
  } else {
    if (form.value.discountValue == null || form.value.discountValue <= 0) {
      return 'Укажите положительный размер скидки.'
    }
    if (form.value.discountTypeCode === 'percentage' && form.value.discountValue > 99) {
      return 'Процент скидки не может превышать 99.'
    }
    if (form.value.subscriptionPlanIds.length === 0) {
      return 'Выберите хотя бы один тариф для скидки.'
    }
    const validityDays = form.value.discountValidityDays
    if (validityDays != null && String(validityDays).trim() !== ''
      && (!Number.isInteger(Number(validityDays)) || Number(validityDays) < 1 || Number(validityDays) > 3650)) {
      return 'Срок использования скидки должен быть целым числом от 1 до 3650 дней.'
    }
  }

  if (!form.value.appliesToAllScenarios && form.value.scenarioCodes.length === 0) {
    return 'Выберите хотя бы одно направление или включите все направления.'
  }

  if (form.value.maximumOrganizationApplications && Number(form.value.maximumOrganizationApplications) < 1) {
    return 'Общий лимит применений должен быть не меньше 1.'
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

    successMessage.value = 'Промокод сохранён.'
    await loadData()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, 'Не удалось сохранить промокод.')
  } finally {
    isSaving.value = false
  }
}

async function toggleStatus(promoCode: AdminPromoCode): Promise<void> {
  clearMessages()

  try {
    await setAdminPromoCodeStatus(promoCode.id, !promoCode.isActive)
    successMessage.value = 'Статус промокода обновлён.'
    await loadData()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, 'Не удалось изменить статус промокода.')
  }
}

async function deletePromoCode(): Promise<void> {
  if (!selectedPromoCode.value || !window.confirm(`Удалить промокод «${selectedPromoCode.value.code}»?`)) {
    return
  }

  isSaving.value = true
  clearMessages()

  try {
    await deleteAdminPromoCode(selectedPromoCode.value.id)
    selectedPromoCode.value = null
    successMessage.value = 'Промокод удалён.'
    await loadData()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, 'Не удалось удалить промокод.')
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
    return 'Без ограничения'
  }

  return new Intl.DateTimeFormat('ru-RU', {
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
    return 'Выгода получена'
  }

  if (usage.applicationStatus) {
    return 'Применён'
  }

  return '—'
}

// Доступность скидки для нового платежа: только денежные организационные промокоды.
function getUsageDiscountText(usage: AdminPromoCodeUsage): string {
  if (isLegacySelected.value || selectedPromoCode.value?.discountTypeCode === 'bonus_days') {
    return '—'
  }

  if (usage.applicationStatus === 'consumed') {
    return 'Использована'
  }

  if (usage.applicationStatus === 'claimed') {
    return 'В платеже'
  }

  return usage.discountValidUntil ? formatDate(usage.discountValidUntil) : 'Без срока'
}

// Краткое описание бонуса legacy-промокода (только для чтения).
function getLegacyBenefitText(promoCode: AdminPromoCode): string {
  if (promoCode.demoDurationDays != null) {
    return `Демо-доступ: ${promoCode.demoDurationDays}`
  }

  if (promoCode.discountTypeCode === 'percentage') {
    return `Скидка: ${promoCode.discountValue}%`
  }

  if (promoCode.discountTypeCode === 'fixed_amount' || promoCode.discountTypeCode === 'fixed_price') {
    return `Скидка: ${promoCode.discountValue} руб.`
  }

  return 'Без бонуса'
}

function getPlanLabel(plan: AdminSubscriptionPlan): string {
  return `${plan.name} (${plan.durationDays} дн.)`
}
</script>

<template>
  <section class="finance-page">
    <h1>Промокоды</h1>
    <p class="muted">Подарочный PRO и скидки для организаций. Код, срок и ограничения задаются здесь; автоматического демо нет. Free не используется для подарочной выдачи.</p>

    <div class="finance-toolbar">
      <input v-model="search" type="search" placeholder="Поиск по коду или названию">
      <SearchSelect v-model="status" :options="[{ value: '', label: 'Все статусы' }, { value: 'active', label: 'Активные' }, { value: 'inactive', label: 'Отключённые' }]" />
      <button class="primary-button" type="button" @click="createPromoCode">Новый промокод</button>
    </div>

    <div v-if="errorMessage" class="form-message form-message--error">{{ errorMessage }}</div>
    <div v-if="successMessage" class="form-message form-message--success">{{ successMessage }}</div>
    <div v-if="isLoading && promoCodes.length === 0" class="panel-state">Загрузка…</div>

    <div v-else class="finance-workspace">
      <div class="finance-table-panel">
        <div class="table-wrap">
          <table class="data-table finance-table">
            <thead><tr><th>Промокод</th><th>Применить до</th><th>Применения</th><th>Статус</th><th></th></tr></thead>
            <tbody>
              <tr v-for="promoCode in filteredPromoCodes" :key="promoCode.id" :class="{ selected: selectedPromoCode?.id === promoCode.id }" @click="selectPromoCode(promoCode)">
                <td><strong>{{ promoCode.code }}</strong><small>{{ promoCode.name }}</small></td>
                <td>{{ formatDate(promoCode.validUntil) }}</td>
                <td>
                  <template v-if="promoCode.isOrganizationCode">{{ promoCode.organizationApplicationCount }}<small v-if="promoCode.maximumOrganizationApplications">из {{ promoCode.maximumOrganizationApplications }}</small></template>
                  <template v-else>{{ promoCode.redemptionCount }}<small v-if="promoCode.maximumRedemptions">из {{ promoCode.maximumRedemptions }}</small><small> регистрации</small></template>
                </td>
                <td><span class="status-badge" :class="promoCode.isActive ? 'status-badge--success' : 'status-badge--problem'">{{ promoCode.isActive ? 'Активен' : 'Отключён' }}</span></td>
                <td><button class="table-action" type="button" @click.stop="toggleStatus(promoCode)">{{ promoCode.isActive ? 'Отключить' : 'Включить' }}</button></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div v-if="isCreating || selectedPromoCode" class="finance-editor finance-editor--wide">
        <form class="finance-form" @submit.prevent="savePromoCode">
          <div class="finance-editor__heading">
            <strong>{{ isCreating ? 'Новый промокод' : selectedPromoCode?.code }}</strong>
            <span v-if="!isCreating && !isLegacySelected" class="status-badge status-badge--success">Один раз на организацию навсегда</span>
            <button type="button" aria-label="Закрыть" @click="closeEditor">×</button>
          </div>
          <fieldset :disabled="isSaving || isLegacySelected">
            <label><span>Код</span><input v-model="form.code" maxlength="64" required></label>
            <label><span>Название</span><input v-model="form.name" maxlength="160" required></label>
            <label class="span-2"><span>Описание</span><textarea v-model="form.description" maxlength="1000" rows="2"></textarea></label>
            <label><span>Можно применить с</span><input v-model="form.validFrom" type="datetime-local"></label>
            <label><span>Можно применить до</span><input v-model="form.validUntil" type="datetime-local"></label>

            <template v-if="isLegacySelected">
              <!-- Запись прежнего формата: бонусы только для чтения, без конвертации. -->
              <label><span>Бонус</span><input :value="getLegacyBenefitText(selectedPromoCode!)" readonly></label>
              <label><span>Лимит активаций</span><input :value="selectedPromoCode!.maximumRedemptions ?? 'Без ограничения'" readonly></label>
              <label class="span-2"><small>Старый формат промокода. Создайте новый промокод с нужным типом бонуса вместо изменения этого.</small></label>
            </template>

            <template v-else>
              <label><span>Тип бонуса</span><SearchSelect v-model="form.discountTypeCode" :options="benefitOptions" :disabled="hasOrganizationApplications" /></label>

              <template v-if="form.discountTypeCode === 'bonus_days'">
                <label><span>Подарочные дни</span><input v-model.number="form.bonusDays" type="number" min="1" max="3650" required></label>
                <label><span>Тариф подарка</span><select v-model.number="form.giftTemplatePlanId" :disabled="hasOrganizationApplications" required><option :value="null" disabled>Выберите тариф</option><option v-for="plan in availablePaidPlans" :key="plan.id" :value="plan.id">{{ getPlanLabel(plan) }}</option></select></label>
                <label class="span-2"><small>Если подписка действует, дни добавятся к ней. Иначе включится подарочный доступ с возможностями выбранного тарифа.</small></label>
              </template>

              <template v-else>
                <label><span>Размер скидки</span><input v-model.number="form.discountValue" type="number" min="0.01" step="0.01" :max="form.discountTypeCode === 'percentage' ? 99 : undefined" required></label>
                <label><span>Использовать скидку после применения, дней</span><input v-model.number="form.discountValidityDays" type="number" min="1" max="3650" step="1" placeholder="Без ограничения"></label>
                <div class="span-2 plan-options">
                  <strong>Тарифы со скидкой</strong>
                  <label v-for="plan in availablePaidPlans" :key="plan.id" class="checkbox-field">
                    <input v-model="form.subscriptionPlanIds" type="checkbox" :value="plan.id" :disabled="hasOrganizationApplications"><span>{{ getPlanLabel(plan) }}</span>
                  </label>
                  <small>Скидка относится только к выбранным тарифам и применяется к одной оплате.</small>
                </div>
                <label class="span-2"><small>Пустое поле — скидка бессрочная и используется для одной оплаты. Истечение или отключение промокода не отзывает уже закреплённую скидку. Указанный срок считается с момента применения промокода.</small></label>
              </template>

              <label><span>Общий лимит применений</span><input v-model.number="form.maximumOrganizationApplications" type="number" min="1"></label>
              <label class="span-2"><small>По всем организациям; засчитывается при применении, без оплаты.</small></label>

              <label class="checkbox-field span-2"><input v-model="form.appliesToAllScenarios" type="checkbox" :disabled="hasOrganizationApplications"><span>Все направления</span></label>
              <small v-if="hasOrganizationApplications" class="span-2">Тип бонуса, тарифы и направления нельзя менять после применения промокода. Остальные поля доступны.</small>
              <div v-if="!form.appliesToAllScenarios" class="span-2 plan-options">
                <label v-for="option in scenarioOptions" :key="option.value" class="checkbox-field">
                  <input v-model="form.scenarioCodes" type="checkbox" :value="option.value" :disabled="hasOrganizationApplications"><span>{{ option.label }}</span>
                </label>
              </div>
            </template>

            <label class="checkbox-field"><input v-model="form.isActive" type="checkbox"><span>Активен</span></label>
          </fieldset>

          <div class="finance-actions">
            <button class="primary-button" type="submit" :disabled="isSaving || isLegacySelected">Сохранить промокод</button>
            <button class="secondary-button" type="button" :disabled="isSaving" @click="closeEditor">Отмена</button>
            <button v-if="selectedPromoCode" class="danger-button" type="button" :disabled="isSaving" @click="deletePromoCode">Удалить</button>
          </div>
        </form>

        <div v-if="selectedPromoCode" class="promo-usages">
          <strong>Применения: {{ usages.length }}</strong>
          <div v-if="usages.length" class="table-wrap">
            <table class="data-table"><thead><tr><th>Пользователь</th><th>Организация</th><th>Дата</th><th>Статус</th><th>Скидка доступна</th></tr></thead><tbody><tr v-for="usage in usages" :key="`${usage.sourceCode}-${usage.id}`"><td>{{ usage.email }}</td><td>{{ usage.organizationName || '—' }}</td><td>{{ formatDate(usage.redeemedAt) }}</td><td>{{ getUsageStatus(usage) }}</td><td>{{ getUsageDiscountText(usage) }}</td></tr></tbody></table>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
