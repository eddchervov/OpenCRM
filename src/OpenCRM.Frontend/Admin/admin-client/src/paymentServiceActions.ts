import type { AdminPaymentServiceActionRequest, PaymentServiceActionKind } from './models/AdminPaymentServiceActions'

export const paymentActionLabels: Record<PaymentServiceActionKind, string> = {
  check: 'Проверить платёж',
  refund: 'Вернуть деньги',
  'retry-access': 'Повторить выдачу доступа',
  'resolve-review': 'Завершить разбор',
}

export function getPaymentActionStatus(status: string): string {
  const labels: Record<string, string> = {
    queued: 'В очереди', requested: 'Запрошено', processing: 'Выполняется',
    pending: 'Ожидает подтверждения', succeeded: 'Выполнено', completed: 'Выполнено',
    failed: 'Не выполнено', rejected: 'Отклонено', needs_attention: 'Нужна проверка', unknown: 'Результат уточняется',
  }

  return labels[status.toLowerCase()] || 'Результат уточняется'
}

export function parseRefundAmount(value: string, available: number): number | null {
  const normalized = value.trim().replace(',', '.')
  if (!/^\d+(?:\.\d{1,2})?$/.test(normalized)) return null
  const amount = Number(normalized)
  if (!Number.isFinite(amount) || !Number.isFinite(available) || amount <= 0 || amount > available) return null

  return Math.round(amount * 100) / 100
}

export function readPaymentActionDraft(value: string | null): AdminPaymentServiceActionRequest | null {
  if (!value) return null
  const draft = JSON.parse(value) as AdminPaymentServiceActionRequest
  if (!draft || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(draft.operationId) ||
    !Object.hasOwn(paymentActionLabels, draft.action) || typeof draft.reason !== 'string' || !draft.reason.trim() ||
    typeof draft.stateToken !== 'string' || !draft.stateToken ||
    (draft.action === 'refund' && (typeof draft.amount !== 'number' || !Number.isFinite(draft.amount) || draft.amount <= 0))) {
    throw new Error('Не удалось прочитать сохранённый запрос. Обновите операции и проверьте журнал перед новым действием.')
  }

  return draft
}
