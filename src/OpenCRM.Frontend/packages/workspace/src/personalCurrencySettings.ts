import { createApp, h } from 'vue'
import { initializeLocalization } from '@opencrm/ui/i18n'
import CurrencySettings from './components/CurrencySettings.vue'
import { RegionalPreferences } from '@opencrm/ui'
import '@opencrm/ui/styles.css'

document.querySelectorAll<HTMLElement>('[data-personal-currency-settings]').forEach(host => {
  const app = createApp({ render: () => h('div', [h(RegionalPreferences), h(CurrencySettings)]) })
  void initializeLocalization(app).then(() => app.mount(host))
})
