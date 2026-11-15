import { MaskInput } from 'maska'
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
  error: HTMLDivElement
  other: HTMLInputElement
  label: HTMLLabelElement
}

const states = new Map<HTMLInputElement, State>()
let installed = false

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
  if (!config) return
  const value = state.vehicle.registrationPlate || ''
  const kind = state.vehicle.kind || 1
  const format = getVehicleRegistrationPlateFormat(kind, config)
  if (!state.inferred && !state.touched && state.vehicle.id && value) {
    state.inferred = true
    state.initialPlate = value
    state.initialKind = kind
    if (format && validateRegistrationPlate(value, kind, config)) {
      state.vm.$set(state.vehicle, 'allowOtherRegistrationPlateFormat', true)
    }
  }
  const other = state.vehicle.allowOtherRegistrationPlateFormat === true
  state.other.checked = other
  state.other.disabled = input.disabled
  state.label.hidden = !format
  input.maxLength = config.maxLength
  input.placeholder = format?.example || 'Номер в свободном формате'
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
  const label = document.createElement('label')
  label.className = 'form-check mt-1'
  const other = document.createElement('input')
  other.type = 'checkbox'
  other.className = 'form-check-input'
  const caption = document.createElement('span')
  caption.className = 'form-check-label'
  caption.textContent = 'Другой формат номера'
  label.append(other, caption)
  const error = document.createElement('div')
  error.className = 'invalid-feedback'
  error.hidden = true
  input.after(label, error)
  const state: State = {
    vehicle: binding.value, vm: vnode.context, key: '', raw: false, touched: false, inferred: false, error, other, label
  }
  states.set(input, state)
  other.addEventListener('change', () => {
    state.vm.$set(state.vehicle, 'allowOtherRegistrationPlateFormat', other.checked)
    state.touched = true
    state.key = ''
    showError(input, state, '')
    sync(input, state)
  })
  input.addEventListener('paste', event => {
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
    if (states.has(input)) showError(input, state, error instanceof Error ? error.message : 'Не удалось загрузить форматы госномеров.')
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
      states.delete(input)
    }
  })
}

export function validate(root: Document | HTMLElement = document) {
  for (const [input, state] of states) {
    if (!root.contains(input) || input.disabled || !input.getClientRects().length) continue
    if (state.vehicle.id && state.vehicle.registrationPlate === state.initialPlate &&
      (state.vehicle.kind || 1) === state.initialKind) {
      showError(input, state, '')
      continue
    }
    const message = state.config
      ? validateRegistrationPlate(state.vehicle.registrationPlate || '', state.vehicle.kind || 1, state.config,
        state.vehicle.allowOtherRegistrationPlateFormat === true)
      : 'Дождитесь загрузки форматов госномеров.'
    showError(input, state, message)
    if (message) {
      input.reportValidity()
      return false
    }
  }
  return true
}
