export interface AdminEmailComposeData {
  organizationTypes: Array<{ code: string; name: string }>
  users: Array<{ id: string; email: string; displayName: string; emailConfirmed: boolean; advertisingConsentAccepted: boolean }>
}
