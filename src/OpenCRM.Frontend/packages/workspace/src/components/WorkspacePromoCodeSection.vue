<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { UiButton, UiInput, UiPagination, UiLoader } from '@opencrm/ui'
import {
  applyWorkspacePromoCode,
  loadWorkspacePromoHistory,
  type WorkspacePromoApplication
} from '../api/workspacePromotions'

const props = defineProps<{
  organizationId: string
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
    ? date.toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short' })
    : date.toLocaleDateString('ru-RU')
}

function getBenefitText(item: WorkspacePromoApplication) {
  if (item.benefitTypeCode === 'bonus_days' && item.bonusDays) {
    return `+${item.bonusDays} дн.`
  }

  if (item.benefitTypeCode === 'percentage' && item.discountValue != null) {
    return `${item.discountValue}%`
  }

  if (item.benefitTypeCode === 'fixed_amount' && item.discountValue != null) {
    return `${item.discountValue} ₽`
  }

  return ''
}

function getStatusText(item: WorkspacePromoApplication) {
  if (item.status === 'consumed') {
    return item.benefitTypeCode === 'bonus_days' ? 'Подарок начислен' : 'Скидка использована'
  }

  if (item.status === 'claimed') {
    // Срок не отменяет уже созданный платеж.
    return 'В платеже'
  }

  if (item.status === 'assigned' && item.benefitTypeCode !== 'bonus_days') {
    const deadline = Date.parse(item.discountValidUntil ?? '')
    if (Number.isFinite(deadline) && deadline <= Date.now()) {
      return 'Срок скидки истёк'
    }
  }

  if (item.status === 'assigned') {
    return 'Закреплён'
  }

  return item.status
}

function getSuccessMessage(item: WorkspacePromoApplication) {
  if (item.benefitTypeCode === 'bonus_days') {
    const days = item.bonusDays ? `+${item.bonusDays} дн.` : ''
    const until = item.accessUntil ? ` Доступ до ${formatDate(item.accessUntil)}.` : ''
    return `Подарок «${item.name}»: ${days}.${until}`
  }

  const deadlineText = item.discountValidUntil
    ? ` Используйте скидку при создании нового платежа до ${formatDate(item.discountValidUntil, true)}.`
    : ' Срок использования скидки не ограничен.'

  return `Промокод закреплён за этой организацией. Скидка действует при создании нового платежа; ранее созданные платежи сохраняют свою цену.${deadlineText}`
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

    historyError.value = error instanceof Error ? error.message : 'Не удалось загрузить историю промокодов.'
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

    errorMessage.value = error instanceof Error ? error.message : 'Не удалось применить промокод.'
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
        <h3><label for="workspace-promo-code">Промокод</label></h3>
        <UiButton href="#billing?tab=promocodes" variant="secondary">История</UiButton>
      </header>

      <form class="workspace-promo__form" @submit.prevent="applyPromoCode">
        <UiInput
          v-model="codeValue"
          id="workspace-promo-code"
          placeholder="Введите код"
          :disabled="disabled || applyBusy"
          required
        />
        <UiButton type="submit" variant="secondary" :disabled="!canApply">
          {{ applyBusy ? 'Применяем…' : 'Применить' }}
        </UiButton>
      </form>
      <p class="workspace-promo__rule">
        <a href="/promo-code-rules" target="_blank" rel="noopener">Правила использования промокодов</a>
      </p>

      <p v-if="errorMessage" class="workspace-promo__error" role="alert" aria-live="assertive">{{ errorMessage }}</p>
      <p v-if="successMessage" class="workspace-promo__success" role="status" aria-live="polite">{{ successMessage }}</p>

    </template>
    <template v-else>
      <p><a href="#billing">← К тарифам</a></p>
      <UiLoader v-if="historyLoading" compact text="Загружаем историю…" />
      <div v-else-if="historyError" role="alert"><p class="workspace-promo__error">{{ historyError }}</p><UiButton variant="secondary" @click="loadHistory">Повторить</UiButton></div>
      <p v-else-if="!history.length">Промокоды ещё не применялись.</p>
      <div v-else class="workspace-promo__history">
        <div v-for="item in history" :key="item.id" class="workspace-promo__history-row">
          <span class="workspace-promo__history-code">{{ item.code }}</span>
          <span v-if="getBenefitText(item)">{{ getBenefitText(item) }}</span>
          <span>{{ getStatusText(item) }}</span>
          <span class="workspace-promo__history-date">{{ formatDate(item.appliedAt) }}</span>
          <span v-if="item.benefitTypeCode !== 'bonus_days'" class="workspace-promo__history-date">{{ item.discountValidUntil ? `Для нового платежа до ${formatDate(item.discountValidUntil, true)}` : 'Без срока использования' }}</span>
          <span v-if="item.benefitTypeCode === 'bonus_days' && item.accessUntil" class="workspace-promo__history-date">до {{ formatDate(item.accessUntil) }}</span>
        </div>
      </div>
      <UiPagination v-model="historyPage" :total-items="totalCount" :items-per-page="20" :disabled="historyLoading" label="История промокодов" />
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
