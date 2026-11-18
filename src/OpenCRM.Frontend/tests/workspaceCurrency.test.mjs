import test from 'node:test'
import assert from 'node:assert/strict'
import { createSSRApp, computed, h } from 'vue'
import { renderToString } from '@vue/server-renderer'
import { provideWorkspaceCurrency, useWorkspaceCurrency } from '../packages/workspace/src/workspaceCurrency.ts'
import { formatLocaleCurrency } from '@opencrm/ui/i18n'

const amount = {
  setup() {
    const currency = useWorkspaceCurrency()
    return () => h('span', formatLocaleCurrency(1250.5, currency.value, { currencyDisplay: 'code' }, 'en-US'))
  }
}

test('Валюта организации передаётся денежным компонентам независимо от языка', async () => {
  const app = createSSRApp({
    setup() {
      provideWorkspaceCurrency(computed(() => 'CNY'))
      return () => h(amount)
    }
  })
  const result = await renderToString(app)
  assert.match(result, /CNY/)
  assert.match(result, /1,250\.50/)
  assert.doesNotMatch(result, /RUB|₽/)
})

test('Валюта сохранённого заказа имеет приоритет над текущей организацией', async () => {
  const order = {
    setup() {
      provideWorkspaceCurrency(computed(() => 'RUB'))
      return () => h(amount)
    }
  }
  const app = createSSRApp({
    setup() {
      provideWorkspaceCurrency(computed(() => 'USD'))
      return () => h(order)
    }
  })
  const result = await renderToString(app)
  assert.match(result, /RUB/)
  assert.doesNotMatch(result, /USD/)
})
