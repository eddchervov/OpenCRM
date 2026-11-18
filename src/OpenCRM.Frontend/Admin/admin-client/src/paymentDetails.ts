import type { AdminBillingPayment, AdminBillingPaymentEvent } from './models/AdminBillingPayment'
import { formattingCulture } from '@opencrm/ui/i18n'

export type PaymentNoticeTone = 'success' | 'warning' | 'danger' | 'neutral'

export interface PaymentNotice {
  tone: PaymentNoticeTone
  title: string
  message: string
  followUp: string | null
}

export interface PaymentMoneySummary {
  confirmed: boolean
  amount: number
  refunded: number
  remaining: number | null
  refundKind: 'none' | 'partial' | 'full'
}

export type PaymentTranslator = (key: string, params?: Record<string, string | number>) => string

const normalize = (value: string | null | undefined): string => (value ?? '').trim().toLowerCase()

export function isPaymentConfirmed(payment: AdminBillingPayment): boolean {
  return Boolean(payment.paidAt) || normalize(payment.providerStatus) === 'succeeded'
}

export function getPaymentMoneySummary(payment: AdminBillingPayment): PaymentMoneySummary {
  const amount = Math.max(0, Number(payment.amount) || 0)
  const refunded = Math.max(0, Number(payment.refundedAmount) || 0)
  const confirmed = isPaymentConfirmed(payment)
  const refundKind = refunded <= 0 ? 'none' : refunded >= amount ? 'full' : 'partial'

  return {
    confirmed,
    amount,
    refunded,
    remaining: confirmed ? Math.max(0, amount - refunded) : null,
    refundKind,
  }
}

export function getPaymentMoneyStatus(payment: AdminBillingPayment, translate: PaymentTranslator): string {
  const money = getPaymentMoneySummary(payment)
  if (money.refundKind === 'full') return translate('adminBilling.details.moneyStatusFullRefund')
  if (money.refundKind === 'partial') return translate('adminBilling.details.moneyStatusPartialRefund')
  if (money.confirmed) return translate('adminBilling.details.moneyStatusPaid')

  const status = normalize(payment.providerStatus || payment.paymentStatus)
  if (['canceled', 'cancelled', 'failed', 'rejected'].includes(status)) return translate('adminBilling.details.moneyStatusFailed')
  return translate('adminBilling.details.moneyStatusPending')
}

export function getPaymentNotice(payment: AdminBillingPayment, translate: PaymentTranslator, formatMoney: (value: number) => string): PaymentNotice {
  const money = getPaymentMoneySummary(payment)
  const attention = normalize(payment.applicationStatus) === 'needs_attention'
  const applied = Boolean(payment.appliedAt) || normalize(payment.applicationStatus) === 'applied'
  const hasGrantedPeriod = applied || Boolean(payment.grantedFrom) || Boolean(payment.grantedUntil)

  if (money.refundKind !== 'none') {
    if (!hasGrantedPeriod && money.refundKind === 'full') {
      return {
        tone: attention ? 'warning' : 'neutral',
        title: translate('adminBilling.details.noticeFullRefundTitle'),
        message: translate('adminBilling.details.noticeRefundNoAccess', { refunded: formatMoney(money.refunded), amount: formatMoney(money.amount) }),
        followUp: translate('adminBilling.details.followRefundNoAccess'),
      }
    }

    if (!hasGrantedPeriod) {
      return {
        tone: 'warning',
        title: translate('adminBilling.details.noticePartialRefundTitle'),
        message: translate('adminBilling.details.noticePartialNoAccess', { refunded: formatMoney(money.refunded), amount: formatMoney(money.amount) }),
        followUp: translate('adminBilling.details.followPartialNoAccess'),
      }
    }

    return {
      tone: attention ? 'warning' : 'neutral',
      title: translate(money.refundKind === 'full' ? 'adminBilling.details.noticeFullRefundTitle' : 'adminBilling.details.noticePartialRefundTitle'),
      message: translate('adminBilling.details.noticeRefundWithAccess', { refunded: formatMoney(money.refunded), amount: formatMoney(money.amount) }),
      followUp: translate('adminBilling.details.followRefundWithAccess'),
    }
  }

  if (attention) {
    if (money.confirmed) {
      return {
        tone: 'warning',
        title: translate('adminBilling.details.noticeAttentionTitle'),
        message: translate('adminBilling.details.noticeAttentionPaid'),
        followUp: translate('adminBilling.details.followAttentionPaid'),
      }
    }

    return {
      tone: 'danger',
      title: translate('adminBilling.details.noticeAttentionTitle'),
      message: translate('adminBilling.details.noticeAttentionUnpaid'),
      followUp: translate('adminBilling.details.followAttentionUnpaid'),
    }
  }

  if (money.confirmed && !applied) {
    return {
      tone: 'warning',
      title: translate('adminBilling.details.noticePaidPendingTitle'),
      message: translate('adminBilling.details.noticePaidPending'),
      followUp: translate('adminBilling.details.followPaidPending'),
    }
  }

  if (money.confirmed && applied) {
    return {
      tone: 'success',
      title: translate('adminBilling.details.noticePaidIssuedTitle'),
      message: translate('adminBilling.details.noticePaidIssued'),
      followUp: null,
    }
  }

  const status = normalize(payment.providerStatus || payment.paymentStatus)
  if (['canceled', 'cancelled', 'failed', 'rejected'].includes(status)) {
    return {
      tone: 'neutral',
      title: translate('adminBilling.details.noticeFailedTitle'),
      message: translate('adminBilling.details.noticeFailed'),
      followUp: translate('adminBilling.details.followFailed'),
    }
  }

  return {
    tone: 'neutral',
    title: translate('adminBilling.details.noticeAwaitingTitle'),
    message: translate('adminBilling.details.noticeAwaiting'),
    followUp: translate('adminBilling.details.followAwaiting'),
  }
}

export function getPaymentAccessStatus(payment: AdminBillingPayment, translate: PaymentTranslator): string {
  const state = normalize(payment.applicationStatus)
  if (Boolean(payment.appliedAt) || state === 'applied') return translate('adminBilling.details.accessIssued')
  if (getPaymentMoneySummary(payment).refundKind === 'full' || state === 'refunded') return translate('adminBilling.details.accessRefunded')
  if (state === 'needs_attention' || getPaymentMoneySummary(payment).refundKind === 'partial') return translate('adminBilling.details.accessReview')
  if (isPaymentConfirmed(payment)) return translate('adminBilling.details.accessPending')
  return translate('adminBilling.details.accessNotIssued')
}

export function formatPaymentEvent(event: AdminBillingPaymentEvent, translate: PaymentTranslator): { title: string; detail: string } | null {
  const stage = normalize(event.stage)
  const code = normalize(event.code)
  if (code === 'stale_event_ignored' || code === 'received' || stage === 'received') return null

  let title = translate('adminBilling.details.eventUpdate')
  if (code === 'refund_canceled' || code === 'refund_failed') title = translate('adminBilling.details.refundNotConfirmed')
  else if (code === 'refund_requires_access_review') title = translate('adminBilling.details.refundReview')
  else if (stage === 'access_application' && code === 'applied') title = translate('adminBilling.details.subscriptionGranted')
  else if (stage === 'state' && code === 'verified_snapshot_applied') {
    title = translate((event.refundedAmountMinor ?? 0) > 0 ? 'adminBilling.details.refundRecorded' : 'adminBilling.details.paymentChecked')
  }

  const details = [
    event.amountMinor != null ? translate('adminBilling.details.purchaseAmountDetail', { amount: formatMinorAmount(event.amountMinor, event.currency) }) : null,
    event.refundedAmountMinor != null && event.refundedAmountMinor > 0 ? translate('adminBilling.details.totalRefundedDetail', { amount: formatMinorAmount(event.refundedAmountMinor, event.currency) }) : null,
  ].filter(Boolean)
  return { title, detail: details.join(' · ') }
}

export function getVisiblePaymentEvents(events: AdminBillingPaymentEvent[] | undefined, limit = 5): AdminBillingPaymentEvent[] {
  return (events ?? [])
    .slice(0, 20)
    .filter(event => event.code !== 'stale_event_ignored' && event.code !== 'received' && event.stage !== 'received')
    .slice(0, Math.max(0, limit))
}

export function formatMinorAmount(amountMinor: number, currency: string): string {
  const currencyCode = currency?.trim().toUpperCase() || 'RUB'
  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: currencyCode, currencyDisplay: 'code' }).format(amountMinor / 100)
}

export function formatPaymentDate(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat(formattingCulture.value, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}
