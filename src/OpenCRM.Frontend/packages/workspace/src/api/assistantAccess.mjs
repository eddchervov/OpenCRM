export function isAssistantScenario(scenarioCode) {
  return ['detailing', 'service-station', 'autoservice'].includes((scenarioCode ?? '').toLowerCase())
}

export function getAssistantPreviewUrl(organizationId, workspaceUrl = '') {
  const workspacePath = workspaceUrl.split(/[?#]/)[0].replace(/\/$/, '')
  const pathBase = workspacePath.replace(/\/(?:detailing|autoservice)\/[^/]+$/, '')
  return `${pathBase}/assistant/${encodeURIComponent(organizationId)}`
}

export function getAssistantUrl(availability, organizationId, origin, workspaceUrl = '') {
  if (!availability?.visible || !availability.enabled || availability.organizationId !== organizationId || !availability.url) {
    return null
  }

  try {
    const candidate = new URL(availability.url, origin)
    const expectedPath = getAssistantPreviewUrl(organizationId, workspaceUrl)
    if (candidate.origin !== origin || candidate.pathname !== expectedPath) return null
    return `${candidate.pathname}${candidate.search}${candidate.hash}`
  } catch {
    return null
  }
}

export function getAssistantFrameUrl(availability, organizationId, origin, workspaceUrl = '') {
  const assistantUrl = getAssistantUrl(availability, organizationId, origin, workspaceUrl)
  return assistantUrl
    ? `${new URL(assistantUrl, origin).pathname}/embedded`
    : null
}
