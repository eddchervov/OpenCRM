import { Mask, type MaskInputOptions } from 'maska'
import { i18n } from '@opencrm/ui/i18n'
import '@opencrm/workspace/plateMessages'

export type VehicleRegistrationPlateFormat = {
  code: string
  kinds: number[]
  masks: string[]
  pattern: string
  example: string
}

export type VehicleRegistrationPlateFormats = {
  maxLength: number
  letterPattern: string
  characterMappings: Record<string, string>
  formats: VehicleRegistrationPlateFormat[]
}

let formatsRequest: Promise<VehicleRegistrationPlateFormats> | undefined

export function loadVehicleRegistrationPlateFormats(): Promise<VehicleRegistrationPlateFormats> {
  formatsRequest ??= fetch('/api/v1/vehicles/registration-plate-formats', {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' }
  }).then(async response => {
    if (!response.ok) {
      throw new Error(i18n.global.t(response.status === 401 ? 'plate.signIn' : 'plate.loadError'))
    }

    const formats = await response.json() as VehicleRegistrationPlateFormats
    if (!formats || !Number.isInteger(formats.maxLength) || !formats.letterPattern || !Array.isArray(formats.formats)) {
      throw new Error(i18n.global.t('plate.invalidConfig'))
    }

    return formats
  }).catch(error => {
    formatsRequest = undefined
    throw error
  })

  return formatsRequest
}

export function getVehicleRegistrationPlateFormat(kind: string | number, config: VehicleRegistrationPlateFormats) {
  const vehicleKind = Number(kind) || 1
  return config.formats.find(format => format.kinds.includes(vehicleKind))
}

export function normalizeRegistrationPlate(value: string, config: VehicleRegistrationPlateFormats, allowOtherFormat = false) {
  if (allowOtherFormat) return value

  const characterMappings = new Map(Object.entries(config.characterMappings).map(([from, to]) => [from.toUpperCase(), to.toUpperCase()]))

  return Array.from(value.toUpperCase())
    .filter(character => !/[\s-]/u.test(character))
    .map(character => characterMappings.get(character) ?? character)
    .join('')
}

export function matchesRegistrationPlateSearch(value: string | null | undefined, query: string, config: VehicleRegistrationPlateFormats | null) {
  if (!value || !config) return false
  const normalizedQuery = normalizeRegistrationPlate(query, config)
  return normalizedQuery.length > 0 && normalizeRegistrationPlate(value, config).includes(normalizedQuery)
}

export function validateRegistrationPlate(value: string, kind: string | number, config: VehicleRegistrationPlateFormats, allowOtherFormat = false): string {
  if (!value.trim()) return ''
  if (value.length > config.maxLength) return i18n.global.t('plate.length', { max: config.maxLength })

  if (allowOtherFormat) {
    if (value.trim().length < 2) return i18n.global.t('plate.short')
    return /^[\p{L}\p{N}\s-]+$/u.test(value) ? '' : i18n.global.t('plate.characters')
  }

  const format = getVehicleRegistrationPlateFormat(kind, config)
  if (!format) {
    if (value.trim().length < 2) return i18n.global.t('plate.short')
    return /^[\p{L}\p{N}\s-]+$/u.test(value) ? '' : i18n.global.t('plate.characters')
  }

  try {
    return new RegExp(format.pattern, 'u').test(normalizeRegistrationPlate(value, config))
      ? ''
      : i18n.global.t('plate.format', { example: format.example })
  } catch {
    return i18n.global.t('plate.validationError')
  }
}

export function prepareRegistrationPlatePaste(
  pastedValue: string,
  kind: string | number,
  config: VehicleRegistrationPlateFormats,
  allowOtherFormat = false,
  currentValue = '',
  selectionStart = 0,
  selectionEnd = currentValue.length
) {
  const value = currentValue.slice(0, selectionStart) + pastedValue + currentValue.slice(selectionEnd)
  if (allowOtherFormat || !getVehicleRegistrationPlateFormat(kind, config)) {
    return { value, error: '', preventDefault: false }
  }

  const normalized = normalizeRegistrationPlate(value, config)
  const error = validateRegistrationPlate(normalized, kind, config)
  if (!error) {
    return {
      value: normalized,
      error: '',
      preventDefault: true,
      selectionStart: normalizeRegistrationPlate(currentValue.slice(0, selectionStart) + pastedValue, config).length
    }
  }

  const maskOptions = createVehicleRegistrationPlateMask(kind, config)
  if (maskOptions && new Mask(maskOptions).masked(normalized) === normalized) {
    return { value, error: '', preventDefault: false }
  }

  return { value, error, preventDefault: true }
}

export function createVehicleRegistrationPlateMask(kind: string | number, config: VehicleRegistrationPlateFormats): MaskInputOptions | undefined {
  const format = getVehicleRegistrationPlateFormat(kind, config)
  if (!format || !format.masks.length) return undefined

  return {
    mask: format.masks.length === 1 ? format.masks[0] : format.masks,
    tokens: {
      L: { pattern: new RegExp(config.letterPattern, 'u'), transform: character => character.toUpperCase() },
      '#': { pattern: /\d/ }
    },
    preProcess: value => normalizeRegistrationPlate(value, config)
  }
}
