import { createApp, h, reactive } from 'vue'
import UiDatePicker from './components/UiDatePicker.vue'
import UiTimePicker from './components/UiTimePicker.vue'
import { initializeWorkspaceTheme, syncWorkspaceThemeFromDocument } from './workspaceTheme'
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
  createApp({
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
  }).mount(mount)
})
