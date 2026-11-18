<script setup lang="ts">
import { formattingCulture, translate as t } from '@opencrm/ui/i18n'
import '../paymentMessages'
import { useWorkspaceCurrency } from '../workspaceCurrency'

import { computed, ref } from 'vue'
import { UiButton, UiMoneyInput, UiSelect, UiTextarea } from '@opencrm/ui'
import { loadMaintenancePayments, saveMaintenanceCommerce, type WorkspaceMaintenance, type WorkspaceMaintenancePayment } from '../api/maintenance'
import { loadClientOptions, type WorkspaceClientOption } from '../api/clients'

const workspaceCurrency = useWorkspaceCurrency()

const props = defineProps<{ organizationId: string; maintenance: WorkspaceMaintenance; disabled?: boolean; timeZoneId?: string | null }>()
const emit = defineEmits<{ changed: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const mode = ref<'payment' | 'history' | 'cancel'>('payment')
const amount = ref('')
const methodCode = ref('')
const otherPayer = ref(false)
const payerClientId = ref('')
const clients = ref<WorkspaceClientOption[]>([])
const comment = ref('')
const reason = ref('')
const error = ref('')
const historyError = ref('')
const clientError = ref('')
const busy = ref(false)
const loading = ref(false)
const payments = ref<WorkspaceMaintenancePayment[]>([])
const cancelling = ref<WorkspaceMaintenancePayment | null>(null)
let requestId = ''
let openRequest = 0

const canManage = computed(() => !props.disabled && props.maintenance.canManagePayments === true)
const canPay = computed(() => canManage.value && props.maintenance.debtAmount > 0 && props.maintenance.status !== 4)
const amountValue = computed(() => Number(amount.value))
const remainingAfterPayment = computed(() => Math.max(0, props.maintenance.debtAmount - (Number.isFinite(amountValue.value) ? amountValue.value : 0)))
const money = (value: number, currencyCode = props.maintenance.currencyCode ?? workspaceCurrency.value) => value.toLocaleString(formattingCulture.value, { style: 'currency', currency: currencyCode })
const date = (value: string) => new Date(value).toLocaleString(formattingCulture.value, {
  dateStyle: 'short', timeStyle: 'short', timeZone: props.timeZoneId || 'UTC'
})
const methodOptions = [
  { value: 'cash', label: t('payments.cash') },
  { value: 'card', label: t('payments.card') },
  { value: 'bank', label: t('payments.bank') },
  { value: 'other', label: t('payments.other') }
]
const methodName = (code?: string | null) => methodOptions.find(option => option.value === code)?.label ?? t('payments.noMethod')
const payerOptions = computed(() => clients.value.map(client => ({ value: client.id, label: client.name })))
const buyerName = computed(() => props.maintenance.buyerClientId ? props.maintenance.buyerName || t('payments.unspecified') : t('payments.unspecified'))
const submitLabel = computed(() => {
  if (busy.value) return t('payments.saving')
  if (mode.value !== 'payment') return t('payments.confirmCancellation')
  if (!Number.isFinite(amountValue.value) || amountValue.value <= 0 || amountValue.value > props.maintenance.debtAmount) return t('payments.recordPayment')

  return t('payments.recordAmount', { amount: money(amountValue.value) })
})

function showDialog() {
  if (dialog.value && !dialog.value.open) dialog.value.showModal()
}

async function loadHistory(request: number) {
  loading.value = true
  historyError.value = ''
  try {
    const result = await loadMaintenancePayments(props.organizationId, props.maintenance.id)
    if (request !== openRequest) return
    payments.value = result
  } catch (e) {
    if (request !== openRequest) return
    historyError.value = e instanceof Error ? e.message : t('payments.loadError')
  } finally {
    if (request === openRequest) loading.value = false
  }
}

async function loadClients(request: number) {
  clientError.value = ''
  try {
    const result = await loadClientOptions(props.organizationId)
    if (request !== openRequest) return
    clients.value = result
  } catch (e) {
    if (request !== openRequest) return
    clientError.value = e instanceof Error ? e.message : t('payments.clientsError')
  }
}

async function history() {
  if (busy.value) return
  openRequest += 1
  const request = openRequest
  mode.value = 'history'
  error.value = ''
  showDialog()
  await loadHistory(request)
}

async function openPayment() {
  if (busy.value || !canPay.value) return
  openRequest += 1
  const request = openRequest
  mode.value = 'payment'
  error.value = ''
  historyError.value = ''
  clientError.value = ''
  if (!requestId) {
    requestId = crypto.randomUUID()
    amount.value = String(props.maintenance.debtAmount)
    methodCode.value = ''
    otherPayer.value = false
    payerClientId.value = ''
    comment.value = ''
  }
  payments.value = []
  showDialog()
  void loadClients(request)
  void loadHistory(request)
}

function close() {
  if (busy.value) return
  openRequest += 1
  dialog.value?.close()
}

function cancel(payment: WorkspaceMaintenancePayment) {
  if (busy.value) return
  openRequest += 1
  mode.value = 'cancel'
  cancelling.value = payment
  reason.value = ''
  error.value = ''
}

async function submit() {
  if (busy.value || !canManage.value) return
  if (mode.value === 'payment' && (!Number.isFinite(amountValue.value) || amountValue.value <= 0 || amountValue.value > props.maintenance.debtAmount)) {
    error.value = t('payments.invalidAmount')
    return
  }
  if (mode.value === 'payment' && !methodCode.value) {
    error.value = t('payments.chooseMethod')
    return
  }
  if (mode.value === 'payment' && otherPayer.value && !payerClientId.value) {
    error.value = t('payments.choosePayer')
    return
  }
  if (mode.value === 'cancel' && !reason.value.trim()) {
    error.value = t('payments.reasonRequired')
    return
  }

  busy.value = true
  error.value = ''
  try {
    if (mode.value === 'payment') {
      await saveMaintenanceCommerce(props.organizationId, props.maintenance.id, 'payments', {
        id: requestId,
        amount: amountValue.value,
        methodCode: methodCode.value,
        payerClientId: otherPayer.value ? payerClientId.value : null,
        comment: comment.value || null,
        expectedPaidAmount: props.maintenance.paidAmount
      })
      requestId = ''
    } else if (cancelling.value) {
      await saveMaintenanceCommerce(props.organizationId, props.maintenance.id, `payments/${cancelling.value.id}/cancel`, { reason: reason.value })
    }
    openRequest += 1
    dialog.value?.close()
    emit('changed')
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('payments.saveError')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="record-payments">
    <UiButton v-if="canPay" @click="openPayment">{{ t('payments.accept') }}</UiButton>
    <button type="button" class="record-payments__history" @click="history">{{ t('payments.history') }}</button>
    <dialog ref="dialog" class="payment-dialog" :class="{ 'payment-dialog--history': mode === 'history' }" aria-labelledby="payment-dialog-title" @cancel="busy && $event.preventDefault()">
      <header class="payment-dialog__header ui-form-header">
        <div><p class="payment-dialog__eyebrow">{{ mode === 'cancel' ? t('payments.correction') : t('payments.maintenanceFinance') }}</p>
          <h2 id="payment-dialog-title">{{ mode === 'payment' ? t('payments.paymentTitle') : mode === 'cancel' ? t('payments.cancelTitle') : t('payments.history') }}</h2></div>
        <button type="button" :disabled="busy" :aria-label="t('payments.close')" @click="close">×</button>
      </header>
      <form v-if="mode !== 'history'" class="payment-dialog__form" @submit.prevent="submit">
        <div v-if="mode === 'payment'" class="payment-layout">
          <section class="payment-layout__main" :aria-label="t('payments.details')">
            <div class="payment-amount">
              <div class="payment-amount__label ui-section-header"><span>{{ t('payments.amount') }}</span>
                <button type="button" :disabled="busy" @click="amount = String(maintenance.debtAmount)">{{ t('payments.fullBalance') }}</button></div>
              <UiMoneyInput id="payment-amount-input" v-model="amount" :label="t('payments.amount')" :min="0.01" :max="maintenance.debtAmount" :step="0.01" :disabled="busy" required />
              <p>{{ t('payments.afterPayment', { amount: money(remainingAfterPayment) }) }}</p>
            </div>

            <fieldset class="payment-methods" :disabled="busy">
              <legend>{{ t('payments.method') }}</legend>
              <div class="payment-methods__grid">
                <button v-for="option in methodOptions" :key="option.value" type="button" :class="{ selected: methodCode === option.value }" :aria-pressed="methodCode === option.value" @click="methodCode = option.value">{{ option.label }}</button>
              </div>
            </fieldset>

            <section class="payment-payer">
              <h3 class="ui-section-header">{{ t('payments.payer') }}</h3>
              <p>{{ buyerName }}<template v-if="maintenance.buyerPartyTypeCode === 'company' && maintenance.buyerInn"> · {{ t('payments.inn') }} {{ maintenance.buyerInn }}<template v-if="maintenance.buyerKpp"> · {{ t('payments.kpp') }} {{ maintenance.buyerKpp }}</template></template></p>
              <label class="payment-payer__other"><input v-model="otherPayer" type="checkbox" :disabled="busy"> {{ t('payments.anotherCustomerPays') }}</label>
              <UiSelect v-if="otherPayer" v-model="payerClientId" :label="t('payments.otherPayer')" :options="payerOptions" :disabled="busy" required />
              <p v-if="clientError" class="payment-error" role="alert">{{ clientError }}</p>
            </section>

            <UiTextarea v-model="comment" :label="t('payments.comment')" :maxlength="1000" :rows="2" :disabled="busy" />
          </section>

          <aside class="payment-layout__side" :aria-label="t('payments.summary')">
            <section class="payment-summary">
              <h3 class="ui-section-header">{{ t('payments.serviceSummary') }}</h3>
              <dl>
                <div><dt>{{ t('payments.workCost') }}</dt><dd>{{ money(maintenance.grossAmount) }}</dd></div>
                <div v-if="maintenance.discountAmount > 0"><dt>{{ t('payments.discount') }}</dt><dd>−{{ money(maintenance.discountAmount) }}</dd></div>
                <div><dt>{{ t('payments.paid') }}</dt><dd>{{ money(maintenance.paidAmount) }}</dd></div>
                <div class="payment-summary__debt"><dt>{{ t('payments.balance') }}</dt><dd>{{ money(maintenance.debtAmount) }}</dd></div>
              </dl>
            </section>
            <section class="payment-recent">
              <div class="payment-recent__heading ui-section-header"><h3>{{ t('payments.recent') }}</h3><button type="button" :disabled="busy" @click="history">{{ t('payments.allHistory') }}</button></div>
              <p v-if="loading" class="payment-muted">{{ t('payments.loading') }}</p>
              <p v-else-if="historyError" class="payment-muted" role="status">{{ t('payments.historyUnavailable') }}</p>
              <p v-else-if="!payments.length" class="payment-muted">{{ t('payments.noPayments') }}</p>
              <ul v-else class="payment-recent__list">
                <li v-for="payment in payments.slice(0, 3)" :key="payment.id" :class="{ cancelled: payment.cancelledAt }">
                  <div><strong>{{ money(payment.amount, payment.currencyCode) }}</strong><span v-if="payment.cancelledAt" class="payment-tag">{{ t('payments.cancelled') }}</span>
                    <small>{{ payment.isOpeningBalance ? t('payments.openingBalance') : date(payment.createdAt) }}</small>
                    <small>{{ methodName(payment.methodCode) }} · {{ payment.payerName || t('payments.payerUnspecified') }}</small></div>
                </li>
              </ul>
            </section>
          </aside>
        </div>
        <section v-else class="payment-cancel">
          <p>{{ t('payments.cancelNotice', { amount: money(cancelling?.amount ?? 0) }) }}</p>
          <UiTextarea v-model="reason" :label="t('payments.cancellationReason')" :maxlength="1000" :rows="3" :disabled="busy" required />
        </section>
        <p v-if="error" class="payment-error" role="alert">{{ error }}</p>
        <footer class="payment-dialog__footer">
          <UiButton variant="secondary" :disabled="busy" @click="close">{{ t('payments.cancel') }}</UiButton>
          <UiButton type="submit" class="payment-dialog__submit" :disabled="busy || (mode === 'payment' && amountValue > maintenance.debtAmount)">{{ submitLabel }}</UiButton>
        </footer>
      </form>
      <div v-else class="payment-history">
        <p v-if="loading">{{ t('payments.loadingPayments') }}</p>
        <p v-else-if="historyError" class="payment-error" role="alert">{{ historyError }} <button type="button" @click="history">{{ t('payments.retry') }}</button></p>
        <p v-else-if="!payments.length">{{ t('payments.noPaymentsSentence') }}</p>
        <article v-for="payment in loading || historyError ? [] : payments" :key="payment.id" :class="{ cancelled: payment.cancelledAt }">
          <div><strong>{{ money(payment.amount, payment.currencyCode) }}</strong><span v-if="payment.cancelledAt" class="payment-tag">{{ t('payments.cancelled') }}</span>
            <small>{{ payment.isOpeningBalance ? t('payments.openingHistory') : date(payment.createdAt) + ' · ' + payment.createdBy }}</small>
            <small>{{ methodName(payment.methodCode) }}</small>
            <small>{{ t('payments.payerLabel', { name: payment.payerName || t('payments.notSet') }) }}</small>
            <p v-if="payment.comment">{{ payment.comment }}</p>
            <p v-if="payment.cancelledAt">{{ date(payment.cancelledAt) }} · {{ payment.cancelledBy }}<br>{{ payment.cancellationReason }}</p>
          </div>
          <button v-if="canManage && !payment.cancelledAt" type="button" @click="cancel(payment)">{{ t('payments.cancelRecord') }}</button>
        </article>
      </div>
    </dialog>
  </div>
</template>

<style scoped>
.record-payments { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; padding: 14px 0 4px; }
.record-payments__history { border: 0; background: transparent; color: var(--crm-text-muted, #64748b); padding: 8px 0; cursor: pointer; font: inherit; font-size: 12px; text-decoration: underline; }
.payment-dialog { box-sizing: border-box; padding: 0; width: min(980px, calc(100vw - 24px)); max-height: min(760px, calc(100dvh - 32px)); overflow: hidden; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 12px; background: var(--crm-surface, #fff); color: var(--crm-text, #172033); }
.payment-dialog::backdrop { background: #0009; }
.payment-dialog[open] { display: flex; flex-direction: column; }
.payment-dialog__header { display: flex; flex-shrink: 0; gap: 16px; align-items: center; justify-content: space-between; padding: 16px 22px; border-bottom: 1px solid var(--crm-form-header-border, #d9e0e9); }
.payment-dialog__header h2 { margin: 0; font-size: 19px; }
.payment-dialog__eyebrow { margin: 0 0 3px; color: var(--crm-text-muted, #64748b); font-size: 11px; }
.payment-dialog__header > button { width: 34px; height: 34px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 8px; background: transparent; color: inherit; font-size: 23px; line-height: 1; cursor: pointer; }
.payment-dialog__form { display: flex; min-height: 0; flex-direction: column; overflow: hidden; }
.payment-layout { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(270px, .8fr); gap: 24px; min-height: 0; padding: 20px 22px; overflow: auto; }
.payment-layout__main { display: grid; align-content: start; gap: 20px; min-width: 0; }
.payment-amount { padding: 14px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 10px; background: var(--crm-surface, #fff); }
.payment-amount__label, .payment-recent__heading { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
.payment-amount__label { margin-bottom: 8px; font-size: 13px; font-weight: 650; }
.payment-amount__label button, .payment-recent__heading button { border: 0; padding: 4px 0; background: transparent; color: var(--crm-accent, #d9561e); cursor: pointer; font: inherit; font-size: 12px; }
.payment-amount > :deep(.ui-money-input) { display: grid; grid-template-columns: 1fr; gap: 0; }
.payment-amount > :deep(.ui-money-input .ui-field-label) { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.payment-amount p { margin: 8px 0 0; color: var(--crm-text-muted, #64748b); font-size: 12px; }
.payment-amount p strong { color: var(--crm-text, #172033); }
.payment-methods { min-width: 0; margin: 0; padding: 0; border: 0; }
.payment-methods legend { margin: 0 0 10px; padding: 0; font-size: 13px; font-weight: 650; }
.payment-payer h3, .payment-summary h3 { margin: 0 0 10px; padding: 10px 12px; border: 1px solid var(--crm-section-header-border, #dde3eb); border-radius: 6px; background: var(--crm-section-header-bg, #f2f4f7); font-size: 13px; font-weight: 650; }
.payment-methods__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; }
.payment-methods__grid button { min-height: 40px; padding: 8px 6px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 8px; background: var(--crm-surface, #fff); color: var(--crm-text, #172033); cursor: pointer; font: inherit; font-size: 12px; }
.payment-methods__grid button.selected { border-color: var(--crm-accent, #d9561e); background: color-mix(in srgb, var(--crm-accent, #d9561e) 10%, var(--crm-surface, #fff)); color: var(--crm-accent, #d9561e); }
.payment-payer { display: grid; gap: 9px; }
.payment-payer h3 { margin-bottom: 0; }
.payment-payer > p { margin: 0; font-size: 13px; }
.payment-payer__other { display: flex; justify-self: start; width: fit-content; max-width: 100%; gap: 8px; align-items: center; font-size: 12px; cursor: pointer; }
.payment-payer__other input { accent-color: var(--crm-accent, #d9561e); }
.payment-layout__side { display: grid; align-content: start; gap: 16px; min-width: 0; }
.payment-summary, .payment-recent { padding: 14px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 10px; }
.payment-summary dl { display: grid; gap: 9px; margin: 0; }
.payment-summary dl > div { display: flex; justify-content: space-between; gap: 12px; font-size: 12px; }
.payment-summary dt { color: var(--crm-text-muted, #64748b); }
.payment-summary dd { margin: 0; font-weight: 600; text-align: right; }
.payment-summary__debt { padding-top: 9px; border-top: 1px solid var(--crm-line, #d9e0e9); font-size: 13px !important; }
.payment-summary__debt dd { color: var(--crm-accent, #d9561e); }
.payment-recent__heading h3 { margin-bottom: 0; }
.payment-recent__heading button:disabled { cursor: default; opacity: .55; }
.payment-recent__list { display: grid; gap: 10px; padding: 0; margin: 12px 0 0; list-style: none; }
.payment-recent__list li { padding-top: 9px; border-top: 1px solid var(--crm-line, #d9e0e9); }
.payment-recent__list li > div > strong, .payment-recent__list li > div > span { display: inline-block; margin-right: 7px; }
.payment-recent__list small { display: block; margin-top: 4px; color: var(--crm-text-muted, #64748b); font-size: 11px; }
.payment-tag { color: var(--crm-danger-text, #b42318); font-size: 11px; }
.cancelled strong { color: var(--crm-text-muted, #64748b); text-decoration: line-through; }
.payment-muted { margin: 12px 0 0; color: var(--crm-text-muted, #64748b); font-size: 12px; }
.payment-dialog__footer { position: sticky; bottom: 0; display: flex; flex: 0 0 auto; justify-content: flex-end; gap: 10px; padding: 12px 22px; border-top: 1px solid var(--crm-line, #d9e0e9); background: var(--crm-surface, #fff); }
.record-payments .payment-dialog .payment-dialog__footer :deep(.ui-button) { width: auto; min-width: 0; flex: 0 0 auto; }
.payment-dialog__footer :deep(.payment-dialog__submit) { border-color: #e87532; background: #e87532; color: #fff; }
.payment-dialog__footer :deep(.payment-dialog__submit:hover:not(:disabled)) { border-color: #d86627; background: #d86627; }
.payment-dialog__footer :deep(.payment-dialog__submit:disabled) { border-color: #e87532; background: #e87532; opacity: .65; }
.payment-cancel { display: grid; flex: 1 1 auto; min-height: 0; gap: 16px; padding: 20px 22px; overflow-y: auto; }
.payment-cancel p, .payment-history p { margin: 0; font-size: 13px; line-height: 1.6; }
.payment-error { margin: 0 22px 10px; color: var(--crm-danger-text, #b42318); font-size: 13px; }
.payment-history { max-height: calc(min(760px, 100dvh - 32px) - 68px); padding: 0 22px 18px; overflow: auto; }
.payment-history > p { margin-top: 16px; }
.payment-history article { display: flex; align-items: start; justify-content: space-between; gap: 12px; padding: 14px 0; border-bottom: 1px solid var(--crm-line, #d9e0e9); }
.payment-history small { display: block; color: var(--crm-text-muted, #64748b); margin: 5px 0; }
.payment-history article > button { align-self: center; padding: 7px 10px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 7px; background: transparent; color: inherit; cursor: pointer; }
.payment-history .payment-tag { margin-left: 8px; }
.payment-history article p { margin-top: 8px; }
@media (max-width: 700px) {
  .payment-dialog { max-height: calc(100dvh - 16px); border-radius: 10px; }
  .payment-dialog__header { padding: 13px 15px; }
  .payment-layout { grid-template-columns: minmax(0, 1fr); gap: 16px; padding: 14px 15px; }
  .payment-layout__side { grid-template-columns: minmax(0, 1fr); }
  .payment-methods__grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .payment-dialog__footer { padding: 10px 15px; }
  .payment-error { margin-right: 15px; margin-left: 15px; }
  .payment-history { max-height: calc(100dvh - 70px); padding: 0 15px 15px; }
}
@media (max-width: 380px) {
  .payment-dialog__footer { display: grid; grid-template-columns: 1fr 1fr; }
  .record-payments .payment-dialog .payment-dialog__footer :deep(.ui-button) { width: 100%; }
}
</style>
