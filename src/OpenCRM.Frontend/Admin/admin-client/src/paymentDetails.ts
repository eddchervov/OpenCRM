import type { AdminBillingPayment, AdminBillingPaymentEvent } from './models/AdminBillingPayment'

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

export function getPaymentMoneyStatus(payment: AdminBillingPayment): string {
  const money = getPaymentMoneySummary(payment)
  if (money.refundKind === 'full') return 'Возврат подтверждён полностью'
  if (money.refundKind === 'partial') return 'Частичный возврат подтверждён'
  if (money.confirmed) return 'Оплата подтверждена'

  const status = normalize(payment.providerStatus || payment.paymentStatus)
  if (['canceled', 'cancelled', 'failed', 'rejected'].includes(status)) return 'Оплата не подтверждена'
  return 'Ожидается подтверждение оплаты'
}

export function getPaymentNotice(payment: AdminBillingPayment): PaymentNotice {
  const money = getPaymentMoneySummary(payment)
  const attention = normalize(payment.applicationStatus) === 'needs_attention'
  const applied = Boolean(payment.appliedAt) || normalize(payment.applicationStatus) === 'applied'
  const hasGrantedPeriod = applied || Boolean(payment.grantedFrom) || Boolean(payment.grantedUntil)

  if (money.refundKind !== 'none') {
    if (!hasGrantedPeriod && money.refundKind === 'full') {
      return {
        tone: attention ? 'warning' : 'neutral',
        title: 'Деньги возвращены полностью',
        message: `По этому платежу возвращено ${money.refunded.toLocaleString('ru-RU')} ₽ из ${money.amount.toLocaleString('ru-RU')} ₽. Доступ по этой покупке не выдавался.`,
        followUp: 'Сверьте сумму возврата в ЮKassa. Если состояние доступа вызывает вопросы, передайте ID платежа специалисту.',
      }
    }

    if (!hasGrantedPeriod) {
      return {
        tone: 'warning',
        title: 'Выполнен частичный возврат',
        message: `По этому платежу возвращено ${money.refunded.toLocaleString('ru-RU')} ₽ из ${money.amount.toLocaleString('ru-RU')} ₽. Выдача доступа не подтверждена.`,
        followUp: 'Сверьте сумму возврата в ЮKassa и вручную разберите условия покупки, прежде чем менять доступ.',
      }
    }

    return {
      tone: attention ? 'warning' : 'neutral',
      title: money.refundKind === 'full' ? 'Деньги возвращены полностью' : 'Выполнен частичный возврат',
      message: `По этому платежу возвращено ${money.refunded.toLocaleString('ru-RU')} ₽ из ${money.amount.toLocaleString('ru-RU')} ₽. Выданный период доступа автоматически не сокращается.`,
      followUp: 'Сверьте сумму возврата в ЮKassa и решите, оставить ли доступ или изменить его вручную в разделе подписок.',
    }
  }

  if (attention) {
    if (money.confirmed) {
      return {
        tone: 'warning',
        title: 'Деньги подтверждены, требуется проверка обработки покупки',
        message: 'ЮKassa подтвердила оплату. Проверяется обработка покупки или выдача доступа.',
        followUp: 'Проверьте операцию в ЮKassa по ID платежа и передайте ID специалисту, если причина не ясна. Не просите клиента платить повторно и не продлевайте доступ вручную.',
      }
    }

    return {
      tone: 'danger',
      title: 'Платёж требует проверки',
      message: 'Подтверждение оплаты пока не получено. Запись требует ручной проверки.',
      followUp: 'Проверьте операцию в ЮKassa по ID платежа. Если причина не ясна, передайте ID специалисту. Не просите клиента платить повторно до проверки.',
    }
  }

  if (money.confirmed && !applied) {
    return {
      tone: 'warning',
      title: 'Деньги получены, доступ ещё не выдан',
      message: 'Оплата подтверждена, но выдача доступа по этой покупке не отмечена.',
      followUp: 'Не создавайте повторную оплату и не продлевайте доступ вручную: это может привести к двойному списанию или продлению. Проверьте покупку и передайте её специалисту.',
    }
  }

  if (money.confirmed && applied) {
    return {
      tone: 'success',
      title: 'Оплата подтверждена, период по покупке выдан',
      message: 'Ниже показан период, который выдала именно эта покупка. Он не подтверждает, что доступ действует сейчас.',
      followUp: null,
    }
  }

  const status = normalize(payment.providerStatus || payment.paymentStatus)
  if (['canceled', 'cancelled', 'failed', 'rejected'].includes(status)) {
    return {
      tone: 'neutral',
      title: 'Оплата не подтверждена',
      message: 'Платёж отменён или завершился ошибкой. Доступ по этой покупке не выдан.',
      followUp: 'Если клиент утверждает, что деньги списались, сначала проверьте операцию в ЮKassa по ID платежа.',
    }
  }

  return {
    tone: 'neutral',
    title: 'Ожидается подтверждение оплаты',
    message: 'Пока нет подтверждения, что ЮKassa получила деньги. Доступ по этой покупке не считается выданным.',
    followUp: 'Обновите данные позже. Не просите клиента платить повторно, пока не проверили текущую операцию.',
  }
}

export function getPaymentAccessStatus(payment: AdminBillingPayment): string {
  const state = normalize(payment.applicationStatus)
  if (Boolean(payment.appliedAt) || state === 'applied') return 'Период по покупке выдан'
  if (getPaymentMoneySummary(payment).refundKind === 'full' || state === 'refunded') return 'Не выдан: средства возвращены'
  if (state === 'needs_attention' || getPaymentMoneySummary(payment).refundKind === 'partial') return 'Выдача требует проверки'
  if (isPaymentConfirmed(payment)) return 'Ожидает выдачи'
  return 'Не выдан'
}

export function formatPaymentEvent(event: AdminBillingPaymentEvent): { title: string; detail: string } | null {
  const stage = normalize(event.stage)
  const code = normalize(event.code)
  if (code === 'stale_event_ignored' || code === 'received' || stage === 'received') return null

  let title = 'Обновление платежа'
  if (code === 'refund_canceled' || code === 'refund_failed') title = 'Возврат не подтверждён'
  else if (code === 'refund_requires_access_review') title = 'Возврат требует решения по доступу'
  else if (stage === 'access_application' && code === 'applied') title = 'Подписка выдана'
  else if (stage === 'state' && code === 'verified_snapshot_applied') {
    title = (event.refundedAmountMinor ?? 0) > 0 ? 'Зафиксирован возврат' : 'Состояние платежа проверено'
  }

  const details = [
    event.amountMinor != null ? `Сумма покупки: ${formatMinorAmount(event.amountMinor, event.currency)}` : null,
    event.refundedAmountMinor != null && event.refundedAmountMinor > 0 ? `Возвращено всего: ${formatMinorAmount(event.refundedAmountMinor, event.currency)}` : null,
  ].filter(Boolean)
  return { title, detail: details.join(' · ') }
}

export function getVisiblePaymentEvents(events: AdminBillingPaymentEvent[] | undefined, limit = 5): AdminBillingPaymentEvent[] {
  return (events ?? [])
    .slice(0, 20)
    .filter(event => formatPaymentEvent(event) !== null)
    .slice(0, Math.max(0, limit))
}

export function formatMinorAmount(amountMinor: number, currency: string): string {
  const currencyCode = currency?.trim().toUpperCase() || 'RUB'
  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: currencyCode }).format(amountMinor / 100)
}

export function formatPaymentDate(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}
