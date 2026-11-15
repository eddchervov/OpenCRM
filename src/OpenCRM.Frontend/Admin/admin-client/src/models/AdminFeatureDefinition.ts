export interface AdminFeatureDefinition {
  id: number
  code: string
  name: string
  description: string
  category: string | null
  scopeCode: string
  audienceCode: string
  valueType: string
  isActive: boolean
  isSystem: boolean
}
