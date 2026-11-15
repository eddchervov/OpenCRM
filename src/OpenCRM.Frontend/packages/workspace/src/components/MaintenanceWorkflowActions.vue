<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { UiButton } from '@opencrm/ui'
import MaintenanceCardActions from './MaintenanceCardActions.vue'
import { loadMaintenance, type WorkspaceMaintenance } from '../api/maintenance'

const props = defineProps<{
  organizationId: string
  maintenanceId: string
  status: number
  disabled?: boolean
  compact?: boolean
  simpleWorkflow?: boolean
}>()
const emit = defineEmits<{ changed: [status: number], error: [message: string] }>()
const maintenance = ref<WorkspaceMaintenance | null>(null)
const actions = ref<InstanceType<typeof MaintenanceCardActions> | null>(null)
const loading = ref(false)

async function open() {
  if (props.disabled || loading.value) return
  loading.value = true
  try {
    maintenance.value = await loadMaintenance(props.organizationId, props.maintenanceId)
    await nextTick()
    actions.value?.show()
  } catch (failure) {
    emit('error', failure instanceof Error ? failure.message : 'Не удалось загрузить запись.')
  } finally {
    loading.value = false
  }
}

async function changed() {
  try {
    maintenance.value = await loadMaintenance(props.organizationId, props.maintenanceId)
    emit('changed', maintenance.value.status)
  } catch (failure) {
    emit('changed', props.status)
    emit('error', failure instanceof Error ? failure.message : 'Обновите список записей.')
  }
}
</script>

<template>
  <div class="maintenance-workflow-actions" :class="{ 'maintenance-workflow-actions--compact': compact }">
    <UiButton variant="secondary" :disabled="disabled || loading" aria-haspopup="dialog" @click.stop.prevent="open">
      {{ loading ? 'Загружаем…' : 'Действия' }}
    </UiButton>
    <MaintenanceCardActions v-if="maintenance" ref="actions" :organization-id="organizationId"
      :maintenance="maintenance" :disabled="disabled" :simple-workflow="simpleWorkflow" hide-trigger @changed="changed" />
  </div>
</template>
