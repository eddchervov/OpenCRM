import { computed, hasInjectionContext, inject, provide, type ComputedRef, type InjectionKey } from 'vue'

const currencyKey: InjectionKey<ComputedRef<string>> = Symbol('workspaceCurrency')

export function provideWorkspaceCurrency(currency: ComputedRef<string>): ComputedRef<string> {
  provide(currencyKey, currency)

  return currency
}

export function useWorkspaceCurrency(): ComputedRef<string> {
  return (hasInjectionContext() ? inject(currencyKey, undefined) : undefined) ?? computed(() => 'RUB')
}
