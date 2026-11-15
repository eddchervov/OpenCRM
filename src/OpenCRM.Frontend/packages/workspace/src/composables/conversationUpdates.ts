import { computed, inject, onBeforeUnmount, provide, ref, watch, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import { conversationUrl, type InboxSummary } from '../api/conversations'

type ConversationUpdates = { unreadCount: Ref<number>; revision: Ref<string>; connected: Ref<boolean>; refresh: () => void }
const updatesKey: InjectionKey<ConversationUpdates> = Symbol('conversation-updates')

export function provideConversationUpdates(organizationId: ComputedRef<string | null>) {
  const unreadCount = ref(0)
  const revision = ref('')
  const connected = ref(false)
  let stream: EventSource | null = null
  let generation = 0
  let refreshing = false
  const refresh = async () => {
    const id = organizationId.value
    if (!id || refreshing) return
    const current = generation
    refreshing = true
    try {
      const response = await fetch(conversationUrl(id, '/summary'), { credentials: 'same-origin' })
      if (current !== generation) return
      if (!response.ok) {
        if ([401, 403, 404].includes(response.status)) {
          unreadCount.value = 0
          revision.value = `access-denied:${current}`
        }
        return
      }
      const summary: InboxSummary = await response.json()
      if (current !== generation) return
      unreadCount.value = summary.unreadCount
      revision.value = summary.revision
    } catch {
      // Повторное подключение потока восстановит актуальное состояние.
    } finally { refreshing = false }
  }
  function connect() {
    stream?.close()
    stream = null
    connected.value = false
    if (!organizationId.value || document.visibilityState !== 'visible') return
    const current = generation
    stream = new EventSource(conversationUrl(organizationId.value, '/events'))
    stream.onopen = () => { if (current === generation) connected.value = true }
    stream.onerror = () => {
      if (current !== generation) return
      connected.value = false
      void refresh()
    }
    stream.addEventListener('summary', event => {
      if (current !== generation) return
      try {
        const summary: InboxSummary = JSON.parse((event as MessageEvent).data)
        unreadCount.value = summary.unreadCount
        revision.value = summary.revision
      } catch {
        // Повреждённое событие не заменяет уже загруженные данные.
      }
    })
  }
  watch(organizationId, () => { generation++; unreadCount.value = 0; revision.value = ''; void refresh(); connect() }, { immediate: true })
  document.addEventListener('visibilitychange', connect)
  onBeforeUnmount(() => { generation++; stream?.close(); document.removeEventListener('visibilitychange', connect) })
  const updates = { unreadCount, revision, connected, refresh: () => { void refresh() } }
  provide(updatesKey, updates)
  return updates
}

export function useConversationUpdates() {
  return inject(updatesKey, { unreadCount: ref(0), revision: ref(''), connected: ref(false), refresh: () => {} })
}
