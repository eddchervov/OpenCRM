export { getAssistantPreviewUrl, getAssistantUrl, getAssistantFrameUrl, isAssistantScenario } from './assistantAccess.mjs'

export interface AssistantAvailability {
  pollingSeconds: number
  visible: boolean
  enabled: boolean
  organizationId: string
  url: string
}

export async function loadAssistantAvailability(organizationId: string): Promise<AssistantAvailability> {
  const query = new URLSearchParams({ organizationId })
  const pathBase = document.querySelector<HTMLMetaElement>('meta[name="opencrm-path-base"]')?.content.replace(/\/$/, '') ?? ''
  const response = await fetch(`${pathBase}/api/v1/assistant/availability?${query.toString()}`, {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' }
  })
  if (!response.ok) throw new Error(`Assistant availability request failed: ${response.status}`)
  return await response.json() as AssistantAvailability
}
