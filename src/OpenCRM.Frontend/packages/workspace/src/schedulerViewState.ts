import { ref, toValue, watch, type MaybeRefOrGetter } from 'vue'

type SchedulerViewMode = 'day' | 'slots' | 'list' | 'calendar'

export function useSchedulerViewState(
  storageKey: MaybeRefOrGetter<string>,
  today: string,
  viewModeStorageKey: MaybeRefOrGetter<string>) {
  const selectedDate = ref(today)
  const listDays = ref(7)
  const viewMode = ref<SchedulerViewMode>('day')
  const slotDuration = ref(60)
  const postFilter = ref('')
  const daySearch = ref('')
  let isRestoring = false

  function restore() {
    isRestoring = true

    try {
      let state: Record<string, unknown> = {}
      let preferredMode: string | null = null

      try {
        const saved = JSON.parse(window.sessionStorage.getItem(toValue(storageKey)) ?? 'null')
        if (saved && typeof saved === 'object' && !Array.isArray(saved)) {
          state = saved
        }
      } catch {
        // Повреждённое или недоступное хранилище не должно мешать открытию записей.
      }

      try {
        preferredMode = window.localStorage.getItem(toValue(viewModeStorageKey))
      } catch {
        // Ранее выбранное представление может быть недоступно в настройках браузера.
      }

      const mode = state.viewMode ?? preferredMode
      const date = typeof state.selectedDate === 'string' ? state.selectedDate : ''
      const parsedDate = /^\d{4}-\d{2}-\d{2}$/.test(date) ? new Date(`${date}T00:00:00Z`) : null
      selectedDate.value = parsedDate && date >= '0001-01-01' && !Number.isNaN(parsedDate.getTime()) && parsedDate.toISOString().slice(0, 10) === date
        ? date
        : today

      const days = typeof state.listDays === 'number' ? state.listDays : 7
      const rangeEnd = new Date(Date.parse(`${selectedDate.value}T00:00:00Z`) + (days - 1) * 86400000)
      const hasValidRange = Number.isSafeInteger(days) && days > 0
        && !Number.isNaN(rangeEnd.getTime()) && rangeEnd.getUTCFullYear() <= 9999
      listDays.value = hasValidRange ? days : 7

      viewMode.value = mode === 'day' || mode === 'slots' || mode === 'list' || mode === 'calendar' ? mode : 'day'
      slotDuration.value = typeof state.slotDuration === 'number' && [30, 60, 90, 120, 180, 240].includes(state.slotDuration)
        ? state.slotDuration
        : 60

      postFilter.value = typeof state.postFilter === 'string' ? state.postFilter : ''
      daySearch.value = typeof state.daySearch === 'string' ? state.daySearch : ''
    } finally {
      isRestoring = false
    }
  }

  restore()
  watch(() => toValue(storageKey), restore, { flush: 'sync' })
  watch([selectedDate, listDays, viewMode, slotDuration, postFilter, daySearch], () => {
    if (isRestoring) {
      return
    }

    try {
      window.sessionStorage.setItem(toValue(storageKey), JSON.stringify({
        selectedDate: selectedDate.value,
        listDays: listDays.value,
        viewMode: viewMode.value,
        slotDuration: slotDuration.value,
        postFilter: postFilter.value,
        daySearch: daySearch.value
      }))
    } catch {
      // Без доступа к хранилищу просмотр работает до ухода со страницы.
    }
  }, { flush: 'sync' })

  return { selectedDate, listDays, viewMode, slotDuration, postFilter, daySearch }
}
