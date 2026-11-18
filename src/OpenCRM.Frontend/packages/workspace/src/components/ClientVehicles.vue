<script setup lang="ts">
import { computed, ref } from 'vue'
import { UiButton, UiInput, useLocalization } from '@opencrm/ui'
import '../clientMessages'
import type { WorkspaceClientVehicle } from '../api/clients'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'

const props = defineProps<{
  vehicles: WorkspaceClientVehicle[]
  clientName: string
  title: string
  canOpen: boolean
  canCreate: boolean
  canAdd: boolean
  createLabel: string
}>()
const { t } = useLocalization()
const emit = defineEmits<{ add: [] }>()
const isOpen = ref(false)
const isChoosing = ref(false)
const search = ref('')
const failedPhotos = ref(new Set<string>())
const visibleVehicles = computed(() => props.vehicles.slice(0, 4))
const filteredVehicles = computed(() => {
  const query = search.value.toLocaleLowerCase('ru').replace(/\s/g, '')
  return props.vehicles.filter(vehicle => !query || `${vehicle.displayName} ${vehicle.registrationPlate || ''}`.toLocaleLowerCase('ru').replace(/\s/g, '').includes(query))
})

function open(choose = false) {
  search.value = ''
  isChoosing.value = choose
  isOpen.value = true
}

function hasPhoto(vehicle: WorkspaceClientVehicle) {
  return !!vehicle.photoUrl && !failedPhotos.value.has(vehicle.photoUrl)
}

defineExpose({ open })
</script>

<template>
  <section class="client-vehicles" :aria-label="title">
    <header class="client-vehicles__header ui-section-header">
      <h3>{{ title }} <span>{{ vehicles.length }}</span></h3>
      <UiButton v-if="canAdd" variant="secondary" @click="emit('add')">{{ t('clients.add') }}</UiButton>
    </header>
    <p v-if="!vehicles.length" class="client-vehicles__empty">{{ t('clients.vehiclesEmpty') }}</p>
    <ul v-else class="client-vehicles__list">
      <li v-for="vehicle in visibleVehicles" :key="vehicle.id" class="client-vehicles__row">
        <component :is="canOpen ? 'a' : 'div'" :href="canOpen ? `#vehicles/${vehicle.id}` : undefined" class="client-vehicles__photo" :aria-label="canOpen ? t('clients.openVehicle', { name: vehicle.displayName }) : undefined">
          <img v-if="hasPhoto(vehicle)" :src="vehicle.photoUrl!" :alt="vehicle.displayName" loading="lazy" @error="failedPhotos.add(vehicle.photoUrl!)" />
          <div v-else class="client-vehicles__no-photo">
            <img v-if="getVehicleBrandLogo(vehicle.brandLogoKey)" :src="getVehicleBrandLogo(vehicle.brandLogoKey)!.src" alt="" />
            <span v-else>{{ t('clients.noPhoto') }}</span>
          </div>
        </component>
        <div class="client-vehicles__identity">
          <div><strong>{{ vehicle.displayName }}</strong><span v-if="vehicle.registrationPlate" class="client-vehicles__plate">{{ vehicle.registrationPlate }}</span></div>
        </div>
        <div class="client-vehicles__actions">
          <UiButton v-if="canOpen" variant="secondary" :href="`#vehicles/${vehicle.id}`">{{ t('clients.open') }}</UiButton>
          <UiButton v-if="canCreate" class="client-vehicles__create" variant="secondary" :href="`#maintenance/new/${vehicle.id}`">{{ createLabel }} →</UiButton>
        </div>
      </li>
    </ul>
    <UiButton v-if="vehicles.length > 4" class="client-vehicles__all" variant="secondary" @click="open()">{{ t('clients.allItems') }} · {{ vehicles.length }} →</UiButton>
  </section>

  <SchedulerBookingDialog :open="isOpen" :title="isChoosing ? t('clients.chooseVehicle') : title" @close="isOpen = false">
    <div class="client-vehicles-modal">
      <div class="client-vehicles-modal__tools">
        <p>{{ clientName }} · {{ vehicles.length }}</p>
        <div><UiInput v-model="search" :label="t('clients.searchVehicle')" :placeholder="t('clients.vehicleSearchPlaceholder')" type="search" /><UiButton v-if="canAdd" variant="secondary" @click="isOpen = false; emit('add')">{{ t('clients.add') }}</UiButton></div>
      </div>
      <div class="client-vehicles-modal__list" :aria-label="t('clients.allClientVehicles')" tabindex="0">
        <p v-if="!filteredVehicles.length" class="client-vehicles__empty" role="status">{{ t('clients.vehicleNotFound') }}</p>
        <article v-for="vehicle in filteredVehicles" :key="vehicle.id" class="client-vehicles-modal__row">
          <div class="client-vehicles__identity">
            <img v-if="hasPhoto(vehicle)" class="client-vehicles__thumbnail" :src="vehicle.photoUrl!" :alt="vehicle.displayName" loading="lazy" @error="failedPhotos.add(vehicle.photoUrl!)" />
            <img v-else-if="getVehicleBrandLogo(vehicle.brandLogoKey)" :src="getVehicleBrandLogo(vehicle.brandLogoKey)!.src" :alt="getVehicleBrandLogo(vehicle.brandLogoKey)!.name" />
            <div><strong>{{ vehicle.displayName }}</strong><span v-if="vehicle.registrationPlate" class="client-vehicles__plate">{{ vehicle.registrationPlate }}</span></div>
          </div>
          <div class="client-vehicles__actions">
            <UiButton v-if="canOpen" variant="secondary" :href="`#vehicles/${vehicle.id}`" @click="isOpen = false">{{ t('clients.open') }}</UiButton>
            <UiButton v-if="canCreate" class="client-vehicles__create" variant="secondary" :href="`#maintenance/new/${vehicle.id}`" @click="isOpen = false">{{ createLabel }}</UiButton>
          </div>
        </article>
      </div>
      <footer class="client-vehicles-modal__footer"><span aria-live="polite">{{ t('clients.foundCount', { found: filteredVehicles.length, total: vehicles.length }) }}</span><UiButton variant="secondary" @click="isOpen = false">{{ t('clients.close') }}</UiButton></footer>
    </div>
  </SchedulerBookingDialog>
</template>

<style scoped>
.client-vehicles { display: block; min-width: 0; margin-top: 0; container-type: inline-size; }
.client-vehicles__header { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px; margin-bottom: 14px; }
.client-vehicles__header h3 { margin: 0; font-size: 16px; }
.client-vehicles__header h3 span { margin-left: 6px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.client-vehicles__list { margin: 0; padding: 0; list-style: none; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; background: var(--crm-surface, #fff); }
.client-vehicles__row { display: grid; grid-template-columns: 56px minmax(0, 1fr) auto; align-items: center; gap: 12px; padding: 12px; border-bottom: 1px solid var(--crm-line, #dce2e9); }
.client-vehicles__row:last-child { border-bottom: 0; }
.client-vehicles__row > .client-vehicles__photo { height: 42px; }
.client-vehicles__row .client-vehicles__identity > div { display: flex; align-items: center; flex-wrap: wrap; gap: 6px 12px; }
.client-vehicles__row .client-vehicles__no-photo img { width: 32px; height: 32px; padding: 3px; }
.client-vehicles__row .client-vehicles__no-photo { font-size: 10px; }
.client-vehicles__row .client-vehicles__actions { justify-content: flex-end; }
.client-vehicles__identity { display: flex; align-items: center; gap: 12px; min-width: 0; }
.client-vehicles__identity img { width: 36px; height: 36px; flex: 0 0 36px; padding: 3px; border-radius: 50%; background: white; object-fit: contain; }
.client-vehicles__identity > div { display: grid; justify-items: start; gap: 7px; min-width: 0; }
.client-vehicles__identity strong { font-size: 14px; overflow-wrap: anywhere; }
.client-vehicles__plate { padding: 2px 6px; background: var(--crm-surface-soft, #f1f3f6); border-radius: 4px; font-size: 12px; overflow-wrap: anywhere; }
.client-vehicles__photo { display: block; height: 130px; overflow: hidden; border-radius: 6px; background: var(--crm-surface-soft, #f1f3f6); color: var(--crm-text-muted, #667085); text-decoration: none; }
.client-vehicles__photo > img { width: 100%; height: 100%; object-fit: cover; }
.client-vehicles__no-photo { display: grid; height: 100%; align-content: center; justify-items: center; gap: 10px; font-size: 12px; }
.client-vehicles__no-photo img { width: 52px; height: 52px; padding: 5px; object-fit: contain; border-radius: 50%; background: white; }
.client-vehicles__identity img.client-vehicles__thumbnail { width: 76px; height: 50px; flex-basis: 76px; padding: 0; border-radius: 5px; object-fit: cover; }
.client-vehicles__actions { display: flex; flex-wrap: wrap; gap: 8px; }
.client-vehicles :deep(.ui-button), .client-vehicles-modal :deep(.ui-button) { min-height: 32px; padding: 6px 10px; font-size: 12px; }
.client-vehicles__all { width: 100%; margin-top: 12px; }
.client-vehicles :deep(.client-vehicles__create), .client-vehicles-modal :deep(.client-vehicles__create) { color: #e87532; border-color: #e87532; }
.client-vehicles__empty { padding: 24px 12px; text-align: center; color: var(--crm-text-muted, #667085); }
:global(.scheduler-booking-dialog:has(.client-vehicles-modal)) { display: flex; flex-direction: column; height: min(780px, 92dvh); }
:global(.scheduler-booking-dialog:has(.client-vehicles-modal) > .scheduler-booking-dialog__header) { flex-shrink: 0; }
:global(.scheduler-booking-dialog:has(.client-vehicles-modal) > .scheduler-booking-dialog__content) { flex: 1; min-height: 0; max-height: none; overflow: hidden; }
.client-vehicles-modal { display: flex; flex-direction: column; min-height: 0; height: 100%; color: var(--crm-text, #202833); }
.client-vehicles-modal__tools { flex-shrink: 0; padding: 16px 24px; border-bottom: 1px solid var(--crm-line, #dce2e9); }
.client-vehicles-modal__tools p { margin: 0 0 12px; color: var(--crm-text-muted, #667085); font-size: 13px; }
.client-vehicles-modal__tools > div { display: flex; align-items: end; gap: 12px; }
.client-vehicles-modal__tools :deep(.ui-field) { flex: 1; min-width: 0; }
.client-vehicles-modal__list { flex: 1; min-height: 0; overflow-y: auto; overscroll-behavior: contain; padding: 0 24px; }
.client-vehicles-modal__row { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: 16px; padding: 16px 0; border-bottom: 1px solid var(--crm-line, #dce2e9); }
.client-vehicles-modal__footer { flex-shrink: 0; display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 24px; border-top: 1px solid var(--crm-line, #dce2e9); font-size: 12px; color: var(--crm-text-muted, #667085); }
.client-vehicles-modal a:focus-visible, .client-vehicles-modal button:focus-visible, .client-vehicles-modal__list:focus-visible { outline: 2px solid #e87532; outline-offset: 2px; }
@container (max-width: 600px) {
  .client-vehicles__row { grid-template-columns: 48px minmax(0, 1fr); gap: 8px 10px; }
  .client-vehicles__row > .client-vehicles__actions { grid-column: 2; justify-content: flex-start; }
}
@media (max-width: 900px) {
  .client-vehicles-modal__row { grid-template-columns: minmax(0, 1fr) auto; }
  .client-vehicles-modal__row .client-vehicles__actions { grid-column: 2; flex-direction: column; }
}
@media (max-width: 480px) {
  :global(.scheduler-booking-dialog:has(.client-vehicles-modal)) { height: 100dvh; }
  .client-vehicles-modal__tools, .client-vehicles-modal__footer { padding: 12px 16px; }
  .client-vehicles-modal__tools > div { flex-wrap: wrap; }
  .client-vehicles-modal__tools :deep(.ui-field) { flex-basis: 100%; }
  .client-vehicles-modal__list { padding-inline: 16px; }
  .client-vehicles-modal__row { gap: 10px; }
  .client-vehicles__identity { gap: 8px; }
}
</style>
