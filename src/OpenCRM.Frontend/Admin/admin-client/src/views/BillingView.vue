<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import PaymentServiceDetails from '@/components/PaymentServiceDetails.vue'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '@/api/ApiError'
import { usesFreePro, subscriptionFallback } from '@/utils/subscriptionPolicy'
import {
  extendAdminSubscription,
  getAdminAvailablePlans,
  getAdminBilling,
  getAdminBillingPayment,
  getAdminBillingPurchases,
  getAdminPaymentStatusAudits,
  getAdminSubscriptionAudits,
  deleteAdminNpdReceiptFile,
  grantAdminOrganizationDemo,
  grantAdminPlan,
  grantAdminUserDemo,
  markAdminNpdReceipt,
  markAdminPaymentStatus,
  revokeAdminOrganizationDemo,
  revokeAdminUserDemo,
  stopAdminSubscription,
  saveAdminNpdReceiptDetails,
  sendAdminNpdReceipt,
  syncAdminPayment,
  uploadAdminNpdReceiptFile
} from '@/api/adminBillingApi'
import type { AdminBilling } from '@/models/AdminBilling'
import type { AdminBillingPayment } from '@/models/AdminBillingPayment'
import type { AdminBillingPurchase } from '@/models/AdminBillingPurchase'
import type { AdminBillingOrganizationSubscription } from '@/models/AdminBillingOrganizationSubscription'
import type { AdminBillingUserSubscription } from '@/models/AdminBillingUserSubscription'
import type { AdminPaymentStatusAudit } from '@/models/AdminPaymentStatusAudit'
import type { AdminSubscriptionAudit } from '@/models/AdminSubscriptionAudit'
import type { AdminAvailablePlan } from '@/models/AdminAvailablePlan'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminBillingMessages'
import { getAdminRecordLabel, getAdminPaymentStatusLabel } from '@/utils/adminRecordLabels'

const currentUserStore = useAdminCurrentUserStore()
const { t, locale } = useLocalization()
const route = useRoute()
const router = useRouter()
const billing = ref<AdminBilling | null>(null)
const purchases = ref<AdminBillingPurchase[]>([])
const availablePlans = ref<AdminAvailablePlan[]>([])
const activeTab = ref<'payments' | 'purchases' | 'subscriptions'>(
  route.query.tab === 'purchases' || route.query.tab === 'payments' ? route.query.tab : 'subscriptions')
const subscriptionScope = ref<'users' | 'organizations'>('organizations')
const search = ref('')
const paymentStatus = ref('')
const purchaseBuyerType = ref('')
const purchaseReceiptStatus = ref('')
const purchaseQueueFilter = ref('attention')
const subscriptionStatus = ref('active')
const subscriptionPlan = ref('')
const subscriptionSource = ref('')
const selectedPayment = ref<AdminBillingPayment | null>(null)
const isLoadingPaymentDetails = ref(false)
const paymentDetailsError = ref<string | null>(null)
let paymentDetailsRequest = 0
const selectedPurchase = ref<AdminBillingPurchase | null>(null)
const receiptUrl = ref('')
const receiptComment = ref('')
const selectedSubscription = ref<AdminBillingUserSubscription | AdminBillingOrganizationSubscription | null>(null)
const paymentStatusAudits = ref<AdminPaymentStatusAudit[]>([])
const subscriptionAudits = ref<AdminSubscriptionAudit[]>([])
const subscriptionAction = ref<'extend' | 'stop' | 'grantDemo' | 'revokeDemo' | 'grantPlan'>('extend')
const subscriptionReason = ref('')
const grantPlanId = ref(0)
const selectedStatus = ref('completed')
const statusChangeReason = ref('')
const receiptChangeReason = ref('')
const extensionDays = ref(30)
const isLoading = ref(false)
const isSaving = ref(false)
const isSyncing = ref(false)
const isUploadingReceipt = ref(false)
const sendingReceiptPaymentId = ref<number | null>(null)
const errorMessage = ref<string | null>(null)
const successMessage = ref<string | null>(null)

const canManage = computed(() => currentUserStore.isSystem ||
  currentUserStore.currentUser?.permissions.includes('Admin.Billing.Manage') === true)
const canCorrectPaymentStatus = computed(() => currentUserStore.isSystem)
const isDetail = computed(() => route.params.recordType !== undefined)

const filteredPayments = computed(() => {
  const query = search.value.trim().toLowerCase()

  return (billing.value?.payments ?? []).filter(payment => {
    const matchesStatus = !paymentStatus.value || payment.statusGroup === paymentStatus.value
    const matchesSearch = !query || [
      payment.user,
      payment.userEmail,
      payment.userPhone,
      payment.planName,
      payment.transactionId,
      payment.externalPaymentId,
      payment.promoCodeSnapshot
    ].some(value => value?.toLowerCase().includes(query))

    return matchesStatus && matchesSearch
  })
})

const filteredPurchases = computed(() => {
  const query = search.value.trim().toLowerCase()

  return purchases.value.filter(purchase => {
    const matchesSearch = !query || [
      purchase.user,
      purchase.userEmail,
      purchase.userPhone,
      purchase.buyerName,
      purchase.buyerInn,
      purchase.planName
    ].some(value => value?.toLowerCase().includes(query))
    const matchesBuyerType = !purchaseBuyerType.value ||
      (purchaseBuyerType.value === 'unknown' && purchase.buyerType === 'unknown') ||
      purchase.buyerType === purchaseBuyerType.value
    const matchesReceiptStatus = !purchaseReceiptStatus.value ||
      (purchaseReceiptStatus.value === 'issued' && purchase.npdReceiptIssued) ||
      (purchaseReceiptStatus.value === 'pending' && !purchase.npdReceiptIssued)
    const matchesQueue = purchaseQueueFilter.value === 'all' ||
      (purchaseQueueFilter.value === 'attention' && requiresReceiptAttention(purchase)) ||
      (purchaseQueueFilter.value === 'overdue' && isReceiptOverdue(purchase)) ||
      (purchaseQueueFilter.value === 'email_failed' && getDeliveryStatus(purchase) === 'failed') ||
      (purchaseQueueFilter.value === 'email_queued' && getDeliveryStatus(purchase) === 'queued') ||
      (purchaseQueueFilter.value === 'email_sent' && ['sent', 'delivered'].includes(getDeliveryStatus(purchase))) ||
      (purchaseQueueFilter.value === 'refunded' && hasRefund(purchase))

    return matchesSearch && matchesBuyerType && matchesReceiptStatus && matchesQueue
  }).sort((first, second) => {
    const refundedOrder = Number(isRefundedPurchase(first)) - Number(isRefundedPurchase(second))
    if (refundedOrder !== 0) {
      return refundedOrder
    }

    const issuedOrder = Number(first.npdReceiptIssued) - Number(second.npdReceiptIssued)
    if (issuedOrder !== 0) {
      return issuedOrder
    }

    const overdueOrder = Number(isReceiptOverdue(second)) - Number(isReceiptOverdue(first))
    if (overdueOrder !== 0) {
      return overdueOrder
    }

    const failedOrder = Number(getDeliveryStatus(second) === 'failed') - Number(getDeliveryStatus(first) === 'failed')
    if (failedOrder !== 0) {
      return failedOrder
    }

    return new Date(first.paidAt).getTime() - new Date(second.paidAt).getTime()
  })
})

const purchaseKpis = computed(() => ({
  paidAmount: purchases.value.reduce((total, purchase) => total + purchase.amount, 0),
  refundedAmount: purchases.value.reduce((total, purchase) => total + getRefundedAmount(purchase), 0),
  netAmount: purchases.value.reduce((total, purchase) => total + purchase.amount - getRefundedAmount(purchase), 0),
  attentionCount: purchases.value.filter(requiresReceiptAttention).length,
  overdueCount: purchases.value.filter(isReceiptOverdue).length
}))

function getRefundedAmount(purchase: AdminBillingPurchase): number {
  return purchase.paymentStatus === 'refunded' && purchase.refundedAmount === 0
    ? purchase.amount
    : purchase.refundedAmount
}

function isRefundedPurchase(purchase: AdminBillingPurchase): boolean {
  return purchase.refundedAmount >= purchase.amount || purchase.paymentStatus.toLowerCase().includes('refund')
}

function hasRefund(purchase: AdminBillingPurchase): boolean {
  return purchase.refundedAmount > 0 || isRefundedPurchase(purchase)
}

function getDeliveryStatus(purchase: AdminBillingPurchase): string {
  return purchase.npdReceiptDeliveryStatus ?? 'not_sent'
}

function isReceiptOverdue(purchase: AdminBillingPurchase): boolean {
  return !isRefundedPurchase(purchase) && !purchase.npdReceiptIssued &&
    Date.now() - new Date(purchase.paidAt).getTime() > 24 * 60 * 60 * 1000
}

function requiresReceiptAttention(purchase: AdminBillingPurchase): boolean {
  return !isRefundedPurchase(purchase) &&
    (!purchase.npdReceiptIssued || getDeliveryStatus(purchase) === 'failed' ||
      ['failed', 'processing'].includes(purchase.paymentStatus))
}

function getReceiptAge(purchase: AdminBillingPurchase): string {
  const ageDays = Math.floor((Date.now() - new Date(purchase.paidAt).getTime()) / (24 * 60 * 60 * 1000))
  return ageDays < 1 ? t('adminBilling.view.todayAge') : t('adminBilling.view.daysShort', { days: ageDays })
}

function getDeliveryStatusName(status: string): string {
  const keys: Record<string, string> = {
    not_sent: 'sentNot', queued: 'sentQueued', sent: 'sentAccepted', delivered: 'sentDelivered', simulated: 'sentSimulated', failed: 'sentFailed'
  }

  return keys[status] ? t(`adminBilling.view.${keys[status]}`) : status
}

const subscriptions = computed(() => subscriptionScope.value === 'users'
  ? billing.value?.subscriptions.users ?? []
  : billing.value?.subscriptions.organizations ?? [])

const subscriptionPlans = computed(() => [...new Set(subscriptions.value.map(subscription => subscription.planName))]
  .sort((first, second) => first.localeCompare(second, locale.value)))
const grantablePlans = computed(() => availablePlans.value.filter(plan =>
  plan.planTypeCode === (subscriptionScope.value === 'users' ? 'personal' : 'business')))

function hasActiveSubscriptionForClient(subscription: AdminBillingUserSubscription | AdminBillingOrganizationSubscription): boolean {
  return subscriptions.value.some(item => item.isActive && (
    'organizationId' in subscription && 'organizationId' in item
      ? item.organizationId === subscription.organizationId
      : 'userId' in subscription && 'userId' in item && item.userId === subscription.userId))
}

const filteredSubscriptions = computed(() => {
  const query = search.value.trim().toLowerCase()
  return subscriptions.value.filter(subscription => {
    const organization = 'organizationName' in subscription ? subscription.organizationName : null
    const owner = 'owner' in subscription ? subscription.owner : null
    const user = 'user' in subscription ? subscription.user : null

    const matchesSearch = !query || [organization, owner, user, subscription.planName,
      'userEmail' in subscription ? subscription.userEmail : null,
      'userPhone' in subscription ? subscription.userPhone : null]
      .some(value => value?.toLowerCase().includes(query))
    const matchesStatus = !subscriptionStatus.value ||
      (subscriptionStatus.value === 'active' && subscription.isActive) ||
      (subscriptionStatus.value === 'expiring' && subscription.isActive && subscription.isExpiringSoon) ||
      (subscriptionStatus.value === 'ended' && !subscription.isActive)

    return matchesSearch && matchesStatus &&
      (!subscriptionPlan.value || subscription.planName === subscriptionPlan.value) &&
      (!subscriptionSource.value || subscription.source === subscriptionSource.value)
  })
})

const previewEndDate = computed(() => {
  if (!selectedSubscription.value || !['extend', 'grantPlan'].includes(subscriptionAction.value) ||
    !Number.isInteger(extensionDays.value) || extensionDays.value < 1 || extensionDays.value > 366 ||
    (subscriptionAction.value === 'grantPlan' && !grantablePlans.value.some(plan => plan.id === grantPlanId.value))) {
    return null
  }

  const currentEnd = new Date(selectedSubscription.value.endDate)
  const start = subscriptionAction.value === 'extend' && currentEnd.getTime() > Date.now() ? currentEnd : new Date()

  return new Date(start.getTime() + extensionDays.value * 24 * 60 * 60 * 1000).toISOString()
})

onMounted(() => {
  void loadBilling()
  if (canManage.value) {
    void loadAvailablePlans()
  }
})

watch(() => route.query.tab, tab => {
  if (tab === 'purchases') {
    showPurchases()
  } else if (tab === 'payments') {
    showPayments()
  } else if (tab === 'subscriptions') {
    showSubscriptions()
  }
})

async function loadAvailablePlans(): Promise<void> {
  try {
    availablePlans.value = await getAdminAvailablePlans()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.plansLoadError'))
  }
}

async function loadBilling(): Promise<void> {
  isLoading.value = true
  errorMessage.value = null

  try {
    billing.value = await getAdminBilling()
    purchases.value = await getAdminBillingPurchases()
    await selectRecordFromRoute()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.loadError'))
  } finally {
    isLoading.value = false
  }
}

watch(() => [route.params.recordType, route.params.recordId], () => void selectRecordFromRoute())

async function selectRecordFromRoute(): Promise<void> {
  if (!billing.value || !isDetail.value) return

  const id = Number(route.params.recordId)
  const type = route.params.recordType
  if (!Number.isInteger(id)) {
    errorMessage.value = t('adminBilling.view.invalidRecordId')
    return
  }

  if (type === 'payments') {
    activeTab.value = 'payments'
    const payment = billing.value.payments.find(item => item.id === id)
      ?? await getAdminBillingPayment(id).catch(() => null)
    if (payment) await selectPayment(payment)
    else errorMessage.value = t('adminBilling.view.paymentNotFound')
  } else if (type === 'purchases') {
    activeTab.value = 'purchases'
    const purchase = purchases.value.find(item => item.paymentId === id)
    if (purchase) selectPurchase(purchase)
    else errorMessage.value = t('adminBilling.view.purchaseNotFound')
  } else if (type === 'user-subscriptions' || type === 'organization-subscriptions') {
    activeTab.value = 'subscriptions'
    subscriptionScope.value = type === 'user-subscriptions' ? 'users' : 'organizations'
    const subscription = billing.value.subscriptions[subscriptionScope.value].find(item => item.id === id)
    if (subscription) await selectSubscription(subscription)
    else errorMessage.value = t('adminBilling.view.subscriptionNotFound')
  } else {
    errorMessage.value = t('adminBilling.view.unknownRecordType')
  }
}

function openRecord(type: string, id: number): void {
  void router.push(`/finance/billing/${type}/${id}`)
}

function returnToList(): void {
  closeEditors()
  void router.push({ path: '/finance/billing', query: { tab: activeTab.value } })
}

function selectPurchase(purchase: AdminBillingPurchase): void {
  selectedPurchase.value = purchase
  receiptUrl.value = purchase.npdReceiptUrl ?? ''
  receiptComment.value = purchase.npdReceiptComment ?? ''
  selectedPayment.value = null
  selectedSubscription.value = null
  receiptChangeReason.value = ''
  clearMessages()
}

async function selectPayment(payment: AdminBillingPayment): Promise<void> {
  const request = ++paymentDetailsRequest
  selectedPayment.value = payment
  selectedSubscription.value = null
  selectedStatus.value = payment.paymentStatus ?? 'completed'
  statusChangeReason.value = ''
  paymentStatusAudits.value = []
  paymentDetailsError.value = null
  isLoadingPaymentDetails.value = true
  clearMessages()

  try {
    const [details, audits] = await Promise.all([
      getAdminBillingPayment(payment.id),
      payment.paymentProviderCode === 'payments-service' ? Promise.resolve([]) : getAdminPaymentStatusAudits(payment.id)
    ])
    if (request !== paymentDetailsRequest || selectedPayment.value?.id !== payment.id) return
    selectedPayment.value = details
    paymentStatusAudits.value = audits
  } catch (error) {
    if (request !== paymentDetailsRequest || selectedPayment.value?.id !== payment.id) return
    paymentDetailsError.value = getErrorMessage(error, t('adminBilling.view.paymentDetailsError'))
    if (payment.paymentProviderCode !== 'payments-service') errorMessage.value = paymentDetailsError.value
  } finally {
    if (request === paymentDetailsRequest) isLoadingPaymentDetails.value = false
  }
}

async function selectSubscription(subscription: AdminBillingUserSubscription | AdminBillingOrganizationSubscription): Promise<void> {
  selectedSubscription.value = subscription
  selectedPayment.value = null
  extensionDays.value = 30
  subscriptionAction.value = 'isHistorical' in subscription && subscription.isHistorical ? 'grantPlan' : 'extend'
  grantPlanId.value = 0
  subscriptionReason.value = ''
  subscriptionAudits.value = []
  clearMessages()

  try {
    subscriptionAudits.value = await getAdminSubscriptionAudits(subscriptionScope.value, subscription.id)
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.subscriptionAuditsError'))
  }
}

function showPayments(): void {
  activeTab.value = 'payments'
  if (route.query.tab !== 'payments') {
    void router.replace({ query: { ...route.query, tab: 'payments' } })
  }
  selectedPurchase.value = null
  selectedSubscription.value = null
  subscriptionPlan.value = ''
}

function showPurchases(): void {
  activeTab.value = 'purchases'
  if (route.query.tab !== 'purchases') {
    void router.replace({ query: { ...route.query, tab: 'purchases' } })
  }
  selectedPayment.value = null
  selectedSubscription.value = null
}

function showSubscriptions(): void {
  activeTab.value = 'subscriptions'
  if (route.query.tab !== 'subscriptions') {
    void router.replace({ query: { ...route.query, tab: 'subscriptions' } })
  }
  selectedPayment.value = null
  selectedPurchase.value = null
}

function setSubscriptionScope(scope: 'users' | 'organizations'): void {
  subscriptionScope.value = scope
  selectedSubscription.value = null
  subscriptionAudits.value = []
  subscriptionPlan.value = ''
}

function showActiveSubscriptions(scope: 'users' | 'organizations'): void {
  showSubscriptions()
  setSubscriptionScope(scope)
  subscriptionStatus.value = 'active'
  subscriptionSource.value = ''
  search.value = ''
}

async function showPaymentSubscriptions(payment: AdminBillingPayment): Promise<void> {
  closeEditors()
  await router.push({ path: '/finance/billing', query: { tab: 'subscriptions' } })
  showActiveSubscriptions(payment.planTypeCode === 'business' ? 'organizations' : 'users')
  subscriptionStatus.value = ''
  search.value = payment.organizationName || payment.userEmail || payment.userPhone || payment.user
}

function closeEditors(): void {
  paymentDetailsRequest++
  isLoadingPaymentDetails.value = false
  paymentDetailsError.value = null
  selectedPayment.value = null
  selectedPurchase.value = null
  selectedSubscription.value = null
  subscriptionAudits.value = []
  paymentStatusAudits.value = []
  statusChangeReason.value = ''
  receiptChangeReason.value = ''
}

async function saveNpdReceiptStatus(): Promise<void> {
  const purchase = selectedPurchase.value
  const reason = receiptChangeReason.value.trim()
  if (!purchase || !reason) {
    return
  }

  const issued = !purchase.npdReceiptIssued
  const action = issued ? t('adminBilling.view.issueReceipt') : t('adminBilling.view.unissueReceipt')
  if (!window.confirm(t('adminBilling.view.confirmActionPrompt', { action }))) {
    return
  }

  isSaving.value = true
  clearMessages()

  try {
    const result = await markAdminNpdReceipt(purchase.paymentId, issued, purchase.npdReceiptIssued, reason)
    successMessage.value = result.message || t('adminBilling.view.receiptStatusSaved')
    purchases.value = await getAdminBillingPurchases()
    returnToList()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.saveReceiptStatusError'))
  } finally {
    isSaving.value = false
  }
}

async function saveNpdReceiptDetails(): Promise<void> {
  const purchase = selectedPurchase.value
  if (!purchase) {
    return
  }

  isSaving.value = true
  clearMessages()

  try {
    const result = await saveAdminNpdReceiptDetails(
      purchase.paymentId,
      receiptUrl.value.trim(),
      receiptComment.value.trim()
    )
    successMessage.value = result.message || t('adminBilling.view.receiptDetailsSaved')
    await refreshSelectedPurchase(purchase.paymentId)
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.saveReceiptDetailsError'))
  } finally {
    isSaving.value = false
  }
}

async function uploadNpdReceiptFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  const purchase = selectedPurchase.value
  if (!file || !purchase) {
    return
  }

  if (file.size > 5 * 1024 * 1024) {
    errorMessage.value = t('adminBilling.view.fileTooLarge')
    input.value = ''
    return
  }

  isUploadingReceipt.value = true
  clearMessages()

  try {
    const result = await uploadAdminNpdReceiptFile(purchase.paymentId, file)
    successMessage.value = result.message || t('adminBilling.view.receiptFileAttached')
    await refreshSelectedPurchase(purchase.paymentId)
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.attachFileError'))
  } finally {
    isUploadingReceipt.value = false
    input.value = ''
  }
}

async function removeNpdReceiptFile(): Promise<void> {
  const purchase = selectedPurchase.value
  if (!purchase || !window.confirm(t('adminBilling.view.removeReceiptFileConfirm'))) {
    return
  }

  isSaving.value = true
  clearMessages()

  try {
    const result = await deleteAdminNpdReceiptFile(purchase.paymentId)
    successMessage.value = result.message || t('adminBilling.view.receiptFileRemoved')
    await refreshSelectedPurchase(purchase.paymentId)
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.removeReceiptFileError'))
  } finally {
    isSaving.value = false
  }
}

async function refreshSelectedPurchase(paymentId: number): Promise<void> {
  purchases.value = await getAdminBillingPurchases()
  selectedPurchase.value = purchases.value.find(purchase => purchase.paymentId === paymentId) ?? null
}

async function sendNpdReceipt(purchase: AdminBillingPurchase): Promise<void> {
  if (sendingReceiptPaymentId.value !== null || getDeliveryStatus(purchase) === 'queued') {
    return
  }

  if (!purchase.userEmail) {
    errorMessage.value = t('adminBilling.view.emailMissing')
    return
  }

  if (!purchase.npdReceiptFileUrl && !purchase.npdReceiptUrl) {
    errorMessage.value = t('adminBilling.view.receiptRequired')
    return
  }

  sendingReceiptPaymentId.value = purchase.paymentId
  clearMessages()

  try {
    const result = await sendAdminNpdReceipt(purchase.paymentId)
    successMessage.value = result.message || t('adminBilling.view.receiptQueued')
    const selectedPaymentId = selectedPurchase.value?.paymentId
    purchases.value = await getAdminBillingPurchases()
    selectedPurchase.value = selectedPaymentId
      ? purchases.value.find(item => item.paymentId === selectedPaymentId) ?? null
      : null
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.sendReceiptError'))
  } finally {
    sendingReceiptPaymentId.value = null
  }
}

async function syncPayment(): Promise<void> {
  if (!selectedPayment.value) {
    return
  }

  isSyncing.value = true
  clearMessages()

  try {
    const result = await syncAdminPayment(selectedPayment.value.id)
    await loadBilling()
    returnToList()
    successMessage.value = result.message || t('adminBilling.view.paymentUpdated')
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.syncPaymentError'))
  } finally {
    isSyncing.value = false
  }
}

async function savePaymentStatus(): Promise<void> {
  if (!selectedPayment.value) {
    return
  }

  await runOperation(
    () => markAdminPaymentStatus(
      selectedPayment.value!.id,
      selectedStatus.value,
      selectedPayment.value!.paymentStatus,
      statusChangeReason.value.trim()
    ),
    t('adminBilling.view.paymentStatusSaved')
  )
}

async function extendSubscription(): Promise<void> {
  if (!selectedSubscription.value || !subscriptionReason.value.trim()) {
    return
  }

  await runOperation(
    () => extendAdminSubscription(subscriptionScope.value, selectedSubscription.value!.id, extensionDays.value, subscriptionReason.value.trim(), selectedSubscription.value!.endDate),
    t('adminBilling.view.subscriptionExtended')
  )
}

async function stopSubscription(): Promise<void> {
  if (!selectedSubscription.value || !subscriptionReason.value.trim()) {
    return
  }

  await runOperation(
    () => stopAdminSubscription(subscriptionScope.value, selectedSubscription.value!.id, subscriptionReason.value.trim(), selectedSubscription.value!.endDate),
    t('adminBilling.view.subscriptionStopped')
  )
}

async function changeDemo(): Promise<void> {
  if (!selectedSubscription.value || !subscriptionReason.value.trim()) {
    return
  }

  const subscription = selectedSubscription.value
  const reason = subscriptionReason.value.trim()
  const isGrant = subscriptionAction.value === 'grantDemo'
  if (isGrant && 'organizationId' in subscription && usesFreePro(subscription.primaryScenarioCode)) {
    return
  }
  const operation = 'organizationId' in subscription
    ? () => isGrant
      ? grantAdminOrganizationDemo(subscription.ownerUserId, subscription.organizationId, reason)
      : revokeAdminOrganizationDemo(subscription.ownerUserId, subscription.organizationId, reason)
    : () => isGrant
      ? grantAdminUserDemo(subscription.userId, reason)
      : revokeAdminUserDemo(subscription.userId, reason)

  await runOperation(operation, isGrant ? t('adminBilling.view.demoGranted') : t('adminBilling.view.demoRevoked'))
}

async function grantPlan(): Promise<void> {
  if (!selectedSubscription.value || hasActiveSubscriptionForClient(selectedSubscription.value) ||
    !grantablePlans.value.some(plan => plan.id === grantPlanId.value) || !previewEndDate.value || !subscriptionReason.value.trim()) {
    return
  }

  const subscription = selectedSubscription.value
  const id = 'organizationId' in subscription ? subscription.organizationId : subscription.userId

  await runOperation(
    () => grantAdminPlan(subscriptionScope.value, id, grantPlanId.value, extensionDays.value, subscriptionReason.value.trim()),
    t('adminBilling.view.planGranted')
  )
}

async function saveSubscriptionAction(): Promise<void> {
  if (subscriptionAction.value === 'extend') {
    await extendSubscription()
  } else if (subscriptionAction.value === 'stop') {
    await stopSubscription()
  } else if (subscriptionAction.value === 'grantPlan') {
    await grantPlan()
  } else {
    await changeDemo()
  }
}

async function runOperation(
  operation: () => Promise<{ message: string }>,
  fallbackMessage: string
): Promise<void> {
  isSaving.value = true
  clearMessages()

  try {
    const result = await operation()
    await loadBilling()
    if (isDetail.value) returnToList()
    else closeEditors()
    successMessage.value = result.message || fallbackMessage
  } catch (error) {
    errorMessage.value = getErrorMessage(error, t('adminBilling.view.operationError'))
  } finally {
    isSaving.value = false
  }
}

function clearMessages(): void {
  errorMessage.value = null
  successMessage.value = null
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.message : fallback
}

function formatMoney(value: number): string {
  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: 'RUB', currencyDisplay: 'code' }).format(value)
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat(formattingCulture.value, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value))
}

function formatDaysLeft(days: number): string {
  return days > 0 ? t('adminBilling.view.expiredInDays', { days }) : t('adminBilling.view.termExpired')
}

function getSourceName(source: string): string {
  const keys: Record<string, string> = {
    paid: 'sourcePaid', demo: 'sourceDemo', promo: 'sourcePromo', manual: 'sourceManual', unknown: 'sourceUnknown'
  }

  return keys[source] ? t(`adminBilling.view.${keys[source]}`) : source
}

function getAuditActionName(action: string): string {
  const keys: Record<string, string> = {
    extended: 'auditExtended', stopped: 'auditStopped', demo_granted: 'auditDemoGranted', demo_revoked: 'auditDemoRevoked', granted: 'auditPlanGranted'
  }

  return keys[action] ? t(`adminBilling.view.${keys[action]}`) : action
}

function getStatusName(status: string): string {
  const keys: Record<string, string> = {
    completed: 'statusCompleted', succeeded: 'statusSucceeded', paid_waiting_confirmation: 'statusPaidWaiting', pending: 'statusPending',
    waiting_for_capture: 'statusCapture', failed: 'statusFailed', canceled: 'statusCanceled', refunded: 'statusRefunded'
  }

  return keys[status] ? t(`adminBilling.view.${keys[status]}`) : status || t('adminBilling.view.statusUnknown')
}

function getBuyerTypeName(buyerType: AdminBillingPurchase['buyerType']): string {
  if (buyerType === 'individual') {
    return t('adminBilling.view.buyerIndividual')
  }

  if (buyerType === 'business') {
    return t('adminBilling.view.buyerBusiness')
  }

  return t('adminBilling.view.buyerUnknown')
}
</script>

<template>
  <section class="finance-page">
    <h1>{{ t('adminBilling.view.title') }}</h1>

    <button v-if="isDetail" class="secondary-button finance-back" type="button" @click="returnToList">{{ t('adminBilling.view.backToList') }}</button>

    <div v-if="billing && !isDetail" class="finance-kpis">
      <div><span>{{ t('adminBilling.view.paidAfterRefunds30') }}</span><strong>{{ formatMoney(billing.kpis.payments30DaysAmount) }}</strong></div>
      <div><span>{{ t('adminBilling.view.confirmedPayments30') }}</span><strong>{{ billing.kpis.payments30DaysCount }}</strong></div>
      <div><span>{{ t('adminBilling.view.pending') }}</span><strong>{{ billing.kpis.pendingPayments30Days }}</strong></div>
      <div><span>{{ t('adminBilling.view.problem') }}</span><strong>{{ billing.kpis.problemPayments30Days }}</strong></div>
      <button class="finance-kpi-action" type="button" @click="showActiveSubscriptions('organizations')"><span>{{ t('adminBilling.view.activeOrganizationSubscriptions') }}</span><strong>{{ billing.kpis.activeOrganizationSubscriptions }}</strong></button>
      <button class="finance-kpi-action" type="button" @click="showActiveSubscriptions('users')"><span>{{ t('adminBilling.view.activeUserSubscriptions') }}</span><strong>{{ billing.kpis.activeUserSubscriptions }}</strong></button>
      <div><span>{{ t('adminBilling.view.expiringSevenDays') }}</span><strong>{{ billing.kpis.expiringSubscriptions7Days }}</strong></div>
    </div>

    <div v-if="!isDetail" class="finance-tabs">
      <button type="button" :class="{ active: activeTab === 'payments' }" @click="showPayments">{{ t('adminBilling.view.paymentsTab') }}</button>
      <button type="button" :class="{ active: activeTab === 'purchases' }" @click="showPurchases">{{ t('adminBilling.view.purchasesTab') }}</button>
      <button type="button" :class="{ active: activeTab === 'subscriptions' }" @click="showSubscriptions">{{ t('adminBilling.view.subscriptionsTab') }}</button>
    </div>

    <div v-if="!isDetail" class="finance-toolbar">
      <input v-model="search" type="search" :placeholder="t('adminBilling.view.search')">
      <SearchSelect v-if="activeTab === 'payments'" v-model="paymentStatus" :options="[
        { value: '', label: t('adminBilling.view.allStatuses') },
        { value: 'success', label: t('adminBilling.view.successStatuses') },
        { value: 'pending', label: t('adminBilling.view.waitingStatuses') },
        { value: 'problem', label: t('adminBilling.view.problemStatuses') },
        { value: 'canceled', label: t('adminBilling.view.canceledStatuses') },
        { value: 'refunded', label: t('adminBilling.view.refunds') },
        { value: 'unknown', label: t('adminBilling.view.statusUnknown') },
        { value: 'other', label: t('adminBilling.view.otherStatuses') }
      ]" />
      <template v-else-if="activeTab === 'purchases'">
        <SearchSelect v-model="purchaseBuyerType" :options="[
          { value: '', label: t('adminBilling.view.allBuyers') },
          { value: 'individual', label: t('adminBilling.view.individuals') },
          { value: 'business', label: t('adminBilling.view.businesses') },
          { value: 'unknown', label: t('adminBilling.view.buyerTypeUnknown') }
        ]" />
        <SearchSelect v-model="purchaseReceiptStatus" :options="[
          { value: '', label: t('adminBilling.view.allReceipts') },
          { value: 'pending', label: t('adminBilling.view.receiptPending') },
          { value: 'issued', label: t('adminBilling.view.receiptIssued') }
        ]" />
        <SearchSelect v-model="purchaseQueueFilter" :options="[
          { value: 'attention', label: t('adminBilling.view.attentionQueue') },
          { value: 'all', label: t('adminBilling.view.allPurchases') },
          { value: 'overdue', label: t('adminBilling.view.overdueReceipts') },
          { value: 'email_failed', label: t('adminBilling.view.emailFailed') },
          { value: 'email_queued', label: t('adminBilling.view.emailQueued') },
          { value: 'email_sent', label: t('adminBilling.view.emailSent') },
          { value: 'refunded', label: t('adminBilling.view.refunds') }
        ]" />
      </template>
      <template v-else>
        <div class="finance-segmented">
          <button type="button" :class="{ active: subscriptionScope === 'users' }" @click="setSubscriptionScope('users')">{{ t('adminBilling.view.usersScope') }}</button>
          <button type="button" :class="{ active: subscriptionScope === 'organizations' }" @click="setSubscriptionScope('organizations')">{{ t('adminBilling.view.organizationsScope') }}</button>
        </div>
        <SearchSelect v-model="subscriptionStatus" :options="[
          { value: '', label: t('adminBilling.view.allStatuses') },
          { value: 'active', label: t('adminBilling.view.activeFilter') },
          { value: 'expiring', label: t('adminBilling.view.expiringFilter') },
          { value: 'ended', label: t('adminBilling.view.endedFilter') }
        ]" />
        <SearchSelect v-model="subscriptionPlan" :options="[
          { value: '', label: t('adminBilling.view.allPlans') },
          ...subscriptionPlans.map(plan => ({ value: plan, label: plan }))
        ]" />
        <SearchSelect v-model="subscriptionSource" :options="[
          { value: '', label: t('adminBilling.view.allSources') },
          { value: 'paid', label: t('adminBilling.view.sourcePaidLabel') },
          { value: 'demo', label: t('adminBilling.view.sourceDemoLabel') },
          { value: 'promo', label: t('adminBilling.view.sourcePromoLabel') },
          { value: 'manual', label: t('adminBilling.view.sourceAdminLabel') },
          { value: 'unknown', label: t('adminBilling.view.unknownFilter') }
        ]" />
      </template>
      <button class="secondary-button" type="button" :disabled="isLoading" @click="loadBilling">{{ t('adminBilling.view.refresh') }}</button>
    </div>

    <div v-if="activeTab === 'purchases' && !isDetail" class="finance-kpis">
      <div><span>{{ t('adminBilling.view.paidBeforeRefunds') }}</span><strong>{{ formatMoney(purchaseKpis.paidAmount) }}</strong></div>
      <div><span>{{ t('adminBilling.view.refunded') }}</span><strong>{{ formatMoney(purchaseKpis.refundedAmount) }}</strong></div>
      <div><span>{{ t('adminBilling.view.netAfterRefunds') }}</span><strong>{{ formatMoney(purchaseKpis.netAmount) }}</strong></div>
      <div><span>{{ t('adminBilling.view.needsAttention') }}</span><strong>{{ purchaseKpis.attentionCount }}</strong></div>
      <div><span>{{ t('adminBilling.view.overdueMoreThanDay') }}</span><strong>{{ purchaseKpis.overdueCount }}</strong></div>
    </div>

    <div v-if="errorMessage" class="form-message form-message--error">{{ errorMessage }}</div>
    <div v-if="successMessage" class="form-message form-message--success">{{ successMessage }}</div>
    <div v-if="isLoading && !billing" class="panel-state">{{ t('adminBilling.view.loading') }}</div>

    <div v-else-if="billing" class="finance-workspace" :class="{ 'finance-workspace--detail': isDetail }">
      <div v-if="!isDetail" class="finance-table-panel">
        <div class="table-wrap">
          <table v-if="activeTab === 'payments'" class="data-table finance-table">
            <thead><tr><th>{{ t('adminBilling.view.user') }}</th><th>{{ t('adminBilling.view.plan') }}</th><th>{{ t('adminBilling.view.amount') }}</th><th>{{ t('adminBilling.view.date') }}</th><th>{{ t('adminBilling.view.status') }}</th><th></th></tr></thead>
            <tbody>
              <tr v-for="payment in filteredPayments" :key="payment.id" @click="openRecord('payments', payment.id)">
                <td><strong>{{ payment.user }}</strong><small>{{ getAdminRecordLabel('paymentMethod', payment.paymentMethod ?? '', payment.paymentMethodName, locale) }}</small></td>
                <td>{{ payment.planName }}<small v-if="payment.promoCodeSnapshot">{{ payment.promoCodeSnapshot }}</small></td>
                <td>{{ formatMoney(payment.amount) }}</td>
                <td>
                  {{ formatDate(payment.createdAt) }}
                  <small v-if="payment.statusUpdatedAt">{{ t('adminBilling.view.statusValue', { status: formatDate(payment.statusUpdatedAt) }) }}</small>
                </td>
                <td><span class="status-badge" :class="`status-badge--${payment.statusGroup}`">{{ getAdminPaymentStatusLabel(payment, locale) }}</span></td>
                <td>
                  <button class="table-action" type="button" @click.stop="openRecord('payments', payment.id)">{{ t('adminBilling.view.open') }}</button>
                  <button v-if="payment.statusGroup === 'canceled'" class="table-action" type="button" @click.stop="showPaymentSubscriptions(payment)">{{ t('adminBilling.view.checkAccess') }}</button>
                </td>
              </tr>
            </tbody>
          </table>

          <table v-else-if="activeTab === 'purchases'" class="data-table finance-table finance-table--receipt-queue">
            <thead><tr><th>{{ t('adminBilling.view.buyer') }}</th><th>{{ t('adminBilling.view.plan') }}</th><th>{{ t('adminBilling.view.amount') }}</th><th>{{ t('adminBilling.view.paidAt') }}</th><th>{{ t('adminBilling.view.age') }}</th><th>{{ t('adminBilling.view.npdReceipt') }}</th><th>{{ t('adminBilling.view.receiptSending') }}</th><th>{{ t('adminBilling.view.actions') }}</th></tr></thead>
            <tbody>
              <tr v-for="purchase in filteredPurchases" :key="purchase.paymentId" :class="{ 'finance-receipt-overdue-row': isReceiptOverdue(purchase), 'finance-receipt-refunded-row': isRefundedPurchase(purchase) }" @click="openRecord('purchases', purchase.paymentId)">
                <td>
                  <RouterLink :to="`/users/${encodeURIComponent(purchase.userId)}`" @click.stop>{{ purchase.user }}</RouterLink>
                  <small>{{ getBuyerTypeName(purchase.buyerType) }}</small>
                  <small v-if="purchase.buyerType === 'business'">{{ purchase.buyerName || t('adminBilling.view.noName') }}<template v-if="purchase.buyerInn"> · {{ t('adminBilling.view.inn') }} {{ purchase.buyerInn }}</template></small>
                  <small>{{ purchase.userEmail || purchase.userPhone || t('adminBilling.view.noContacts') }}</small>
                </td>
                <td>{{ purchase.planName }}</td>
                <td>{{ formatMoney(purchase.amount) }}<small v-if="getRefundedAmount(purchase) > 0">{{ t('adminBilling.view.refundedAmount', { amount: formatMoney(getRefundedAmount(purchase)) }) }}</small></td>
                <td>{{ formatDate(purchase.paidAt) }}<small v-if="purchase.paymentStatus !== 'completed'">{{ getStatusName(purchase.paymentStatus) }}</small></td>
                <td><span :class="{ 'finance-receipt-age--overdue': isReceiptOverdue(purchase) }">{{ getReceiptAge(purchase) }}</span><small v-if="isReceiptOverdue(purchase)" class="finance-receipt-age--overdue">{{ t('adminBilling.view.receiptOverdue') }}</small></td>
                <td>
                  <span class="status-badge" :class="purchase.npdReceiptIssued ? 'status-badge--success' : 'status-badge--pending'">{{ purchase.npdReceiptIssued ? t('adminBilling.view.receiptIssued') : t('adminBilling.view.receiptNotMarked') }}</span>
                  <small v-if="purchase.npdReceiptIssuedAt">{{ formatDate(purchase.npdReceiptIssuedAt) }}</small>
                  <small v-if="purchase.npdReceiptMarkedBy">{{ purchase.npdReceiptMarkedBy }}</small>
                  <small v-if="purchase.npdReceiptUrl"><a :href="purchase.npdReceiptUrl" target="_blank" rel="noopener noreferrer" @click.stop>{{ t('adminBilling.view.receiptLink') }}</a></small>
                  <small v-if="purchase.npdReceiptFileUrl"><a :href="purchase.npdReceiptFileUrl" target="_blank" rel="noopener noreferrer" @click.stop>{{ purchase.npdReceiptFileName || t('adminBilling.view.attachment') }}</a></small>
                  <small v-if="purchase.npdReceiptComment">{{ purchase.npdReceiptComment }}</small>
                </td>
                <td>
                  <span class="status-badge" :class="['sent', 'delivered'].includes(getDeliveryStatus(purchase)) ? 'status-badge--success' : getDeliveryStatus(purchase) === 'failed' ? 'status-badge--problem' : 'status-badge--pending'">{{ getDeliveryStatusName(getDeliveryStatus(purchase)) }}</span>
                  <small v-if="purchase.npdReceiptSentAt">{{ formatDate(purchase.npdReceiptSentAt) }}</small>
                  <small v-if="purchase.npdReceiptSentTo">{{ purchase.npdReceiptSentTo }}</small>
                  <small v-if="purchase.npdReceiptSendError" class="finance-delivery-error">{{ purchase.npdReceiptSendError }}</small>
                </td>
                <td class="finance-receipt-actions">
                  <button v-if="canManage" class="table-action" type="button" :disabled="sendingReceiptPaymentId === purchase.paymentId || getDeliveryStatus(purchase) === 'queued' || isRefundedPurchase(purchase) || !purchase.userEmail || (!purchase.npdReceiptFileUrl && !purchase.npdReceiptUrl)" @click.stop="sendNpdReceipt(purchase)">{{ sendingReceiptPaymentId === purchase.paymentId ? t('adminBilling.view.sending') : getDeliveryStatus(purchase) === 'queued' ? t('adminBilling.view.sentQueued') : ['sent', 'delivered'].includes(getDeliveryStatus(purchase)) ? t('adminBilling.view.sendUpdatedReceipt') : getDeliveryStatus(purchase) === 'failed' ? t('adminBilling.view.retrySend') : t('adminBilling.view.issueReceiptButton') }}</button>
                  <button class="table-action" type="button" @click.stop="openRecord('purchases', purchase.paymentId)">{{ t('adminBilling.view.open') }}</button>
                  <small v-if="!purchase.userEmail">{{ t('adminBilling.view.noBuyerEmail') }}</small>
                  <small v-else-if="!purchase.npdReceiptFileUrl && !purchase.npdReceiptUrl">{{ t('adminBilling.view.addReceiptLinkOrFile') }}</small>
                </td>
              </tr>
              <tr v-if="filteredPurchases.length === 0"><td colspan="8" class="finance-empty">{{ t('adminBilling.view.noPurchasesMatch') }}</td></tr>
            </tbody>
          </table>

          <table v-else class="data-table finance-table">
            <thead><tr><th>{{ subscriptionScope === 'users' ? t('adminBilling.view.user') : t('adminBilling.view.organization') }}</th><th>{{ t('adminBilling.view.plan') }}</th><th>{{ t('adminBilling.view.source') }}</th><th>{{ t('adminBilling.view.period') }}</th><th>{{ t('adminBilling.view.remaining') }}</th><th>{{ t('adminBilling.view.status') }}</th><th></th></tr></thead>
            <tbody>
              <tr v-for="subscription in filteredSubscriptions" :key="`${subscriptionScope}-${subscription.id}`" @click="openRecord(subscriptionScope === 'users' ? 'user-subscriptions' : 'organization-subscriptions', subscription.id)">
                <td>
                  <RouterLink v-if="'organizationName' in subscription" :to="`/users/${encodeURIComponent(subscription.ownerUserId)}`" @click.stop>{{ subscription.organizationName }}</RouterLink>
                  <RouterLink v-else :to="`/users/${encodeURIComponent(subscription.userId)}`" @click.stop>{{ subscription.user }}</RouterLink>
                  <small v-if="'owner' in subscription">{{ subscription.owner }}</small>
                  <small v-else>{{ subscription.userEmail || subscription.userPhone }}</small>
                </td>
                <td>{{ subscription.planName }}</td>
                <td>{{ getSourceName(subscription.source) }}</td>
                <td>{{ formatDate(subscription.startDate) }}<small>{{ t('adminBilling.view.until', { date: formatDate(subscription.endDate) }) }}</small></td>
                <td :class="{ 'finance-expiring': subscription.isExpiringSoon }">{{ subscription.isActive ? formatDaysLeft(subscription.daysLeft) : t('adminBilling.view.subscriptionFinished') }}</td>
                <td><span class="status-badge" :class="subscription.isActive ? (subscription.isExpiringSoon ? 'status-badge--pending' : 'status-badge--success') : 'status-badge--problem'">{{ subscription.isActive ? (subscription.isExpiringSoon ? t('adminBilling.view.subscriptionExpiring') : t('adminBilling.view.subscriptionActive')) : t('adminBilling.view.subscriptionFinished') }}</span></td>
                <td><button class="table-action" type="button" @click.stop="openRecord(subscriptionScope === 'users' ? 'user-subscriptions' : 'organization-subscriptions', subscription.id)">{{ t('adminBilling.view.open') }}</button></td>
              </tr>
              <tr v-if="filteredSubscriptions.length === 0"><td colspan="7" class="finance-empty">{{ t('adminBilling.view.noSubscriptions') }}</td></tr>
            </tbody>
          </table>
        </div>
      </div>

      <form v-if="selectedPurchase" class="finance-editor finance-editor--page" @submit.prevent="saveNpdReceiptStatus">
        <div class="finance-editor__heading"><strong>{{ t('adminBilling.view.purchaseNumber', { id: selectedPurchase.paymentId }) }}</strong></div>
        <div class="finance-subscription-summary">
          <strong>{{ selectedPurchase.user }}</strong>
          <span>{{ selectedPurchase.planName }} · {{ formatMoney(selectedPurchase.amount) }}</span>
          <span>{{ getBuyerTypeName(selectedPurchase.buyerType) }}</span>
          <span v-if="selectedPurchase.buyerType === 'business'">{{ selectedPurchase.buyerName || t('adminBilling.view.noName') }}<template v-if="selectedPurchase.buyerInn"> · {{ t('adminBilling.view.inn') }} {{ selectedPurchase.buyerInn }}</template></span>
          <span>{{ t('adminBilling.view.paidOn', { date: formatDate(selectedPurchase.paidAt) }) }}</span>
          <span>{{ t('adminBilling.view.statusValue', { status: getStatusName(selectedPurchase.paymentStatus) }) }}</span>
          <span>{{ t('adminBilling.view.refundValue', { amount: formatMoney(getRefundedAmount(selectedPurchase)) }) }}</span>
          <span>{{ t('adminBilling.view.receiptValue', { status: selectedPurchase.npdReceiptIssued ? t('adminBilling.view.receiptIssuedAt', { date: formatDate(selectedPurchase.npdReceiptIssuedAt || selectedPurchase.paidAt) }) : t('adminBilling.view.receiptNotMarked') }) }}</span>
          <span>{{ t('adminBilling.view.sendingValue', { status: getDeliveryStatusName(getDeliveryStatus(selectedPurchase)) }) }}</span>
          <span v-if="selectedPurchase.npdReceiptSentTo">{{ t('adminBilling.view.address', { address: selectedPurchase.npdReceiptSentTo }) }}</span>
          <span v-if="selectedPurchase.npdReceiptSendError">{{ t('adminBilling.view.error', { message: selectedPurchase.npdReceiptSendError }) }}</span>
        </div>
        <template v-if="canManage">
        <button class="secondary-button" type="button" :disabled="sendingReceiptPaymentId === selectedPurchase.paymentId || getDeliveryStatus(selectedPurchase) === 'queued' || isRefundedPurchase(selectedPurchase) || !selectedPurchase.userEmail || (!selectedPurchase.npdReceiptFileUrl && !selectedPurchase.npdReceiptUrl)" @click="sendNpdReceipt(selectedPurchase)">{{ t('adminBilling.view.issueReceiptButton') }}</button>
        <div class="finance-warning">{{ t('adminBilling.view.receiptManualWarning') }}</div>
        <label><span>{{ t('adminBilling.view.receiptLink') }}</span><input v-model="receiptUrl" type="url" maxlength="2048" placeholder="https://..." /></label>
        <label><span>{{ t('adminBilling.view.comment') }}</span><textarea v-model="receiptComment" maxlength="1000" rows="3" :placeholder="t('adminBilling.view.receiptCommentPlaceholder')"></textarea></label>
        <button class="secondary-button" type="button" :disabled="isSaving" @click="saveNpdReceiptDetails">{{ t('adminBilling.view.saveReceiptDetails') }}</button>
        <label><span>{{ t('adminBilling.view.attachReceiptFile') }}</span><input type="file" accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg" :disabled="isUploadingReceipt" @change="uploadNpdReceiptFile" /></label>
        <div v-if="selectedPurchase.npdReceiptFileUrl" class="finance-subscription-summary">
          <a :href="selectedPurchase.npdReceiptFileUrl" target="_blank" rel="noopener noreferrer">{{ selectedPurchase.npdReceiptFileName || t('adminBilling.view.openReceiptFile') }}</a>
          <button class="danger-button" type="button" :disabled="isSaving" @click="removeNpdReceiptFile">{{ t('adminBilling.view.detachFile') }}</button>
        </div>
        <div v-else-if="selectedPurchase.npdReceiptFileName" class="finance-subscription-summary">
          <span>{{ t('adminBilling.view.attachedFilename', { filename: selectedPurchase.npdReceiptFileName }) }}</span>
          <button class="danger-button" type="button" :disabled="isSaving" @click="removeNpdReceiptFile">{{ t('adminBilling.view.detachFile') }}</button>
        </div>
        <label><span>{{ t('adminBilling.view.reasonFor', { reason: selectedPurchase.npdReceiptIssued ? t('adminBilling.view.revokeIssuedMark') : t('adminBilling.view.markIssued') }) }}</span><textarea v-model="receiptChangeReason" maxlength="500" rows="3" required></textarea></label>
        <button :class="selectedPurchase.npdReceiptIssued ? 'danger-button' : 'primary-button'" type="submit" :disabled="isSaving || !receiptChangeReason.trim()">{{ isSaving ? t('adminBilling.view.saving') : selectedPurchase.npdReceiptIssued ? t('adminBilling.view.removeMark') : t('adminBilling.view.markIssuedButton') }}</button>
        </template>
        <div v-if="selectedPurchase.npdReceiptReason" class="payment-audits">
          <strong>{{ t('adminBilling.view.lastReason') }}</strong>
          <small>{{ selectedPurchase.npdReceiptReason }}</small>
        </div>
      </form>

      <PaymentServiceDetails
        v-if="selectedPayment?.paymentProviderCode === 'payments-service'"
        :key="selectedPayment.id"
        :payment="selectedPayment"
        :can-manage="canManage"
        :actor-id="currentUserStore.currentUser?.id || ''"
        :loading="isLoadingPaymentDetails"
        :load-error="paymentDetailsError"
        @refresh="selectPayment(selectedPayment)"
        @subscriptions="showPaymentSubscriptions(selectedPayment)"
        @updated="selectPayment(selectedPayment)"
      />
      <form v-else-if="selectedPayment" class="finance-editor finance-editor--page" @submit.prevent="savePaymentStatus">
        <div class="finance-editor__heading"><strong>{{ t('adminBilling.view.paymentNumber', { id: selectedPayment.id }) }}</strong></div>
        <div class="finance-subscription-summary">
          <strong>{{ selectedPayment.user }}</strong>
          <span>{{ selectedPayment.planName }} · {{ formatMoney(selectedPayment.amount) }}</span>
          <span>{{ getAdminPaymentStatusLabel(selectedPayment, locale) }} · {{ getAdminRecordLabel('paymentMethod', selectedPayment.paymentMethod ?? '', selectedPayment.paymentMethodName, locale) }}</span>
          <span>{{ t('adminBilling.view.created', { date: formatDate(selectedPayment.createdAt) }) }}</span>
          <span v-if="selectedPayment.statusUpdatedAt">{{ t('adminBilling.view.statusChanged', { date: formatDate(selectedPayment.statusUpdatedAt) }) }}</span>
          <span v-if="selectedPayment.externalPaymentId">YooKassa: {{ selectedPayment.externalPaymentId }}</span>
          <span v-if="selectedPayment.transactionId">{{ t('adminBilling.view.transaction', { id: selectedPayment.transactionId }) }}</span>
        </div>
        <button v-if="canManage && selectedPayment.paymentProviderCode !== 'payments-service' && selectedPayment.hasExternalId" class="secondary-button" type="button" :disabled="isSyncing" @click="syncPayment">{{ isSyncing ? t('adminBilling.view.checking') : t('adminBilling.view.reconcileYooKassa') }}</button>
        <template v-if="canCorrectPaymentStatus">
          <div class="finance-warning">{{ t('adminBilling.view.manualStatusWarning') }}</div>
          <label><span>{{ t('adminBilling.view.status') }}</span><SearchSelect v-model="selectedStatus" :options="[{ value: 'completed', label: t('adminBilling.view.statusCompleted') }, { value: 'pending', label: t('adminBilling.view.statusPending') }, { value: 'failed', label: t('adminBilling.view.statusFailed') }, { value: 'canceled', label: t('adminBilling.view.statusCanceled') }, { value: 'refunded', label: t('adminBilling.view.statusRefunded') }]" /></label>
          <label><span>{{ t('adminBilling.view.reasonCorrection') }}</span><textarea v-model="statusChangeReason" maxlength="500" rows="3" required></textarea></label>
          <button class="danger-button" type="submit" :disabled="isSaving || !statusChangeReason.trim()">{{ t('adminBilling.view.changeLocalStatus') }}</button>
        </template>
        <div v-if="paymentStatusAudits.length > 0" class="payment-audits">
          <strong>{{ t('adminBilling.view.statusJournal') }}</strong>
          <div v-for="audit in paymentStatusAudits" :key="audit.id">
            <span>{{ getStatusName(audit.previousStatus) }} → {{ getStatusName(audit.newStatus) }}</span>
            <small>{{ formatDate(audit.changedAt) }} · {{ audit.changedBy }}</small>
            <small>{{ audit.source === 'manual' ? audit.reason : t('adminBilling.view.reconcileFromYooKassa') }}</small>
          </div>
        </div>
      </form>

      <form v-if="selectedSubscription" class="finance-editor finance-editor--page" @submit.prevent="saveSubscriptionAction">
        <div class="finance-editor__heading"><strong>{{ t('adminBilling.view.subscriptionNumber', { id: selectedSubscription.id }) }}</strong></div>
        <div class="finance-subscription-summary">
          <strong>{{ selectedSubscription.planName }}</strong>
          <span>{{ getSourceName(selectedSubscription.source) }} · {{ selectedSubscription.isActive ? t('adminBilling.view.subscriptionActive') : t('adminBilling.view.subscriptionFinished') }}</span>
          <span>{{ t('adminBilling.view.nowUntil', { date: formatDate(selectedSubscription.endDate) }) }}</span>
          <span>{{ t('adminBilling.view.startDate', { date: formatDate(selectedSubscription.startDate) }) }}</span>
        </div>
        <template v-if="canManage && (!('isHistorical' in selectedSubscription && selectedSubscription.isHistorical) || !hasActiveSubscriptionForClient(selectedSubscription))">
        <label><span>{{ t('adminBilling.view.action') }}</span><SearchSelect v-model="subscriptionAction" :options="[
          ...(!('isHistorical' in selectedSubscription && selectedSubscription.isHistorical) ? [{ value: 'extend', label: t('adminBilling.view.extendWithoutPayment') }] : []),
          ...(selectedSubscription.isActive ? [{ value: 'stop', label: t('adminBilling.view.finishSubscription') }] : []),
          ...(!hasActiveSubscriptionForClient(selectedSubscription) && (!('organizationId' in selectedSubscription) || !usesFreePro(selectedSubscription.primaryScenarioCode)) ? [{ value: 'grantDemo', label: t('adminBilling.view.grantDemo') }] : []),
          ...(!hasActiveSubscriptionForClient(selectedSubscription) && grantablePlans.length ? [{ value: 'grantPlan', label: t('adminBilling.view.grantPlanWithoutPayment') }] : []),
          ...(selectedSubscription.isDemoPlan && selectedSubscription.isActive ? [{ value: 'revokeDemo', label: t('adminBilling.view.revokeDemo') }] : [])
        ]" /></label>
        <label v-if="subscriptionAction === 'grantPlan'"><span>{{ t('adminBilling.view.plan') }}</span><SearchSelect v-model="grantPlanId" :options="[{ value: 0, label: t('adminBilling.view.selectPlan') }, ...grantablePlans.map(plan => ({ value: plan.id, label: plan.name }))]" /></label>
        <label v-if="subscriptionAction === 'extend' || subscriptionAction === 'grantPlan'"><span>{{ t('adminBilling.view.termDays') }}</span><input v-model.number="extensionDays" type="number" min="1" max="366" required></label>
        <div class="finance-warning">
          <template v-if="subscriptionAction === 'extend'">{{ t('adminBilling.view.finalTerm', { date: previewEndDate ? formatDate(previewEndDate) : t('adminBilling.view.specifyTerm') }) }}</template>
          <template v-else-if="subscriptionAction === 'grantPlan'">{{ t('adminBilling.view.newPlanTerm', { date: previewEndDate ? formatDate(previewEndDate) : t('adminBilling.view.afterPlanChoice') }) }}</template>
          <template v-else-if="subscriptionAction === 'stop'">{{ t('adminBilling.view.stopSubscriptionWarning') }}</template>
          <template v-else-if="subscriptionAction === 'grantDemo'">{{ t('adminBilling.view.grantDemoWarning') }}</template>
          <template v-else>{{ t('adminBilling.view.revokeDemoWarning') }}</template>
          <p v-if="'organizationId' in selectedSubscription && ['stop', 'revokeDemo'].includes(subscriptionAction)">{{ subscriptionFallback(selectedSubscription.primaryScenarioCode) }}</p>
        </div>
        <label><span>{{ t('adminBilling.view.reason') }}</span><textarea v-model="subscriptionReason" maxlength="500" rows="3" required></textarea></label>
        <button :class="subscriptionAction === 'stop' || subscriptionAction === 'revokeDemo' ? 'danger-button' : 'primary-button'" type="submit" :disabled="isSaving || !subscriptionReason.trim() || (['extend', 'grantPlan'].includes(subscriptionAction) && !previewEndDate)">{{ isSaving ? t('adminBilling.view.saving') : t('adminBilling.view.confirmAction') }}</button>
        </template>
        <div class="payment-audits">
          <strong>{{ t('adminBilling.view.journalActions') }}</strong>
          <div v-if="subscriptionAudits.length === 0"><small>{{ t('adminBilling.view.noAuditRecords') }}</small></div>
          <div v-for="audit in subscriptionAudits" :key="audit.id">
            <span>{{ getAuditActionName(audit.action) }}</span>
            <small>{{ formatDate(audit.changedAt) }} · {{ audit.changedBy }}</small>
            <small v-if="audit.previousEndDate || audit.newEndDate">{{ audit.previousEndDate ? formatDate(audit.previousEndDate) : t('adminBilling.view.noTerm') }} → {{ audit.newEndDate ? formatDate(audit.newEndDate) : t('adminBilling.view.noTerm') }}</small>
            <small>{{ audit.reason }}</small>
          </div>
        </div>
      </form>
    </div>
  </section>
</template>
