type WorkflowOrganization = {
  primaryScenarioCode?: string | null
  maintenanceWorkflowMode?: string | null
}

export function supportsMaintenanceWorkflow(organization: WorkflowOrganization): boolean {
  return organization.primaryScenarioCode === 'detailing' || organization.primaryScenarioCode === 'service-station'
}

export function isSimpleMaintenanceWorkflow(organization: WorkflowOrganization): boolean {
  return supportsMaintenanceWorkflow(organization) && organization.maintenanceWorkflowMode === 'simple'
}
