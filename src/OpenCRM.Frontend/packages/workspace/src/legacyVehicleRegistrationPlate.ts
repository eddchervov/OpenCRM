import { MaskInput } from 'maska'
import { i18n, translate } from '@opencrm/ui/i18n'
import './localization'
import { internationalRegistrationPlateError } from './internationalRegistrationPlate'
import { registrationCountryOptions } from './utils/registrationCountries'
import {
  createVehicleRegistrationPlateMask,
  getVehicleRegistrationPlateFormat,
  loadVehicleRegistrationPlateFormats,
  normalizeRegistrationPlate,
  prepareRegistrationPlatePaste,
  validateRegistrationPlate,
  type VehicleRegistrationPlateFormats
} from './vehicleRegistrationPlate'

type Vehicle = {
  id?: string
  kind?: number
  registrationPlate?: string
  registrationCountryCode?: string | null
  allowOtherRegistrationPlateFormat?: boolean
}

type LegacyVm = { $set: (target: Vehicle, key: string, value: unknown) => void }
type Binding = { value: Vehicle }
type VNode = { context: LegacyVm }
type State = {
  vehicle: Vehicle
  vm: LegacyVm
  config?: VehicleRegistrationPlateFormats
  mask?: MaskInput
  key: string
  raw: boolean
  touched: boolean
  inferred: boolean
  initialPlate?: string
  initialKind?: number
  initialCountry?: string
  error: HTMLDivElement
  other: HTMLInputElement
  label: HTMLLabelElement
  country: HTMLSelectElement
  countryGroup: HTMLDivElement
}

const states = new Map<HTMLInputElement, State>()
let installed = false
let nextCountryId = 0

function isForeign(vehicle: Vehicle) {
  return Boolean(vehicle.registrationCountryCode && vehicle.registrationCountryCode !== 'RU')
}

function plateError(state: State) {
  if (isForeign(state.vehicle)) {
    const error = internationalRegistrationPlateError(state.vehicle.registrationPlate || '')
    const keys = { length: 'regional.plateLength', characters: 'regional.plateCharacters', short: 'regional.plateShort' }

    return error ? translate(keys[error]) : ''
  }

  return state.config
    ? validateRegistrationPlate(state.vehicle.registrationPlate || '', state.vehicle.kind || 1, state.config,
      state.vehicle.allowOtherRegistrationPlateFormat === true)
    : translate('regional.waitFormats')
}

function showError(input: HTMLInputElement, state: State, text: string) {
  state.error.textContent = text
  state.error.hidden = !text
  input.setCustomValidity(text)
  input.classList.toggle('is-invalid', Boolean(text))
  if (text) input.setAttribute('aria-invalid', 'true')
  else input.removeAttribute('aria-invalid')
}

function sync(input: HTMLInputElement, state: State) {
  const config = state.config
  const value = state.vehicle.registrationPlate || ''
  const kind = state.vehicle.kind || 1
  const foreign = isForeign(state.vehicle)
  const format = config ? getVehicleRegistrationPlateFormat(kind, config) : undefined
  state.country.value = state.vehicle.registrationCountryCode || 'RU'
  state.country.disabled = input.disabled
  if (!state.inferred && !state.touched && state.vehicle.id && value && (config || foreign)) {
    state.inferred = true
    state.initialPlate = value
    state.initialKind = kind
    state.initialCountry = state.vehicle.registrationCountryCode || ''
    if (!foreign && config && format && validateRegistrationPlate(value, kind, config)) {
      state.vm.$set(state.vehicle, 'allowOtherRegistrationPlateFormat', true)
    }
  }
  if (foreign) {
    state.mask?.destroy()
    state.mask = undefined
    state.key = ''
    state.label.hidden = true
    input.maxLength = 24
    input.placeholder = translate('regional.freePlate')
    showError(input, state, plateError(state))
    return
  }
  if (!config) return
  const other = state.vehicle.allowOtherRegistrationPlateFormat === true
  state.other.checked = other
  state.other.disabled = input.disabled
  state.label.hidden = !format
  input.maxLength = config.maxLength
  input.placeholder = format?.example || translate('regional.freePlate')
  const error = validateRegistrationPlate(value, kind, config, other)
  const formatChanged = !state.key.startsWith(`${kind}:${other}:`)
  if (value && !other && format && error && (!state.touched || formatChanged)) state.raw = true
  if (!error) state.raw = false
  const key = `${kind}:${other}:${state.raw}`
  if (state.key === key) return
  state.key = key
  state.mask?.destroy()
  state.mask = undefined
  if (!other && !state.raw) {
    const options = createVehicleRegistrationPlateMask(kind, config)
    if (options) state.mask = new MaskInput(input, options)
  }
}

function mount(input: HTMLInputElement, binding: Binding, vnode: VNode) {
  const countryGroup = document.createElement('div')
  countryGroup.className = 'mb-2'
  const countryLabel = document.createElement('label')
  countryLabel.className = 'form-label'
  countryLabel.textContent = translate('regional.registrationCountry')
  const country = document.createElement('select')
  country.id = `registration-country-${++nextCountryId}`
  country.className = 'form-select'
  countryLabel.htmlFor = country.id
  for (const option of [{ value: 'ZZ', label: translate('regional.unknownCountry') }, ...registrationCountryOptions(i18n.global.locale.value)]) {
    const element = document.createElement('option')
    element.value = option.value
    element.textContent = option.label
    country.append(element)
  }
  countryGroup.append(countryLabel, country)
  const plateLabel = input.labels?.[0]
  if (plateLabel && plateLabel.parentElement === input.parentElement) plateLabel.before(countryGroup)
  else input.before(countryGroup)
  const label = document.createElement('label')
  label.className = 'form-check mt-1'
  const other = document.createElement('input')
  other.type = 'checkbox'
  other.className = 'form-check-input'
  const caption = document.createElement('span')
  caption.className = 'form-check-label'
  caption.textContent = translate('regional.otherPlate')
  label.append(other, caption)
  const error = document.createElement('div')
  error.className = 'invalid-feedback'
  error.hidden = true
  input.after(label, error)
  const state: State = {
    vehicle: binding.value, vm: vnode.context, key: '', raw: false, touched: false, inferred: false, error, other, label, country, countryGroup
  }
  states.set(input, state)
  country.addEventListener('change', () => {
    state.vm.$set(state.vehicle, 'registrationCountryCode', country.value)
    state.touched = true
    state.key = ''
    sync(input, state)
    showError(input, state, plateError(state))
  })
  sync(input, state)
  other.addEventListener('change', () => {
    state.vm.$set(state.vehicle, 'allowOtherRegistrationPlateFormat', other.checked)
    state.touched = true
    state.key = ''
    showError(input, state, '')
    sync(input, state)
  })
  input.addEventListener('paste', event => {
    if (isForeign(state.vehicle)) return
    if (!state.config || !event.clipboardData) return
    event.preventDefault()
    state.touched = true
    const text = input.value.slice(0, input.selectionStart ?? 0) + event.clipboardData.getData('text') +
      input.value.slice(input.selectionEnd ?? input.value.length)
    const result = prepareRegistrationPlatePaste(text, state.vehicle.kind || 1, state.config,
      state.vehicle.allowOtherRegistrationPlateFormat === true)
    state.mask?.destroy()
    state.mask = undefined
    state.raw = Boolean(result.error)
    state.key = ''
    input.value = result.value
    state.vm.$set(state.vehicle, 'registrationPlate', result.value)
    showError(input, state, result.error)
    sync(input, state)
  })
  input.addEventListener('input', () => {
    state.touched = true
    if (isForeign(state.vehicle)) {
      state.vm.$set(state.vehicle, 'registrationPlate', input.value.trim())
      showError(input, state, plateError(state))
      return
    }
    if (!state.config) return
    const otherFormat = state.vehicle.allowOtherRegistrationPlateFormat === true ||
      !getVehicleRegistrationPlateFormat(state.vehicle.kind || 1, state.config)
    const value = normalizeRegistrationPlate(input.value, state.config, otherFormat)
    state.vm.$set(state.vehicle, 'registrationPlate', value)
    if (state.error.textContent && !validateRegistrationPlate(value, state.vehicle.kind || 1, state.config, otherFormat)) {
      showError(input, state, '')
    }
  })
  loadVehicleRegistrationPlateFormats().then(config => {
    if (!states.has(input)) return
    state.config = config
    sync(input, state)
  }).catch(error => {
    if (states.has(input) && !isForeign(state.vehicle)) {
      showError(input, state, error instanceof Error ? error.message : translate('regional.plateLoad'))
    }
  })
}

export function installVue(vue: { directive: (name: string, definition: object) => void }) {
  if (installed) return
  installed = true
  vue.directive('registration-plate', {
    inserted: mount,
    componentUpdated(input: HTMLInputElement, binding: Binding, vnode: VNode) {
      const state = states.get(input)
      if (!state) return
      if (state.vehicle !== binding.value) {
        state.vehicle = binding.value
        state.inferred = false
        state.initialPlate = undefined
        state.initialKind = undefined
        state.initialCountry = undefined
        state.touched = false
        state.key = ''
      }
      state.vm = vnode.context
      sync(input, state)
    },
    unbind(input: HTMLInputElement) {
      const state = states.get(input)
      state?.mask?.destroy()
      state?.label.remove()
      state?.error.remove()
      state?.countryGroup.remove()
      states.delete(input)
    }
  })
}

export function validate(root: Document | HTMLElement = document) {
  for (const [input, state] of states) {
    if (!root.contains(input) || input.disabled || !input.getClientRects().length) continue
    if (state.vehicle.id && state.vehicle.registrationPlate === state.initialPlate &&
      (state.vehicle.kind || 1) === state.initialKind && (state.vehicle.registrationCountryCode || '') === state.initialCountry) {
      showError(input, state, '')
      continue
    }
    const message = plateError(state)
    showError(input, state, message)
    if (message) {
      input.reportValidity()
      return false
    }
  }
  return true
}
