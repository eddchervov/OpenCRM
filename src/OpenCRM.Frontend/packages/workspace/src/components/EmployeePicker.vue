<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { UiSelect, UiTooltip } from '@opencrm/ui'
import { loadEmployees, type OrganizationEmployee } from '../api/employees'
import { checkShiftAvailability } from '../api/shiftAvailability'
const props = defineProps<{ organizationId: string; modelValue?: string | null; currentName?: string | null; items?: OrganizationEmployee[]; disabled?: boolean; emptyLabel?: string; plannedStartLocal?: string; plannedEndLocal?: string; appendTo?: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const loaded = ref<OrganizationEmployee[]>([])
const error = ref('')
const loading = ref(false)
const isOutsideShift = ref(false)
const availabilityError = ref(false)
let availabilityRequestId = 0
const options = computed(() => {
  const items = (props.items ?? loaded.value).filter(item => item.isActive || item.id === props.modelValue)
  const options = [{ value: '', label: props.emptyLabel ?? 'Не назначен' }, ...items.map(item => ({ value: item.id, label: `${item.name}${item.isActive ? '' : ' · архив'}` }))]
  if (props.modelValue && !items.some(item => item.id === props.modelValue)) options.push({ value: props.modelValue, label: props.currentName || 'Назначенный сотрудник' })
  return options
})
async function load() {
  if (props.items) return
  loading.value = true
  error.value = ''
  try { loaded.value = (await loadEmployees(props.organizationId)).items }
  catch (e) { error.value = e instanceof Error ? e.message : 'Не удалось загрузить сотрудников.' }
  finally { loading.value = false }
}
onMounted(load)
watch(() => [props.organizationId, props.modelValue, props.plannedStartLocal, props.plannedEndLocal], async () => {
  const requestId = ++availabilityRequestId
  isOutsideShift.value = false
  availabilityError.value = false
  if (!props.modelValue || !props.plannedStartLocal || !props.plannedEndLocal
    || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(props.plannedStartLocal)
    || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(props.plannedEndLocal)
    || props.plannedEndLocal <= props.plannedStartLocal) return

  try {
    const result = await checkShiftAvailability(
      props.organizationId,
      props.modelValue,
      props.plannedStartLocal,
      props.plannedEndLocal
    )
    if (requestId === availabilityRequestId) isOutsideShift.value = result.isAvailable === false
  } catch {
    if (requestId === availabilityRequestId) availabilityError.value = true
  }
}, { immediate: true })
</script>
<template>
  <div class="employee-picker">
    <div class="employee-picker__field">
      <UiSelect :model-value="modelValue || ''" label="Сотрудник" :options="options" :disabled="disabled || loading || !!error" :append-to="appendTo" @update:model-value="emit('update:modelValue', $event)" />
      <span v-if="isOutsideShift" class="employee-picker__shift-hint">
        <UiTooltip text="По графику сотрудник вне смены. Назначить можно.">
          <button type="button" class="employee-picker__shift-warning" aria-label="Сотрудник вне смены: подробности">!</button>
        </UiTooltip>
      </span>
    </div>
    <p v-if="availabilityError" class="employee-picker__warning">Не удалось проверить график. Назначить можно.</p>
    <p v-if="error" role="alert">{{ error }} <button type="button" @click="load">Повторить</button></p>
  </div>
</template>
<style scoped>
.employee-picker { min-width:0; }
.employee-picker__field { display:flex; align-items:flex-end; gap:8px; }
.employee-picker__field > .ui-select-field { flex:1; min-width:0; }
.employee-picker__shift-hint { flex-shrink:0; margin-bottom:6px; }
.employee-picker p { color:var(--crm-danger-text, #a12622); font-size:12px; }
.employee-picker .employee-picker__warning { color:var(--crm-warning-text, #8a5600); }
.employee-picker button { color:inherit; background:transparent; border:0; text-decoration:underline; cursor:pointer; }
.employee-picker .employee-picker__shift-warning { display:grid; place-items:center; width:28px; height:28px; padding:0; border:1px solid var(--crm-danger-border, #e5aaa6); border-radius:50%; background:var(--crm-danger-surface, #fff1f0); color:var(--crm-danger-text, #a12622); font-family:inherit; font-size:16px; font-weight:700; line-height:1; text-decoration:none; }
.employee-picker__shift-warning:focus-visible { outline:2px solid var(--crm-danger-text, #a12622); outline-offset:2px; }
</style>
