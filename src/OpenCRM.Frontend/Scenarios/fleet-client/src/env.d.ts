/// <reference types="vite/client" />

interface Window {
  opencrmFleetClient?: {
    organizationId?: string
    contextUrl?: string
    workspaceUrl?: string
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
