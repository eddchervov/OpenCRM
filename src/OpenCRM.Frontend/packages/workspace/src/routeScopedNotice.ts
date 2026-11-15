import { ref } from 'vue'

export type WorkspaceRouteIdentity = {
  section: string
  entityId?: string
  action?: string
  childId?: string
}

export function getWorkspaceRouteKey(route: WorkspaceRouteIdentity) {
  return [route.section, route.entityId ?? '', route.action ?? '', route.childId ?? ''].join('/')
}

export function createRouteScopedNotice() {
  const message = ref('')
  let targetRouteKey = ''

  function show(route: WorkspaceRouteIdentity, text?: string) {
    message.value = text ?? ''
    targetRouteKey = message.value ? getWorkspaceRouteKey(route) : ''
  }

  function navigate(route: WorkspaceRouteIdentity) {
    if (message.value && getWorkspaceRouteKey(route) !== targetRouteKey) {
      clear()
    }
  }

  function clear() {
    message.value = ''
    targetRouteKey = ''
  }

  return { message, show, navigate, clear }
}
