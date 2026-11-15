import { ApiError } from './ApiError'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
const safeMethods = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE'])

let antiforgeryTokenPromise: Promise<string> | null = null

async function getAntiforgeryToken(): Promise<string> {
  antiforgeryTokenPromise ??= fetch(`${apiBaseUrl}/api/v1/antiforgery/token`, {
    credentials: 'include',
    headers: {
      Accept: 'application/json'
    }
  })
    .then(async response => {
      if (!response.ok) {
        throw new ApiError(response.status, 'Не удалось получить защитный токен запроса.')
      }

      const payload = await response.json() as { requestToken?: string }
      if (!payload.requestToken) {
        throw new Error('Сервер не вернул защитный токен запроса.')
      }

      return payload.requestToken
    })
    .catch(error => {
      antiforgeryTokenPromise = null
      throw error
    })

  return await antiforgeryTokenPromise
}

export async function request<TResponse>(
  path: string,
  options: RequestInit = {}
): Promise<TResponse> {
  const method = (options.method ?? 'GET').toUpperCase()
  const headers = new Headers(options.headers)
  headers.set('Accept', 'application/json')

  if (!safeMethods.has(method) && !headers.has('Authorization')) {
    headers.set('RequestVerificationToken', await getAntiforgeryToken())
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...options,
    credentials: 'include',
    headers
  })

  if (!response.ok) {
    const payload = await response.json().catch(() => null) as { message?: string; code?: string } | null
    throw new ApiError(
      response.status,
      payload?.message ?? `API request failed with status ${response.status}.`,
      payload?.code
    )
  }

  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as TResponse
  }

  const responseText = await response.text()
  if (!responseText) {
    return undefined as TResponse
  }

  return JSON.parse(responseText) as TResponse
}
