<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { UiButton, UiLoader } from '@opencrm/ui'
import { loadMaintenance, loadMaintenancePhotos, type WorkspaceMaintenance, type WorkspaceMaintenancePhoto } from '../api/maintenance'
import type { WorkspaceWorkdayItem } from '../api/workday'
import type { WorkspaceContext } from '../types'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'

const props = defineProps<{ context: WorkspaceContext, item: WorkspaceWorkdayItem }>()
const emit = defineEmits<{ close: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const maintenance = ref<WorkspaceMaintenance | null>(null)
const photos = ref<WorkspaceMaintenancePhoto[]>([])
const activePanel = ref<'work' | 'photos' | 'finance'>('work')
const isLoading = ref(false)
const error = ref('')
const photoError = ref('')
const fullHref = computed(() => `#maintenance/${props.item.id}`)
const brandLogo = computed(() => getVehicleBrandLogo(props.item.vehicleBrandLogoKey))
const services = computed(() => maintenance.value?.maintenanceDetails.filter(detail => !(
  detail.materialCatalogItemId || detail.materialName || detail.materialQuantity != null
)) ?? [])
const totalAmount = computed(() => Math.max(0, (maintenance.value?.grossAmount ?? maintenance.value?.price ?? 0) - (maintenance.value?.discountAmount ?? 0)))
const balanceLabel = computed(() => maintenance.value?.paymentStatus === 'overpaid' ? 'Переплата' : 'Остаток')
const balanceAmount = computed(() => maintenance.value?.paymentStatus === 'overpaid'
  ? Math.max(0, maintenance.value.paidAmount - totalAmount.value)
  : maintenance.value?.debtAmount ?? 0)
const completedCount = computed(() => Math.min(props.item.workTotalCount, Math.max(0, props.item.workCompletedCount)))
const status = computed(() => props.item.handedOverAt ? 'Выдан'
  : props.item.readyForHandoverAt ? 'Готов к выдаче'
    : maintenance.value?.status === 2 ? 'В работе'
      : maintenance.value?.status === 3 ? 'Выполнено'
        : maintenance.value?.status === 4 ? 'Отменено' : 'Запланировано')
let isDisposed = false
let previousOverflow = ''

function formatMoney(amount: number) {
  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits: 2 }).format(amount)
}

async function load() {
  isLoading.value = true
  error.value = ''
  try {
    const result = await loadMaintenance(props.context.organization.id, props.item.id)
    if (isDisposed) return
    maintenance.value = result
  } catch (cause) {
    if (!isDisposed) error.value = cause instanceof Error ? cause.message : 'Не удалось загрузить запись.'
  } finally {
    if (!isDisposed) isLoading.value = false
  }
}

async function loadPhotos() {
  try {
    const result = await loadMaintenancePhotos(props.context.organization.id, props.item.id)
    if (!isDisposed) photos.value = result
  } catch {
    if (!isDisposed) photoError.value = 'Не удалось загрузить фото записи.'
  }
}

function closeFromBackdrop(event: MouseEvent) {
  if (event.target !== dialog.value || !dialog.value) return
  const bounds = dialog.value.getBoundingClientRect()
  if (event.clientX >= bounds.left && event.clientX <= bounds.right
    && event.clientY >= bounds.top && event.clientY <= bounds.bottom) return
  emit('close')
}

onMounted(() => {
  previousOverflow = document.body.style.overflow
  document.body.style.overflow = 'hidden'
  dialog.value?.showModal()
  void load()
  void loadPhotos()
})

onBeforeUnmount(() => {
  isDisposed = true
  dialog.value?.close()
  document.body.style.overflow = previousOverflow
})
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="workday-preview" aria-labelledby="workday-preview-title" @cancel.prevent="emit('close')" @click="closeFromBackdrop">
      <header class="workday-preview__header ui-form-header">
        <h2 id="workday-preview-title">Просмотр записи</h2>
        <button type="button" class="workday-preview__close" aria-label="Закрыть просмотр записи" autofocus @click="emit('close')">×</button>
      </header>
      <div class="workday-preview__body">
        <div class="workday-preview__summary">
          <div class="ui-section-header"><h3><img v-if="brandLogo" :src="brandLogo.src" :alt="`Логотип ${brandLogo.name}`" class="workday-preview__brand">{{ item.vehicleName }}</h3><span v-if="item.registrationPlate" class="workday-preview__plate">{{ item.registrationPlate }}</span></div>
          <span class="workday-preview__status">{{ status }}</span>
        </div>
        <dl class="workday-preview__details">
          <div v-if="item.clientName"><dt>Клиент</dt><dd>{{ item.clientName }}</dd></div>
          <div><dt>Дата</dt><dd>{{ item.date.slice(0, 10).split('-').reverse().join('.') }}</dd></div>
          <div v-if="item.startTime"><dt>Время</dt><dd>{{ item.startTime.slice(0, 5) }}<template v-if="item.endTime"> — <template v-if="item.endDate && item.endDate.slice(0, 10) !== item.date.slice(0, 10)">{{ item.endDate.slice(0, 10).split('-').reverse().join('.') }} </template>{{ item.endTime.slice(0, 5) }}</template></dd></div>
          <div v-if="item.location"><dt>Пост / место</dt><dd>{{ item.location }}</dd></div>
          <div v-if="item.performer"><dt>Сотрудник</dt><dd>{{ item.performer }}</dd></div>
        </dl>
        <nav class="workday-preview__tabs" aria-label="Разделы записи">
          <button type="button" :aria-pressed="activePanel === 'work'" @click="activePanel = 'work'">Работы</button>
          <button type="button" :aria-pressed="activePanel === 'photos'" @click="activePanel = 'photos'">Приёмка и фото</button>
          <button type="button" :aria-pressed="activePanel === 'finance'" @click="activePanel = 'finance'">Расчёт</button>
        </nav>
        <UiLoader v-if="isLoading" text="Загружаем запись…" />
        <div v-else-if="error" class="workday-preview__error" role="alert"><p>{{ error }}</p><UiButton variant="secondary" @click="load">Повторить</UiButton></div>
        <template v-else-if="maintenance">
          <div v-if="activePanel === 'work'" class="workday-preview__work">
            <div>
              <ul class="workday-preview__services">
                <li v-for="(service, index) in services" :key="service.detailId ?? index">
                  <div><strong>{{ service.name }}</strong><span v-if="service.quantity != null">Количество: {{ service.quantity }}</span></div>
                  <span v-if="service.price != null">{{ formatMoney(service.price) }}</span>
                </li>
              </ul>
              <p v-if="!services.length">Услуги не указаны.</p>
              <div v-if="item.workTotalCount > 0" class="workday-preview__progress">
                <progress :value="completedCount" :max="item.workTotalCount" :aria-label="`Выполнено ${completedCount} из ${item.workTotalCount}`" />
                <span>Выполнено {{ completedCount }} из {{ item.workTotalCount }}</span>
                <span v-if="item.workPausedCount">На паузе: {{ item.workPausedCount }}</span>
              </div>
              <p v-if="maintenance.comment" class="workday-preview__comment">{{ maintenance.comment }}</p>
            </div>
            <dl class="workday-preview__money">
              <div><dt>Стоимость</dt><dd>{{ formatMoney(totalAmount) }}</dd></div>
              <div><dt>Оплачено</dt><dd>{{ formatMoney(maintenance.paidAmount) }}</dd></div>
              <div><dt>{{ balanceLabel }}</dt><dd>{{ formatMoney(balanceAmount) }}</dd></div>
            </dl>
          </div>
          <div v-else-if="activePanel === 'photos'">
            <p v-if="maintenance.intakeNotes">{{ maintenance.intakeNotes }}</p>
            <p v-if="photoError" role="alert">{{ photoError }}</p>
            <div v-else-if="photos.length" class="workday-preview__photos">
              <figure v-for="photo in photos" :key="photo.photoId"><img :src="photo.url" :alt="photo.stage === 1 ? 'До работы' : photo.stage === 2 ? 'После работы' : 'Фото записи'" loading="lazy"><figcaption>{{ photo.stage === 1 ? 'До работы' : photo.stage === 2 ? 'После работы' : 'Фото записи' }}</figcaption></figure>
            </div>
            <p v-else>Фотографии не добавлены.</p>
          </div>
          <dl v-else class="workday-preview__money">
            <div><dt>Стоимость работ</dt><dd>{{ formatMoney(maintenance.grossAmount) }}</dd></div>
            <div><dt>Скидка</dt><dd>{{ formatMoney(maintenance.discountAmount) }}</dd></div>
            <div><dt>Оплачено</dt><dd>{{ formatMoney(maintenance.paidAmount) }}</dd></div>
            <div><dt>{{ balanceLabel }}</dt><dd>{{ formatMoney(balanceAmount) }}</dd></div>
          </dl>
        </template>
      </div>
      <footer class="workday-preview__footer"><UiButton variant="secondary" @click="emit('close')">Закрыть</UiButton><UiButton :href="fullHref">Открыть полную карточку ↗</UiButton></footer>
    </dialog>
  </Teleport>
</template>

<style scoped>
.workday-preview { width: min(900px, calc(100vw - 40px)); max-height: calc(100dvh - 40px); padding: 0; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 12px; background: var(--crm-surface, #fff); color: var(--crm-text, #202733); box-shadow: 0 16px 64px var(--crm-shadow, #10182833); }
.workday-preview::backdrop { background: var(--crm-backdrop, #10182866); }
.workday-preview__header, .workday-preview__footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 14px 20px; }
.workday-preview__header { border-bottom: 1px solid var(--crm-form-header-border, #e3e6eb); }
.workday-preview__header h2 { margin: 0; font-size: 17px; }
.workday-preview__close { width: 32px; height: 32px; border: 0; border-radius: 6px; background: var(--crm-surface-soft, #f5f6f8); color: inherit; font-size: 24px; cursor: pointer; }
.workday-preview__body { padding: 20px; overflow-wrap: anywhere; }
.workday-preview__summary { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.workday-preview__summary h3 { margin: 0 0 8px; font-size: 20px; }
.workday-preview__brand { width: 28px; height: 28px; margin-right: 8px; padding: 3px; vertical-align: middle; object-fit: contain; border-radius: 5px; background: #fff; }
.workday-preview__plate, .workday-preview__status { padding: 4px 8px; border-radius: 5px; background: var(--crm-surface-soft, #f5f6f8); font-size: 12px; }
.workday-preview__details { display: flex; flex-wrap: wrap; gap: 12px 24px; margin: 20px 0; font-size: 13px; }
.workday-preview dt { color: var(--crm-text-muted, #667085); }
.workday-preview dd { margin: 4px 0 0; }
.workday-preview__tabs { display: flex; flex-wrap: wrap; gap: 12px; margin: 0 0 18px; border-bottom: 1px solid var(--crm-line, #e3e6eb); }
.workday-preview__tabs button { padding: 10px 0; border: 0; border-bottom: 2px solid transparent; background: none; color: var(--crm-text-muted, #667085); font: inherit; font-size: 13px; cursor: pointer; }
.workday-preview__tabs button[aria-pressed="true"] { border-bottom-color: var(--opencrm-accent, #e67337); color: var(--crm-text, #202733); }
.workday-preview__work { display: grid; grid-template-columns: minmax(0, 1fr) 240px; gap: 24px; }
.workday-preview__services { list-style: none; padding: 0; margin: 0; }
.workday-preview__services li { display: flex; justify-content: space-between; gap: 12px; padding: 12px 0; border-bottom: 1px solid var(--crm-line, #e3e6eb); font-size: 13px; }
.workday-preview__services li > span { flex-shrink: 0; }
.workday-preview__services strong { font-weight: 600; }
.workday-preview__services div > span { display: block; margin-top: 4px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.workday-preview__money { align-self: start; display: grid; gap: 12px; margin: 0; padding: 16px; border-radius: 8px; background: var(--crm-surface-soft, #f5f6f8); font-size: 13px; }
.workday-preview__money div { display: flex; justify-content: space-between; gap: 12px; }
.workday-preview__money dd { margin: 0; white-space: nowrap; }
.workday-preview__money div:last-child { padding-top: 12px; border-top: 1px solid var(--crm-line, #e3e6eb); font-weight: 700; }
.workday-preview__progress { display: grid; gap: 6px; margin-top: 16px; font-size: 12px; color: var(--crm-text-muted, #667085); }
.workday-preview__progress progress { width: 100%; height: 6px; appearance: none; border: 0; border-radius: 6px; overflow: hidden; background: var(--crm-line, #e3e6eb); }
.workday-preview__progress progress::-webkit-progress-bar { background: var(--crm-line, #e3e6eb); }
.workday-preview__progress progress::-webkit-progress-value { background: var(--opencrm-accent, #e67337); border-radius: 6px; }
.workday-preview__progress progress::-moz-progress-bar { background: var(--opencrm-accent, #e67337); border-radius: 6px; }
.workday-preview__comment { white-space: pre-wrap; font-size: 13px; }
.workday-preview__photos { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; }
.workday-preview__photos figure { margin: 0; }
.workday-preview__photos img { display: block; width: 100%; height: 180px; object-fit: contain; border-radius: 6px; background: var(--crm-surface-soft, #f5f6f8); }
.workday-preview__photos figcaption { margin-top: 6px; font-size: 12px; color: var(--crm-text-muted, #667085); }
.workday-preview__footer { justify-content: flex-end; border-top: 1px solid var(--crm-line, #e3e6eb); }
.workday-preview button:focus-visible, .workday-preview a:focus-visible { outline: 2px solid var(--opencrm-accent, #e67337); outline-offset: 3px; }
@media (max-width: 640px) {
  .workday-preview { width: 100%; max-width: 100%; height: 100dvh; max-height: 100dvh; margin: 0; border: 0; border-radius: 0; }
  .workday-preview__work { grid-template-columns: minmax(0, 1fr); gap: 16px; }
  .workday-preview__body { padding: 16px; }
  .workday-preview__footer { flex-wrap: wrap; padding: 12px 16px; }
}
</style>
