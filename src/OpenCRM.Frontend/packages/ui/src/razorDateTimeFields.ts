import { createApp, h, reactive } from 'vue'
import UiDatePicker from './components/UiDatePicker.vue'
import UiTimePicker from './components/UiTimePicker.vue'
import UiSelect from './components/UiSelect.vue'
import { initializeWorkspaceTheme, syncWorkspaceThemeFromDocument } from './workspaceTheme'
import { initializeLocalization } from './i18n'
import './styles.css'

initializeWorkspaceTheme()
new MutationObserver(syncWorkspaceThemeFromDocument).observe(document.documentElement, {
  attributes: true,
  attributeFilter: ['data-crm-theme']
})

document.querySelectorAll<HTMLElement>('[data-ui-date-picker], [data-ui-time-picker]').forEach(host => {
  const field = host.querySelector<HTMLInputElement>('input[type="hidden"]')
  if (!field) return

  const state = reactive({ value: field.value, disabled: field.disabled })
  const component = host.hasAttribute('data-ui-time-picker') ? UiTimePicker : UiDatePicker
  const mount = document.createElement('div')
  host.appendChild(mount)
  field.addEventListener('opencrm:field-sync', () => {
    state.value = field.value
    state.disabled = field.disabled
  })
  const app = createApp({
    render: () => h(component, {
      label: host.dataset.label || '',
      id: host.dataset.inputId,
      modelValue: state.value,
      disabled: state.disabled,
      'onUpdate:modelValue': (value: string) => {
        state.value = value
        field.value = value
        field.dispatchEvent(new Event('change', { bubbles: true }))
      }
    })
  })
  void initializeLocalization(app).then(() => app.mount(mount))
})

function addBrowserTimeZones(field: HTMLSelectElement) {
  if (typeof Intl.supportedValuesOf !== 'function') return

  const existingIds = new Set(Array.from(field.options, option => option.value))
  const now = new Date()
  for (const id of Intl.supportedValuesOf('timeZone')) {
    if (existingIds.has(id)) continue

    const name = new Intl.DateTimeFormat('en', { timeZone: id, timeZoneName: 'longOffset' })
      .formatToParts(now).find(part => part.type === 'timeZoneName')?.value
    const offset = name === 'GMT' ? '+00:00' : name?.slice(3)
    if (!offset) continue

    const [hours, minutes] = offset.slice(1).split(':').map(Number)
    const sign = offset.startsWith('-') ? -1 : 1
    const option = new Option('UTC' + offset + ' · ' + id, id)
    option.dataset.utcOffset = String(sign * (hours * 60 + minutes))
    field.add(option)
  }
}

document.querySelectorAll<HTMLElement>('[data-ui-select]').forEach(host => {
  const fallback = host.querySelector<HTMLElement>('[data-ui-select-fallback]')
  const field = fallback?.querySelector('select')
  if (!fallback || !field) return

  const mount = document.createElement('div')
  const { required, form } = field
  const state = reactive({ value: field.value, error: '' })
  const isTimeZone = field.hasAttribute('data-ui-time-zone')
  if (isTimeZone) addBrowserTimeZones(field)
  const nativeOptions = Array.from(field.options).filter(option => option.value)
  if (isTimeZone) {
    nativeOptions.sort((left, right) => Number(left.dataset.utcOffset) - Number(right.dataset.utcOffset)
      || left.value.localeCompare(right.value))
  }
  const options = nativeOptions.map(option => ({ value: option.value, label: option.text }))
  const placeholder = field.options[0]?.value === '' ? field.options[0].text : ''
  fallback.after(mount)

  const app = createApp({
    render: () => h(UiSelect, {
      id: field.id ? field.id + '-picker' : undefined,
      label: host.dataset.label,
      describedBy: field.getAttribute('aria-describedby') ?? undefined,
      placeholder,
      options,
      modelValue: state.value,
      disabled: field.disabled,
      searchable: host.dataset.searchable !== 'false',
      required,
      error: state.error,
      'onUpdate:modelValue': (value: string) => {
        state.value = value
        state.error = ''
        field.value = value
        field.dispatchEvent(new Event('change', { bubbles: true }))
      }
    })
  })
  void initializeLocalization(app).then(() => {
    state.value = field.value
    app.mount(mount)
    fallback.hidden = true
    field.required = false
    form?.addEventListener('submit', event => {
      if (!required || field.disabled || field.value) return

      event.preventDefault()
      state.error = host.dataset.requiredMessage ?? field.dataset.valRequired ?? ''
      mount.querySelector<HTMLElement>('[role="combobox"]')?.focus()
    }, { capture: true })
  })
})
