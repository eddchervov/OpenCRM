<script setup lang="ts">
import { computed, onMounted, ref, useId } from 'vue'
import Multiselect from '@vueform/multiselect'
import '@vueform/multiselect/themes/default.css'
import UiFieldLabel from './UiFieldLabel.vue'
import { useLocalization } from '../i18n'

const model = defineModel<string>({ default: '' })
const { t } = useLocalization()

const props = withDefaults(defineProps<{
  appendTo?: string
  id?: string
  label?: string
  labelHelp?: string
  error?: string
  options: Array<{
    value: string
    label: string
  }>
  disabled?: boolean
  ariaLabel?: string
  describedBy?: string
  placeholder?: string
  searchable?: boolean
  required?: boolean
}>(), {
  appendTo: undefined,
  id: undefined,
  label: '',
  labelHelp: '',
  error: '',
  disabled: false,
  ariaLabel: '',
  describedBy: '',
  placeholder: '',
  searchable: true,
  required: false
})

const isMounted = ref(false)
onMounted(() => { isMounted.value = true })
const fallbackId = useId()
const selectId = computed(() => props.id ?? `ui-select-${fallbackId}`)
const descriptionIds = computed(() => [props.describedBy, props.error ? `${selectId.value}-error` : ''].filter(Boolean).join(' ') || undefined)
</script>

<template>
  <div class="ui-field ui-select-field">
    <UiFieldLabel :label="label" :required="required" :help="labelHelp" :for-id="selectId" />
    <Multiselect
      v-if="!appendTo || isMounted"
      :append-to="appendTo"
      :classes="appendTo ? { dropdown: 'multiselect-dropdown ui-select-options' } : {}"
      v-model="model"
      class="ui-select"
      :id="selectId"
      :options="options"
      :disabled="disabled"
      :searchable="searchable"
      :placeholder="placeholder || t('common.selectValue')"
      :aria="{ 'aria-label': ariaLabel || label, 'aria-invalid': !!error || undefined, 'aria-describedby': descriptionIds, 'aria-required': required || undefined }"
      open-direction="bottom"
      :hide-selected="false"
      :can-clear="false"
      :can-deselect="!required"
      value-prop="value"
      label="label"
      track-by="label"
      :no-options-text="t('common.emptyList')"
      :no-results-text="t('common.nothingFound')"
    />
    <span v-if="error" :id="`${selectId}-error`" class="ui-field__error" role="alert">{{ error }}</span>
  </div>
</template>

<style scoped>
.ui-select {
  width: 100%;
  min-width: 0;
  margin: 0;
}
</style>
