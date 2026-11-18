import { createApp } from 'vue'
import App from './App.vue'
import { initializeUpdateNotice } from '../../../update-notice'
import SearchSelect from './components/SearchSelect.vue'
import { initializeLocalization } from '@opencrm/ui'
import router from './router'
import { pinia } from './stores/pinia'
import './styles.css'
import './users.css'
import './finance.css'
import './operations.css'
import './dashboard.css'
import './theme.css'

initializeUpdateNotice('/admin-panel/')

const app = createApp(App)
await initializeLocalization(app)

app
  .component('SearchSelect', SearchSelect)
  .use(pinia)
  .use(router)
  .mount('#app')
