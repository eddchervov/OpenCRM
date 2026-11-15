<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { SearchSelectOption } from '@/models/SearchSelectOption'

const props = withDefaults(defineProps<{
  options: SearchSelectOption[]
  placeholder?: string
  disabled?: boolean
}>(), {
  placeholder: 'Выберите значение',
  disabled: false
})

const emit = defineEmits<{ change: [] }>()
const model = defineModel<string | number>({ required: true })
const root = ref<HTMLElement | null>(null)
const input = ref<HTMLInputElement | null>(null)
const query = ref('')
const isOpen = ref(false)
const activeIndex = ref(0)

const selectedOption = computed(() => props.options.find(option => option.value === model.value))
const canClear = computed(() => props.options.some(option => option.value === ''))
const filteredOptions = computed(() => {
  const normalizedQuery = query.value.trim().toLocaleLowerCase('ru')
  if (!normalizedQuery || selectedOption.value?.label === query.value) {
    return props.options
  }

  return props.options.filter(option => option.label.toLocaleLowerCase('ru').includes(normalizedQuery))
})

watch([model, selectedOption], () => {
  if (!isOpen.value) {
    query.value = selectedOption.value?.label ?? ''
  }
}, { immediate: true })

watch(filteredOptions, () => {
  activeIndex.value = 0
})

onMounted(() => document.addEventListener('mousedown', closeFromOutside))
onBeforeUnmount(() => document.removeEventListener('mousedown', closeFromOutside))

function open(): void {
  if (props.disabled || isOpen.value) {
    return
  }

  isOpen.value = true
  query.value = ''
  activeIndex.value = Math.max(0, filteredOptions.value.findIndex(option => option.value === model.value))
  void nextTick(() => input.value?.focus())
}

function close(): void {
  isOpen.value = false
  query.value = selectedOption.value?.label ?? ''
}

function select(option: SearchSelectOption): void {
  model.value = option.value
  close()
  emit('change')
}

function clear(): void {
  model.value = ''
  query.value = ''
  emit('change')
  void nextTick(() => input.value?.focus())
}

function handleKeydown(event: KeyboardEvent): void {
  if (!isOpen.value && ['ArrowDown', 'ArrowUp', 'Enter'].includes(event.key)) {
    event.preventDefault()
    open()
    return
  }

  if (event.key === 'Escape') {
    close()
    return
  }

  if (event.key === 'ArrowDown') {
    event.preventDefault()
    activeIndex.value = Math.min(activeIndex.value + 1, filteredOptions.value.length - 1)
    return
  }

  if (event.key === 'ArrowUp') {
    event.preventDefault()
    activeIndex.value = Math.max(activeIndex.value - 1, 0)
    return
  }

  if (event.key === 'Enter' && filteredOptions.value[activeIndex.value]) {
    event.preventDefault()
    select(filteredOptions.value[activeIndex.value])
  }
}

function closeFromOutside(event: MouseEvent): void {
  if (!root.value?.contains(event.target as Node)) {
    close()
  }
}
</script>

<template>
  <div ref="root" class="search-select" :class="{ 'search-select--open': isOpen, 'search-select--disabled': disabled }">
    <input ref="input" v-model="query" class="search-select__input" type="search" autocomplete="off" :disabled="disabled" :placeholder="placeholder" role="combobox" :aria-expanded="isOpen" @focus="open" @click="open" @keydown="handleKeydown">
    <button v-if="model !== '' && canClear && !disabled" class="search-select__clear" type="button" aria-label="Очистить" @click="clear">×</button>
    <button class="search-select__toggle" type="button" tabindex="-1" :disabled="disabled" aria-label="Открыть список" @click="isOpen ? close() : open()">⌄</button>
    <div v-if="isOpen" class="search-select__menu" role="listbox">
      <button v-for="(option, index) in filteredOptions" :key="option.value" type="button" class="search-select__option" :class="{ 'search-select__option--active': index === activeIndex, 'search-select__option--selected': option.value === model }" role="option" :aria-selected="option.value === model" @mouseenter="activeIndex = index" @mousedown.prevent="select(option)">{{ option.label }}</button>
      <div v-if="filteredOptions.length === 0" class="search-select__empty">Ничего не найдено</div>
    </div>
  </div>
</template>
