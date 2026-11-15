import { createApp } from 'vue'
import { UiTooltip, initializeWorkspaceTheme } from '@opencrm/ui'
import App from './App.vue'
import { initializeUpdateNotice } from '../../../update-notice'
import '@opencrm/ui/styles.css'
import '@opencrm/workspace/styles.css'
import './styles.css'

initializeWorkspaceTheme()
initializeUpdateNotice('/clients/autoservice/')

const app = createApp(App)

app.component('UiTooltip', UiTooltip)

app.mount('#app')
