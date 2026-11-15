<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'

const props = withDefaults(defineProps<{
  options: Array<{ value: string; label: string }>
  placeholder?: string
}>(), {
  placeholder: 'Выберите аудиторию'
})

const model = defineModel<string[]>({ required: true })
const root = ref<HTMLElement | null>(null)
const searchInput = ref<HTMLInputElement | null>(null)
const search = ref('')
const isOpen = ref(false)
const selectedLabels = computed(() => props.options.filter(option => model.value.includes(option.value)).map(option => option.label))
const filteredOptions = computed(() => {
  const query = search.value.trim().toLocaleLowerCase('ru')
  return query
    ? props.options.filter(option => option.label.toLocaleLowerCase('ru').includes(query))
    : props.options
})

onMounted(() => document.addEventListener('mousedown', closeFromOutside))
onBeforeUnmount(() => document.removeEventListener('mousedown', closeFromOutside))

function open(): void {
  isOpen.value = true
  void nextTick(() => searchInput.value?.focus())
}

function close(): void {
  isOpen.value = false
  search.value = ''
}

function toggle(value: string): void {
  model.value = model.value.includes(value)
    ? model.value.filter(item => item !== value)
    : [...model.value, value]
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    close()
  }
}

function closeFromOutside(event: MouseEvent): void {
  if (!root.value?.contains(event.target as Node)) {
    close()
  }
}
</script>

<template>
  <div ref="root" class="search-multi-select">
    <button class="search-multi-select__toggle" type="button" :aria-expanded="isOpen" @click="isOpen ? close() : open()">
      <span>{{ selectedLabels.length ? selectedLabels.join(', ') : placeholder }}</span>
      <span aria-hidden="true">{{ isOpen ? '▴' : '▾' }}</span>
    </button>
    <div v-if="isOpen" class="search-multi-select__menu" @keydown="handleKeydown">
      <input ref="searchInput" v-model="search" class="search-multi-select__search" type="search" placeholder="Поиск" aria-label="Поиск аудитории">
      <div class="search-multi-select__options">
        <label v-for="option in filteredOptions" :key="option.value">
          <input type="checkbox" :checked="model.includes(option.value)" @change="toggle(option.value)">
          <span>{{ option.label }}</span>
        </label>
        <small v-if="filteredOptions.length === 0">Ничего не найдено</small>
      </div>
      <div class="search-multi-select__footer">
        <span>Выбрано: {{ model.length }}</span>
        <button type="button" @click="close">Готово</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.search-multi-select {
  position: relative;
}

.search-multi-select__toggle {
  display: flex;
  width: 100%;
  min-height: 42px;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 12px;
  border: 1px solid #d6dbe5;
  border-radius: 7px;
  background: #fff;
  color: #202939;
  font: inherit;
  font-weight: 400;
  text-align: left;
}

.search-multi-select__toggle > span:first-child {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.search-multi-select__menu {
  position: absolute;
  z-index: 20;
  top: calc(100% + 4px);
  right: 0;
  left: 0;
  display: grid;
  gap: 8px;
  padding: 8px;
  border: 1px solid #d6dbe5;
  border-radius: 8px;
  background: #fff;
  box-shadow: 0 8px 24px rgb(16 24 40 / 14%);
}

.search-multi-select__search {
  width: 100%;
  min-height: 38px;
  padding: 8px 10px;
  border: 1px solid #d6dbe5;
  border-radius: 6px;
}

.search-multi-select__options {
  display: grid;
  gap: 4px;
  max-height: 180px;
  overflow: auto;
}

.search-multi-select__options label {
  display: flex;
  gap: 8px;
  align-items: center;
  padding: 8px 6px;
  border-radius: 5px;
  cursor: pointer;
}

.search-multi-select__options label:hover {
  background: #f3f5f8;
}

.search-multi-select__options small,
.search-multi-select__footer {
  color: #667085;
  font-size: 12px;
}

.search-multi-select__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.search-multi-select__footer button {
  border: 0;
  background: transparent;
  color: #2456a6;
  font: inherit;
  font-weight: 600;
}
</style>