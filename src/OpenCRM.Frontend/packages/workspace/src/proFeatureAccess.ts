import type { ComputedRef, InjectionKey } from 'vue'

export const proFeatureBillingAccessKey: InjectionKey<ComputedRef<boolean>> = Symbol('pro-feature-billing-access')

export const commercialModuleEnabledKey: InjectionKey<ComputedRef<boolean>> = Symbol('commercial-module-enabled')
