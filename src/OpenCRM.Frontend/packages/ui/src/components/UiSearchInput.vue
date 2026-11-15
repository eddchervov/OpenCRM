<script setup lang="ts">
import UiInput from './UiInput.vue'

const model = defineModel<string>({ default: '' })

withDefaults(defineProps<{
  id?: string
  label?: string
  placeholder?: string
  disabled?: boolean
}>(), {
  id: undefined,
  label: '',
  placeholder: '',
  disabled: false
})
</script>

<template>
  <div class="ui-search-input">
    <span class="ui-search-input__icon" aria-hidden="true">⌕</span>
    <UiInput v-model="model" :id="id" :label="label" :placeholder="placeholder" :disabled="disabled" />
  </div>
</template>

<style scoped>
.ui-search-input {
  width: 100%;
  min-width: 0;
}
</style>
