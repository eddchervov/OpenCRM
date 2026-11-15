export function usesFreePro(primaryScenarioCode: string | null | undefined): boolean {
  return primaryScenarioCode === 'detailing' || primaryScenarioCode === 'service-station'
}

export function isFreePlan(plan: { group: string | null }): boolean {
  return plan.group === 'workspace.free.v1'
}

export function isRequiredFreeLimit(plan: { group: string | null }, code: string): boolean {
  return isFreePlan(plan) && ['workspace.clients.limit', 'workspace.vehicles.limit'].includes(code)
}

export function subscriptionFallback(primaryScenarioCode: string | null | undefined): string {
  return usesFreePro(primaryScenarioCode)
    ? 'После завершения PRO владелец продолжит базовую работу на Free. История сохранится; новые платные действия будут закрыты.'
    : 'После завершения подписки действуют прежние условия этого направления.'
}
