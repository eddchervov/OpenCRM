export function trackWorkspaceGoal(goal: string, params: Record<string, string | number | boolean> = {}) {
  window.dispatchEvent(new CustomEvent('opencrm:analytics-goal', {
    detail: { goal, params }
  }))
}
