export interface AdminCatalogSuggestion {
  id: number
  kind: number
  kindText: string
  brandName: string
  modelName: string
  status: number
  statusText: string
  suggestedByEmail: string | null
  createdAt: string
  reviewedAt: string | null
  adminComment: string | null
}
