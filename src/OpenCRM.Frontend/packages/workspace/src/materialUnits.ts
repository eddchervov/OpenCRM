export const materialUnitOptions = [
  { value: 'шт', label: 'шт' },
  { value: 'л', label: 'л' },
  { value: 'мл', label: 'мл' },
  { value: 'кг', label: 'кг' },
  { value: 'г', label: 'г' },
  { value: 'м', label: 'м' },
  { value: 'м2', label: 'м2' },
  { value: 'компл', label: 'компл' }
]

export function normalizeMaterialUnit(value?: string | null): string {
  const unit = value?.trim() ?? ''
  return materialUnitOptions.some(option => option.value === unit) ? unit : 'шт'
}

export function isDiscreteMaterialUnit(value?: string | null): boolean {
  const unit = normalizeMaterialUnit(value)
  return unit === 'шт' || unit === 'компл'
}
