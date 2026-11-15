import type { ComputedRef, InjectionKey } from 'vue'

export type PageBreadcrumb = { title: string, href?: string, onClick?: () => void }

export const pageBreadcrumbsKey: InjectionKey<ComputedRef<PageBreadcrumb[]>> = Symbol('pageBreadcrumbs')
