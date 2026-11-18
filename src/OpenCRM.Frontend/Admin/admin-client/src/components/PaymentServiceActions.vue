<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminBillingMessages'
import { ApiError } from '@/api/ApiError'
import { getAdminPaymentServiceActions, submitAdminPaymentServiceAction } from '@/api/adminBillingApi'
import type { AdminPaymentServiceActions, AdminPaymentServiceActionRequest, PaymentServiceActionKind } from '@/models/AdminPaymentServiceActions'
import { parseRefundAmount, readPaymentActionDraft } from '@/paymentServiceActions'
import { formatPaymentDate } from '@/paymentDetails'

const props = defineProps<{ paymentId: number; canManage: boolean; actorId: string }>()
const { t } = useLocalization()
const emit = defineEmits<{ updated: [] }>()
const state = ref<AdminPaymentServiceActions | null>(null)
const loading = ref(false)
const submitting = ref(false)
const loadError = ref('')
const actionError = ref('')
const message = ref('')
const dialog = ref<HTMLDialogElement | null>(null)
const selectedAction = ref<PaymentServiceActionKind | null>(null)
const reason = ref('')
const refundMode = ref<'full' | 'partial'>('full')
const amount = ref('')
const acknowledged = ref(false)
const pendingDraft = ref<AdminPaymentServiceActionRequest | null>(null)
const draftBlocked = ref(false)
let requestVersion = 0
let pollTimer: ReturnType<typeof setTimeout> | undefined
let pollsLeft = 0
let active = true
const draftKey = computed(() => 'admin-payment-action:' + props.actorId + ':' + props.paymentId)
const refundAmount = computed(() => refundMode.value === 'full' ? state.value?.refundableAmount ?? null : parseRefundAmount(amount.value, state.value?.refundableAmount ?? 0))
const actionItems = computed(() => state.value ? [
  { kind: 'check' as const, enabled: state.value.canCheck, hint: state.value.checkHint },
  { kind: 'refund' as const, enabled: state.value.canRefund, hint: state.value.refundHint },
  { kind: 'retry-access' as const, enabled: state.value.canRetryAccess, hint: state.value.retryAccessHint },
  { kind: 'resolve-review' as const, enabled: state.value.canResolveReview, hint: state.value.resolveReviewHint },
] : [])
function actionLabel(kind: PaymentServiceActionKind): string {
  const keys: Record<PaymentServiceActionKind, string> = {
    check: 'adminBilling.actions.labels.check',
    refund: 'adminBilling.actions.labels.refundAction',
    'retry-access': 'adminBilling.actions.labels.retryAccessLabel',
    'resolve-review': 'adminBilling.actions.labels.resolveReviewAction'
  }

  return t(keys[kind])
}
function actionStatus(status: string): string {
  const keys: Record<string, string> = {
    queued: 'statusQueued', requested: 'statusRequested', processing: 'statusProcessing', pending: 'statusPending',
    succeeded: 'statusSucceeded', completed: 'statusSucceeded', failed: 'statusFailed', rejected: 'statusRejected',
    needs_attention: 'statusReview', unknown: 'statusUnknown'
  }

  return t(`adminBilling.actions.labels.${keys[status.toLowerCase()] ?? 'statusUnknown'}`)
}
const selectedHint = computed(() => actionItems.value.find(item => item.kind === selectedAction.value)?.hint || '')
const canSubmit = computed(() => props.canManage && !submitting.value && !pendingDraft.value && !draftBlocked.value &&
  reason.value.trim().length > 0 && acknowledged.value &&
  actionItems.value.some(item => item.kind === selectedAction.value && item.enabled) &&
  (selectedAction.value !== 'refund' || refundAmount.value !== null && refundAmount.value > 0))

function money(value: number): string {
  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: 'RUB', currencyDisplay: 'code' }).format(value)
}

function restoreDraft(): void {
  try {
    pendingDraft.value = readPaymentActionDraft(sessionStorage.getItem(draftKey.value))
  } catch {
    draftBlocked.value = true
    actionError.value = t('adminBilling.actions.restorePendingError')
  }
}

function clearDraft(): void {
  sessionStorage.removeItem(draftKey.value)
  pendingDraft.value = null
}

async function loadState(): Promise<void> {
  const version = ++requestVersion
  const paymentId = props.paymentId
  loading.value = true
  loadError.value = ''
  clearTimeout(pollTimer)
  try {
    const result = await getAdminPaymentServiceActions(paymentId)
    if (version !== requestVersion) return
    const completedTransition = state.value != null && result.operations.some(item => item.completedAt &&
      state.value?.operations.some(previous => previous.id === item.id && !previous.completedAt))
    if (state.value?.stateToken !== result.stateToken) acknowledged.value = false
    state.value = result
    if (completedTransition) emit('updated')
    const knownOperation = pendingDraft.value && result.operations.find(item => item.id === pendingDraft.value?.operationId)
    if (knownOperation && knownOperation.action === pendingDraft.value?.action &&
      knownOperation.reason === pendingDraft.value.reason &&
      (pendingDraft.value.action !== 'refund' || knownOperation.amount === pendingDraft.value.amount)) {
      clearDraft()
      actionError.value = ''
      message.value = knownOperation.message
      closeDialog()
    }
    if (pollsLeft > 0 && result.operations.some(item => ['queued', 'requested', 'processing', 'pending', 'unknown'].includes(item.status))) {
      pollsLeft--
      pollTimer = setTimeout(() => { void loadState() }, 5000)
    }
  } catch (error) {
    if (version === requestVersion) loadError.value = error instanceof ApiError ? error.message : t('adminBilling.actions.loadError')
  } finally {
    if (version === requestVersion) loading.value = false
  }
}

async function openAction(kind: PaymentServiceActionKind): Promise<void> {
  if (!props.canManage || submitting.value || pendingDraft.value || draftBlocked.value) return
  selectedAction.value = kind
  reason.value = ''
  refundMode.value = 'full'
  amount.value = ''
  acknowledged.value = false
  actionError.value = ''
  await nextTick()
  dialog.value?.showModal()
}

function closeDialog(): void {
  if (submitting.value) return
  dialog.value?.close()
  selectedAction.value = null
}

async function submit(): Promise<void> {
  if (!canSubmit.value || !selectedAction.value || !state.value) return
  const draft: AdminPaymentServiceActionRequest = {
    operationId: crypto.randomUUID(), action: selectedAction.value,
    reason: reason.value.trim(), stateToken: state.value.stateToken,
    ...(selectedAction.value === 'refund' ? { amount: refundAmount.value! } : {})
  }
  try {
    sessionStorage.setItem(draftKey.value, JSON.stringify(draft))
    pendingDraft.value = draft
  } catch {
    actionError.value = t('adminBilling.actions.saveDraftError')
    return
  }
  await sendDraft()
}

async function sendDraft(): Promise<void> {
  if (!props.canManage || submitting.value || !pendingDraft.value) return
  const draft = pendingDraft.value
  const paymentId = props.paymentId
  const storageKey = draftKey.value
  submitting.value = true
  actionError.value = ''
  message.value = ''
  try {
    const result = await submitAdminPaymentServiceAction(paymentId, draft)
    if (!active) { sessionStorage.removeItem(storageKey); return }
    if (result.id !== draft.operationId || result.action !== draft.action ||
      (draft.action === 'refund' && result.amount !== draft.amount)) throw new Error('operation_identity_mismatch')
    clearDraft()
    message.value = result.message
    submitting.value = false
    closeDialog()
    pollsLeft = 12
    await loadState()
    emit('updated')
  } catch (error) {
    if (!active) return
    const definitiveRejection = error instanceof ApiError && [400, 404, 409, 422].includes(error.status) && error.code !== 'operation_id_conflict'
    if (definitiveRejection) {
      clearDraft()
      acknowledged.value = false
      actionError.value = error.message
      await loadState()
    } else {
      actionError.value = t('adminBilling.actions.ambiguousResponse')
    }
  } finally {
    submitting.value = false
  }
}

watch(() => [props.paymentId, props.actorId], () => {
  requestVersion++
  clearTimeout(pollTimer)
  state.value = null
  pendingDraft.value = null
  draftBlocked.value = false
  actionError.value = ''
  message.value = ''
  pollsLeft = 12
  restoreDraft()
  void loadState()
}, { immediate: true })

onBeforeUnmount(() => { active = false; requestVersion++; clearTimeout(pollTimer) })
</script>

<template>
  <section class="payment-actions" aria-labelledby="payment-actions-title">
    <div class="payment-actions__heading">
      <h3 id="payment-actions-title">{{ t('adminBilling.actions.title') }}</h3>
      <button type="button" class="payment-actions__refresh" :disabled="loading || submitting" :title="t('adminBilling.actions.refreshTitle')" @click="loadState">{{ loading ? t('adminBilling.actions.refreshing') : t('adminBilling.actions.refresh') }}</button>
    </div>
    <p v-if="loadError" class="payment-actions__error" role="alert">{{ loadError }}</p>
    <p v-if="state?.operations[0] || message" class="payment-actions__message" role="status">{{ state?.operations[0]?.message || message }}</p>
    <div v-if="pendingDraft && !submitting" class="payment-actions__pending" role="status">
      <strong>{{ t('adminBilling.actions.pendingTitle') }}</strong>
          <p>{{ t('adminBilling.actions.pendingOperation', { action: actionLabel(pendingDraft.action), amount: pendingDraft.amount == null ? '' : ` · ${money(pendingDraft.amount)}` }) }}</p>
      <button type="button" :disabled="submitting || !canManage" @click="sendDraft">{{ submitting ? t('adminBilling.actions.checking') : t('adminBilling.actions.checkResult') }}</button>
    </div>
    <p v-if="actionError && !selectedAction" class="payment-actions__error" role="alert">{{ actionError }}</p>
    <p v-if="!canManage" class="payment-actions__hint">{{ t('adminBilling.actions.viewOnly') }}</p>
    <template v-if="state">
      <p v-if="state.reviewResolved" class="payment-actions__review">{{ t('adminBilling.actions.reviewResolved') }}</p>
      <div class="payment-actions__grid">
        <article v-for="item in actionItems" :key="item.kind">
          <button type="button" :class="{ 'payment-actions__refund': item.kind === 'refund' }" :disabled="!canManage || !item.enabled || loading || submitting || !!pendingDraft || draftBlocked" :aria-describedby="'payment-action-hint-' + item.kind" :title="item.hint" @click="openAction(item.kind)">{{ actionLabel(item.kind) }}</button>
          <p :id="'payment-action-hint-' + item.kind">{{ item.hint }}</p>
        </article>
      </div>
      <details v-if="state.operations.length" class="payment-actions__history">
        <summary>{{ t('adminBilling.actions.recentAdminActions', { count: state.operations.length }) }}</summary>
        <ol>
          <li v-for="operation in state.operations" :key="operation.id">
            <div class="payment-actions__operation-heading"><strong>{{ actionLabel(operation.action) || operation.action }}</strong><span>{{ actionStatus(operation.status) }}</span></div>
            <p>{{ operation.message }}<span v-if="operation.amount != null"> · {{ money(operation.amount) }}</span></p>
            <p class="payment-actions__hint">{{ formatPaymentDate(operation.createdAt) }} · {{ operation.actor }}</p>
            <p>{{ t('adminBilling.actions.reason', { reason: operation.reason }) }}</p>
            <details><summary>{{ t('adminBilling.actions.operationId') }}</summary><code>{{ operation.id }}</code><p v-if="operation.code">{{ t('adminBilling.actions.code', { code: operation.code }) }}</p></details>
          </li>
        </ol>
      </details>
    </template>
    <dialog ref="dialog" class="payment-actions__dialog" aria-labelledby="payment-action-dialog-title" @cancel="submitting ? $event.preventDefault() : closeDialog()">
      <form v-if="selectedAction" @submit.prevent="submit">
        <header><h3 id="payment-action-dialog-title">{{ actionLabel(selectedAction) }}</h3><button type="button" :aria-label="t('adminBilling.actions.close')" :disabled="submitting" @click="closeDialog">×</button></header>
        <p class="payment-actions__hint">{{ selectedHint }}</p>
        <fieldset v-if="selectedAction === 'refund'" :disabled="submitting || !!pendingDraft">
          <legend>{{ t('adminBilling.actions.refundAmount') }}</legend>
          <label class="payment-actions__choice"><input v-model="refundMode" type="radio" value="full"> {{ t('adminBilling.actions.fullRefund', { amount: money(state?.refundableAmount ?? 0) }) }}</label>
          <label class="payment-actions__choice"><input v-model="refundMode" type="radio" value="partial">{{ t('adminBilling.actions.partialRefund') }}</label>
          <label v-if="refundMode === 'partial'" class="payment-actions__field"><span>{{ t('adminBilling.actions.returnAmount') }}</span><input v-model="amount" inputmode="decimal" :placeholder="t('adminBilling.actions.amountExample')" required></label>
          <p v-if="refundMode === 'partial' && amount && refundAmount === null" class="payment-actions__error">{{ t('adminBilling.actions.invalidAmount') }}</p>
          <p class="payment-actions__hint">{{ t('adminBilling.actions.refundWarning') }}</p>
        </fieldset>
        <p v-if="selectedAction === 'retry-access'" class="payment-actions__hint">{{ t('adminBilling.actions.retryAccessHint') }}</p>
        <p v-if="selectedAction === 'resolve-review'" class="payment-actions__hint">{{ t('adminBilling.actions.reviewHint') }}</p>
        <label class="payment-actions__field"><span>{{ t(selectedAction === 'resolve-review' ? 'adminBilling.actions.decisionAndComment' : 'adminBilling.actions.actionReason') }}</span><textarea v-model="reason" rows="3" maxlength="500" required :disabled="submitting || !!pendingDraft" :placeholder="t('adminBilling.actions.reasonPlaceholder')"></textarea></label>
        <label class="payment-actions__choice"><input v-model="acknowledged" type="checkbox" :disabled="submitting || !!pendingDraft"> {{ selectedAction === 'refund' ? t('adminBilling.actions.confirmRefund', { amount: refundAmount == null ? t('adminBilling.actions.amountToBeSpecified') : money(refundAmount) }) : t('adminBilling.actions.confirmAction') }}</label>
        <p v-if="actionError" class="payment-actions__error" role="alert">{{ actionError }}</p>
        <div v-if="pendingDraft && !submitting" class="payment-actions__pending"><button type="button" :disabled="submitting" @click="sendDraft">{{ submitting ? t('adminBilling.actions.checking') : t('adminBilling.actions.checkResult') }}</button><p class="payment-actions__hint">{{ t('adminBilling.actions.repeatSameRequest') }}</p></div>
        <footer><button type="button" :disabled="submitting" :title="t(pendingDraft ? 'adminBilling.actions.closePendingTitle' : 'adminBilling.actions.closeWithoutSendTitle')" @click="closeDialog">{{ pendingDraft ? t('adminBilling.actions.closeForm') : t('adminBilling.actions.cancel') }}</button><button type="submit" class="payment-actions__confirm" :disabled="!canSubmit">{{ submitting ? t('adminBilling.actions.sending') : selectedAction === 'refund' ? t('adminBilling.actions.confirmRefundButton') : t('adminBilling.actions.confirmActionButton') }}</button></footer>
      </form>
    </dialog>
  </section>
</template>

<style scoped>
.payment-actions { border-top: 1px solid var(--payment-border); padding-top: 16px; }
.payment-actions__heading, .payment-actions__operation-heading { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.payment-actions h3 { margin: 0; font-size: 14px; }
.payment-actions p { margin: 7px 0 0; line-height: 1.5; font-size: 12px; overflow-wrap: anywhere; }
.payment-actions button { min-height: 36px; padding: 8px 12px; border: 1px solid var(--payment-border); border-radius: 8px; color: var(--payment-text); background: var(--payment-surface); font: inherit; font-size: 12px; font-weight: 700; cursor: pointer; }
.payment-actions button:hover:not(:disabled) { background: var(--payment-soft); }
.payment-actions button:disabled { opacity: .55; cursor: default; }
.payment-actions :is(button, input, textarea, summary):focus-visible { outline: 2px solid #cf6c31; outline-offset: 3px; }
.payment-actions .payment-actions__refresh { font-weight: 500; }
.payment-actions__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; margin-top: 12px; }
.payment-actions__grid article { padding: 12px; border: 1px solid var(--payment-border); border-radius: 10px; }
.payment-actions__grid p, .payment-actions__hint { color: var(--payment-muted); }
.payment-actions__grid .payment-actions__refund { color: var(--payment-red); }
.payment-actions__error { color: var(--payment-red); }
.payment-actions__message, .payment-actions__review { padding: 10px 12px; background: var(--payment-soft); border-radius: 8px; }
.payment-actions__pending { padding: 12px; margin-top: 12px; border: 1px solid var(--payment-orange); border-radius: 8px; }
.payment-actions__pending button { margin-top: 8px; }
.payment-actions__history { margin-top: 16px; }
.payment-actions summary { cursor: pointer; font-size: 12px; }
.payment-actions__history ol { padding: 0; margin: 10px 0 0; list-style: none; }
.payment-actions__history li { border-top: 1px solid var(--payment-border); padding: 12px 0; }
.payment-actions__operation-heading { align-items: flex-start; font-size: 12px; }
.payment-actions__operation-heading > span { color: var(--payment-muted); }
.payment-actions__history li > details { margin-top: 8px; color: var(--payment-muted); }
.payment-actions code { display: block; margin-top: 6px; overflow-wrap: anywhere; font-size: 11px; }
.payment-actions__dialog { width: min(520px, calc(100% - 32px)); max-height: calc(100dvh - 32px); padding: 20px; border: 1px solid var(--payment-border); border-radius: 14px; color: var(--payment-text); background: var(--payment-surface); box-sizing: border-box; }
.payment-actions__dialog::backdrop { background: rgb(0 0 0 / 45%); }
.payment-actions__dialog header, .payment-actions__dialog footer { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
.payment-actions__dialog header h3 { font-size: 18px; }
.payment-actions__dialog header button { font-size: 20px; }
.payment-actions__dialog footer { justify-content: flex-end; margin-top: 18px; }
.payment-actions__dialog fieldset { border: 1px solid var(--payment-border); border-radius: 8px; margin: 16px 0; padding: 12px; }
.payment-actions__dialog legend { font-size: 13px; font-weight: 700; }
.payment-actions__field { display: grid; gap: 6px; margin-top: 14px; font-size: 13px; font-weight: 600; }
.payment-actions__field :is(input, textarea) { width: 100%; padding: 10px; border: 1px solid var(--payment-border); border-radius: 8px; box-sizing: border-box; color: var(--payment-text); background: var(--payment-soft); font: inherit; font-weight: 400; }
.payment-actions__choice { display: flex; align-items: flex-start; gap: 8px; margin-top: 12px; font-size: 12px; line-height: 1.5; }
.payment-actions__choice input { flex: none; margin-top: 3px; accent-color: #cf6c31; }
.payment-actions button.payment-actions__confirm { border-color: #cf6c31; background: #cf6c31; color: #fff; }
@media (max-width: 760px) { .payment-actions__grid { grid-template-columns: minmax(0, 1fr); } }
@media (max-width: 420px) { .payment-actions__heading { align-items: flex-start; flex-direction: column; } .payment-actions__dialog { padding: 14px; } }
</style>
