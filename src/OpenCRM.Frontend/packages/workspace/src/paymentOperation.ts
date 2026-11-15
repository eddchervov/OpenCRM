import type { WorkspaceActionResult } from './api/workspaceAdmin'

export type PaymentOperationScope = {
  userId: string
  organizationId: string
  kind: 'purchase' | 'extend'
  planId?: number
}

export function paymentOperationStorageKey(scope: PaymentOperationScope): string {
  return ['im', 'checkout', scope.userId, scope.organizationId, scope.kind, scope.planId ?? 'current']
    .map(value => encodeURIComponent(String(value))).join(':')
}

export function getOrCreatePaymentOperation(storage: Storage, scope: PaymentOperationScope, createId: () => string): string {
  const key = paymentOperationStorageKey(scope)
  const existing = storage.getItem(key)
  if (existing) {
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(existing)) {
      throw new Error('Не удалось восстановить попытку оплаты. Обратитесь в поддержку.')
    }
    return existing
  }

  const operationId = createId()
  storage.setItem(key, operationId)
  if (storage.getItem(key) !== operationId) {
    throw new Error('Не удалось сохранить попытку оплаты. Повторите позже.')
  }
  return operationId
}

export async function acquirePaymentOperation(scope: PaymentOperationScope): Promise<{ operationId: string, wasExisting: boolean }> {
  if (!navigator.locks) {
    throw new Error('Обновите браузер, чтобы безопасно продолжить оплату.')
  }
  return navigator.locks.request(paymentOperationStorageKey(scope), () => {
    const wasExisting = Boolean(window.localStorage.getItem(paymentOperationStorageKey(scope)))
    const operationId = getOrCreatePaymentOperation(window.localStorage, scope, () => crypto.randomUUID())
    return { operationId, wasExisting }
  })
}

export function finishPaymentOperation(storage: Storage, scope: PaymentOperationScope, operationId: string, code?: string | null) {
  if (!['Completed', 'Canceled', 'Rejected', 'Refunded'].includes(code ?? '')) {
    return
  }
  const key = paymentOperationStorageKey(scope)
  if (storage.getItem(key) === operationId) {
    storage.removeItem(key)
    storage.removeItem(paymentPurchaseStorageKey(scope))
  }
}

function paymentPurchaseStorageKey(scope: PaymentOperationScope): string {
  return `${paymentOperationStorageKey(scope)}:purchase`
}

function isFinalPaymentStatus(code?: string | null): boolean {
  return ['PaymentRequired', 'Completed', 'Canceled', 'Rejected', 'Refunded', 'ManualReview', 'Conflict', 'Invalid'].includes(code ?? '')
}

function waitForPoll(delayMs: number, signal?: AbortSignal): Promise<void> {
  if (signal?.aborted) {
    return Promise.reject(signal.reason ?? new DOMException('Опрос отменён.', 'AbortError'))
  }

  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', abort)
      resolve()
    }, delayMs)
    const abort = () => {
      clearTimeout(timer)
      signal?.removeEventListener('abort', abort)
      reject(signal?.reason ?? new DOMException('Опрос отменён.', 'AbortError'))
    }
    signal?.addEventListener('abort', abort, { once: true })
  })
}

export async function pollPaymentPurchase(
  purchaseId: string,
  load: (purchaseId: string, signal?: AbortSignal) => Promise<WorkspaceActionResult>,
  options: { signal?: AbortSignal, delaysMs?: number[] } = {}
): Promise<WorkspaceActionResult> {
  const delays = options.delaysMs ?? [500, 1000, 2000, 4000]
  let lastNetworkError: unknown
  let lastResult: WorkspaceActionResult | undefined

  for (let attempt = 0; attempt <= delays.length; attempt++) {
    if (options.signal?.aborted) {
      throw options.signal.reason ?? new DOMException('Опрос отменён.', 'AbortError')
    }

    try {
      lastResult = await load(purchaseId, options.signal)
      lastNetworkError = undefined
      if (isFinalPaymentStatus(lastResult.code)) {
        return lastResult
      }
    } catch (error) {
      if (options.signal?.aborted) {
        throw options.signal.reason ?? error
      }
      lastNetworkError = error
    }

    if (attempt < delays.length) {
      await waitForPoll(delays[attempt], options.signal)
    }
  }

  if (lastNetworkError) {
    throw lastNetworkError
  }

  return {
    ...lastResult,
    code: lastResult?.code ?? 'InProgress',
    message: lastResult?.isPaid ? 'Оплата получена, активируем подписку.' : lastResult?.message ?? 'Готовим оплату.'
  }
}

export async function submitPaymentOperation(
  scope: PaymentOperationScope,
  send: (operationId: string) => Promise<WorkspaceActionResult>,
  options: { loadPurchase?: (purchaseId: string, signal?: AbortSignal) => Promise<WorkspaceActionResult>, signal?: AbortSignal, delaysMs?: number[] } = {}
): Promise<WorkspaceActionResult> {
  const { operationId, wasExisting } = await acquirePaymentOperation(scope)
  let result: WorkspaceActionResult
  let resumedPurchase = false
  try {
    const purchaseStorageKey = paymentPurchaseStorageKey(scope)
    const savedPurchaseId = window.localStorage.getItem(purchaseStorageKey)
    if (savedPurchaseId && !options.loadPurchase) {
      throw new Error('Для продолжения оплаты обновите страницу и повторите проверку.')
    }
    if (savedPurchaseId && options.loadPurchase) {
      resumedPurchase = true
      result = await pollPaymentPurchase(savedPurchaseId, options.loadPurchase, { signal: options.signal, delaysMs: options.delaysMs })
    } else {
      result = await send(operationId)
      if (result.purchaseId) {
        window.localStorage.setItem(purchaseStorageKey, result.purchaseId)
      }
      if (result.code === 'InProgress' && result.purchaseId && options.loadPurchase) {
        result = await pollPaymentPurchase(result.purchaseId, options.loadPurchase, { signal: options.signal, delaysMs: options.delaysMs })
      }
    }
  } catch (error) {
    if (error instanceof Error && 'code' in error && typeof error.code === 'string') {
      finishPaymentOperation(window.localStorage, scope, operationId, error.code)
    }
    throw error
  }
  finishPaymentOperation(window.localStorage, scope, operationId, result.code)
  if (result.code === 'Completed' && wasExisting && !resumedPurchase) {
    throw new Error('Эта оплата уже обработана. Для новой покупки или продления нажмите кнопку ещё раз.')
  }
  if (resumedPurchase && result.code === 'Canceled') {
    return { ...result, message: 'Предыдущая оплата отменена. Чтобы начать новую, нажмите «Продолжить к оплате» ещё раз.' }
  }
  return result
}
