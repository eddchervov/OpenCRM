export function isAssistantScenario(scenarioCode: string | null | undefined): boolean
export function getAssistantPreviewUrl(organizationId: string, workspaceUrl?: string): string
export function getAssistantUrl(
  availability: {
    visible: boolean
    enabled: boolean
    organizationId: string
    url: string
  } | null,
  organizationId: string,
  origin: string,
  workspaceUrl?: string
): string | null

export function getAssistantFrameUrl(...args: Parameters<typeof getAssistantUrl>): string | null
