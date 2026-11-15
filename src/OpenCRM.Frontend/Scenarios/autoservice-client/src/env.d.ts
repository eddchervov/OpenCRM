/// <reference types="vite/client" />

import type { AutoServiceWorkspaceContext } from '@/api/autoserviceBff'

declare global {
  interface Window {
    opencrmAutoServiceClient?: {
      organizationId?: string
      contextUrl?: string
      workspaceUrl?: string
      context?: AutoServiceWorkspaceContext
    }
    FullCalendar?: {
      Calendar: new (element: HTMLElement, options: Record<string, unknown>) => {
        render: () => void
        destroy: () => void
        updateSize: () => void
        changeView: (viewName: string) => void
      setOption: (name: string, value: unknown) => void
        view?: { type?: string }
      }
    }
  }
}

export {}
