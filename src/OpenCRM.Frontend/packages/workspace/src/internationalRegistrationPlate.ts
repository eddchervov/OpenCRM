import type { VehicleRegistrationPlateFormats } from './vehicleRegistrationPlate'

export function registrationPlateFormatsForCountry(config: VehicleRegistrationPlateFormats, countryCode?: string | null): VehicleRegistrationPlateFormats {
  if (!countryCode || countryCode === 'RU') return config

  return { maxLength: 24, letterPattern: '\\p{L}', characterMappings: {}, formats: [] }
}

export function internationalRegistrationPlateError(value: string): 'length' | 'characters' | 'short' | '' {
  if (!value.trim()) return ''
  if (value.trim().length > 24) return 'length'
  if (!/^[\p{L}\p{N} ·-]+$/u.test(value.trim())) return 'characters'
  if (Array.from(value).filter(character => /[\p{L}\p{N}]/u.test(character)).length < 2) return 'short'

  return ''
}
