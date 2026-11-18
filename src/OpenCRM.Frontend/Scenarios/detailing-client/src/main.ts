import { createApp } from 'vue'
import { UiTooltip, initializeLocalization, initializeWorkspaceTheme } from '@opencrm/ui'
import App from './App.vue'
import { initializeUpdateNotice } from '../../../update-notice'
import '@opencrm/ui/styles.css'
import '@opencrm/workspace/styles.css'
import './styles.css'

initializeWorkspaceTheme()
initializeUpdateNotice('/clients/detailing/')

const app = createApp(App)

await initializeLocalization(app)
app.component('UiTooltip', UiTooltip)

app.mount('#app')
