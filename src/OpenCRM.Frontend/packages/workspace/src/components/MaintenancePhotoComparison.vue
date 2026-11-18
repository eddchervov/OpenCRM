<script setup lang="ts">
import { computed, nextTick, onMounted, ref, shallowRef, watch } from 'vue'
import { UiButton, UiImagePreview, UiLoader } from '@opencrm/ui'
import { useLocalization } from '@opencrm/ui'
import '../maintenanceMediaMessages'
import { loadMaintenancePhotoContent, type WorkspaceMaintenancePhoto } from '../api/maintenance'

const props = defineProps<{
  photos: WorkspaceMaintenancePhoto[]
  organizationId: string
  organizationName: string
  maintenanceId: string
}>()
const { t } = useLocalization()
const emit = defineEmits<{ close: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const stage = ref<HTMLElement | null>(null)
const mode = ref<'side' | 'slider'>('side')
const position = ref(50)
const beforePhotos = computed(() => props.photos.filter(photo => photo.stage === 1))
const afterPhotos = computed(() => props.photos.filter(photo => photo.stage === 2))
const beforeId = ref(beforePhotos.value[0]?.photoId ?? '')
const afterId = ref(afterPhotos.value[0]?.photoId ?? '')
const images = shallowRef<HTMLImageElement[]>([])
const isLoading = ref(false)
const isDownloading = ref(false)
const errorMessage = ref('')
const preview = ref<{ src: string; title: string } | null>(null)
const retry = ref(0)
const choices = computed(() => [
  { title: t('photoComparison.before'), photos: beforePhotos.value, selectedId: beforeId.value },
  { title: t('photoComparison.after'), photos: afterPhotos.value, selectedId: afterId.value }
])

onMounted(async () => {
  await nextTick()
  dialog.value?.showModal()
})

watch([beforeId, afterId, retry], async (_, __, onCleanup) => {
  const controller = new AbortController()
  const urls: string[] = []
  onCleanup(() => {
    controller.abort()
    urls.forEach(url => URL.revokeObjectURL(url))
  })
  images.value = []
  preview.value = null
  errorMessage.value = ''
  if (!beforeId.value || !afterId.value) return
  isLoading.value = true
  try {
    const loaded = await Promise.all([beforeId.value, afterId.value].map(async id => {
      const blob = await loadMaintenancePhotoContent(props.organizationId, props.maintenanceId, id, controller.signal)
      if (controller.signal.aborted) throw new DOMException(t('photoComparison.cancelledLoad'), 'AbortError')
      const url = URL.createObjectURL(blob)
      urls.push(url)
      const image = new Image()
      image.src = url
      await image.decode().catch(() => { throw new Error(t('photoComparison.readPhotoError')) })
      return image
    }))
    if (!controller.signal.aborted) images.value = loaded
  } catch (error) {
    if (!controller.signal.aborted) {
      errorMessage.value = error instanceof Error ? error.message : t('photoComparison.openError')
    }
  } finally {
    if (!controller.signal.aborted) isLoading.value = false
  }
}, { immediate: true })

function choosePhoto(side: number, id: string) {
  if (side === 0) beforeId.value = id
  else afterId.value = id
}

function moveDivider(event: PointerEvent) {
  if (mode.value !== 'slider' || !stage.value || !images.value.length) return
  const bounds = stage.value.getBoundingClientRect()
  position.value = Math.round(Math.max(0, Math.min(100, (event.clientX - bounds.left) / bounds.width * 100)))
}

function startDrag(event: PointerEvent) {
  if (mode.value !== 'slider' || event.button !== 0) return
  stage.value?.setPointerCapture(event.pointerId)
  moveDivider(event)
}

function drag(event: PointerEvent) {
  if (stage.value?.hasPointerCapture(event.pointerId)) moveDivider(event)
}

function drawContained(context: CanvasRenderingContext2D, image: HTMLImageElement, x: number, y: number, width: number, height: number) {
  const scale = Math.min(width / image.naturalWidth, height / image.naturalHeight)
  const imageWidth = image.naturalWidth * scale
  const imageHeight = image.naturalHeight * scale
  context.drawImage(image, x + (width - imageWidth) / 2, y + (height - imageHeight) / 2, imageWidth, imageHeight)
}

async function download() {
  if (images.value.length !== 2 || isDownloading.value) return
  isDownloading.value = true
  errorMessage.value = ''
  try {
    const canvas = document.createElement('canvas')
    const margin = 32
    const top = 144
    const gap = 24
    canvas.width = 2000
    const width = canvas.width - margin * 2
    const panelWidth = mode.value === 'side' ? (width - gap) / 2 : width
    const height = Math.round(panelWidth * 3 / 4)
    canvas.height = top + height + margin
    const context = canvas.getContext('2d')
    if (!context) throw new Error(t('photoComparison.noCanvas'))
    context.fillStyle = '#18202b'
    context.fillRect(0, 0, canvas.width, canvas.height)
    context.fillStyle = '#ffffff'
    context.font = '600 38px sans-serif'
    let title = props.organizationName
    while (title.length && context.measureText(title).width > width) title = title.slice(0, -1)
    if (title !== props.organizationName) title = title.slice(0, -1) + '…'
    context.fillText(title, margin, 62)
    context.font = '600 30px sans-serif'
    const beforeLabel = t('photoComparison.before').toLocaleUpperCase()
    const afterLabel = t('photoComparison.after').toLocaleUpperCase()
    context.fillText(beforeLabel, margin, 118)
    const afterX = mode.value === 'side' ? margin + panelWidth + gap : canvas.width - margin - context.measureText(afterLabel).width
    context.fillText(afterLabel, afterX, 118)
    context.fillStyle = '#242d39'
    context.fillRect(margin, top, width, height)
    if (mode.value === 'side') {
      drawContained(context, images.value[0], margin, top, panelWidth, height)
      drawContained(context, images.value[1], afterX, top, panelWidth, height)
    } else {
      drawContained(context, images.value[1], margin, top, width, height)
      const boundary = margin + width * position.value / 100
      context.save()
      context.beginPath()
      context.rect(margin, top, boundary - margin, height)
      context.clip()
      context.fillStyle = '#242d39'
      context.fillRect(margin, top, width, height)
      drawContained(context, images.value[0], margin, top, width, height)
      context.restore()
      if (position.value > 0 && position.value < 100) {
        context.fillStyle = '#ffffff'
        context.fillRect(boundary - 2, top, 4, height)
      }
    }
    const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob(value => {
      if (value) resolve(value)
      else reject(new Error(t('photoComparison.saveError')))
    }, 'image/png'))
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${t('photoComparison.filePrefix')}-${props.maintenanceId}.png`
    link.click()
    // Браузеру нужно время, чтобы начать скачивание по временному адресу.
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('photoComparison.downloadError')
  } finally {
    isDownloading.value = false
  }
}
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="photo-comparison" aria-labelledby="comparison-title" @close="emit('close')" @click.self="dialog?.close()">
      <header class="photo-comparison__header ui-form-header">
        <div><h2 id="comparison-title">{{ t('photoComparison.title') }}</h2><p>{{ organizationName }}</p></div>
        <button class="photo-comparison__close" type="button" :aria-label="t('photoComparison.close')" autofocus @click="dialog?.close()">×</button>
      </header>
      <div class="photo-comparison__body">
        <div class="photo-comparison__toolbar">
          <div class="photo-comparison__modes" role="group" :aria-label="t('photoComparison.mode')">
            <button type="button" :aria-pressed="mode === 'side'" @click="mode = 'side'">{{ t('photoComparison.sideBySide') }}</button>
            <button type="button" :aria-pressed="mode === 'slider'" @click="mode = 'slider'">{{ t('photoComparison.slider') }}</button>
          </div>
          <UiButton :disabled="images.length !== 2 || isDownloading" @click="download">{{ isDownloading ? t('photoComparison.saving') : t('photoComparison.download') }}</UiButton>
        </div>
        <div class="photo-comparison__choices">
          <section v-for="(choice, side) in choices" :key="side" :aria-label="t('photoComparison.photos', { side: choice.title.toLocaleLowerCase() })">
            <div class="photo-comparison__choice-heading">
              <h3 class="ui-section-header">{{ choice.title }} <span>{{ choice.photos.length }}</span></h3>
              <button v-if="images[side]" type="button" @click="preview = { src: images[side].src, title: choice.title }">{{ t('photoComparison.enlarge', { side: choice.title.toLocaleLowerCase() }) }}</button>
            </div>
            <div class="photo-comparison__thumbnails">
              <button v-for="(photo, index) in choice.photos" :key="photo.photoId" type="button" :aria-pressed="choice.selectedId === photo.photoId"
                :aria-label="t('photoComparison.photo', { side: choice.title, index: index + 1 })" @click="choosePhoto(side, photo.photoId)">
                <img :src="photo.url" alt="" loading="lazy">
                <span>{{ index + 1 }}</span>
              </button>
            </div>
          </section>
        </div>
        <div v-if="errorMessage" class="photo-comparison__error" role="alert">
          {{ errorMessage }} <button v-if="images.length !== 2" type="button" @click="retry++">{{ t('photoComparison.retry') }}</button>
        </div>
        <UiLoader v-if="isLoading" :text="t('photoComparison.loading')" />
        <div v-else-if="images.length === 2" ref="stage" class="photo-comparison__stage" :class="`photo-comparison__stage--${mode}`"
          @pointerdown="startDrag" @pointermove="drag" @pointerup="stage?.hasPointerCapture($event.pointerId) && stage.releasePointerCapture($event.pointerId)">
          <figure class="photo-comparison__image photo-comparison__image--after">
            <img :src="images[1].src" :alt="t('photoComparison.afterWork')" draggable="false"><figcaption>{{ t('photoComparison.after') }}</figcaption>
          </figure>
          <figure class="photo-comparison__image photo-comparison__image--before" :style="mode === 'slider' ? { clipPath: `inset(0 ${100 - position}% 0 0)` } : undefined">
            <img :src="images[0].src" :alt="t('photoComparison.beforeWork')" draggable="false"><figcaption>{{ t('photoComparison.before') }}</figcaption>
          </figure>
          <div v-if="mode === 'slider'" class="photo-comparison__divider" :style="{ left: `${position}%` }" aria-hidden="true"><span>↔</span></div>
        </div>
        <label v-if="mode === 'slider' && images.length === 2" class="photo-comparison__range">
          <span>{{ t('photoComparison.before') }}</span><input v-model.number="position" type="range" min="0" max="100" :aria-label="t('photoComparison.dividerPosition')" :aria-valuetext="t('photoComparison.dividerValue', { before: position, after: 100 - position })"><span>{{ t('photoComparison.after') }}</span>
        </label>
      </div>
    </dialog>
  </Teleport>
  <UiImagePreview v-if="preview" :src="preview.src" :title="preview.title" @close="preview = null" />
</template>

<style scoped>
.photo-comparison {
  width: min(1180px, calc(100vw - 24px));
  max-width: none;
  max-height: calc(100dvh - 24px);
  margin: auto;
  padding: 0;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 12px;
  color: var(--crm-text, #202833);
  background: var(--crm-surface, #fff);
}

.photo-comparison::backdrop {
  background: rgba(0, 0, 0, .75);
}

.photo-comparison__header {
  position: sticky;
  top: 0;
  z-index: 2;
  background: var(--crm-surface, #fff);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 20px;
  background: var(--crm-form-header-bg, #fff7f0);
  border-bottom: 1px solid var(--crm-form-header-border, #dce2e9);
}

.photo-comparison__header h2 {
  margin: 0;
  font-size: 18px;
}

.photo-comparison__header p {
  margin: 4px 0 0;
  color: var(--crm-text-muted, #667085);
  font-size: 13px;
  overflow-wrap: anywhere;
}

.photo-comparison__close {
  flex: 0 0 36px;
  height: 36px;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 6px;
  color: inherit;
  background: var(--crm-surface-soft, #f4f6f8);
  font-size: 24px;
  cursor: pointer;
}

.photo-comparison__body {
  padding: 16px 20px 20px;
  display: grid;
  gap: 16px;
}

.photo-comparison__toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.photo-comparison__modes {
  display: flex;
  padding: 3px;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 7px;
  background: var(--crm-surface-soft, #f4f6f8);
}

.photo-comparison__modes button {
  padding: 8px 14px;
  border: 0;
  border-radius: 4px;
  background: transparent;
  color: var(--crm-text-muted, #667085);
  font: inherit;
  font-size: 13px;
  font-weight: 700;
  cursor: pointer;
}

.photo-comparison__modes button[aria-pressed="true"] {
  background: var(--crm-text, #202833);
  color: var(--crm-surface, #fff);
}

.photo-comparison__choices {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.photo-comparison__choices section {
  min-width: 0;
}

.photo-comparison__choice-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}

.photo-comparison__choice-heading h3 {
  margin: 0;
  font-size: 14px;
}

.photo-comparison__choice-heading h3 span {
  margin-left: 6px;
  font-size: 12px;
  font-weight: 400;
  color: var(--crm-text-muted, #667085);
}

.photo-comparison__choice-heading button, .photo-comparison__error button {
  border: 0;
  padding: 4px 0;
  background: transparent;
  color: inherit;
  text-decoration: underline;
  font: inherit;
  font-size: 12px;
  cursor: pointer;
}

.photo-comparison__thumbnails {
  display: flex;
  gap: 8px;
  overflow-x: auto;
  padding: 3px;
}

.photo-comparison__thumbnails button {
  position: relative;
  flex: 0 0 72px;
  height: 54px;
  padding: 0;
  border: 2px solid transparent;
  border-radius: 6px;
  overflow: hidden;
  background: var(--crm-surface-soft, #f4f6f8);
  cursor: pointer;
}

.photo-comparison__thumbnails button[aria-pressed="true"] {
  border-color: #e87532;
}

.photo-comparison__thumbnails img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.photo-comparison__thumbnails span {
  position: absolute;
  right: 2px;
  bottom: 2px;
  padding: 1px 4px;
  border-radius: 3px;
  background: #18202b;
  color: #fff;
  font-size: 10px;
}

.photo-comparison__stage {
  position: relative;
  overflow: hidden;
  border-radius: 8px;
  background: #242d39;
}

.photo-comparison__stage--side {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.photo-comparison__stage--slider {
  width: min(100%, max(280px, calc((100dvh - 340px) * 4 / 3)));
  margin-inline: auto;
  aspect-ratio: 4 / 3;
  touch-action: pan-y;
  cursor: ew-resize;
}

.photo-comparison__image {
  position: relative;
  margin: 0;
  min-width: 0;
  background: #242d39;
}

.photo-comparison__stage--side .photo-comparison__image {
  aspect-ratio: 4 / 3;
}

.photo-comparison__stage--side .photo-comparison__image--before {
  order: -1;
}

.photo-comparison__stage--slider .photo-comparison__image {
  position: absolute;
  inset: 0;
}

.photo-comparison__image img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: contain;
  user-select: none;
}

.photo-comparison__image figcaption {
  position: absolute;
  top: 12px;
  left: 12px;
  padding: 5px 10px;
  border-radius: 5px;
  background: rgba(24, 32, 43, .85);
  color: white;
  font-size: 13px;
  font-weight: 700;
}

.photo-comparison__image--after figcaption {
  left: auto;
  right: 12px;
}

.photo-comparison__divider {
  position: absolute;
  inset-block: 0;
  width: 2px;
  background: #fff;
  pointer-events: none;
}

.photo-comparison__divider span {
  position: absolute;
  top: 50%;
  left: 50%;
  display: grid;
  place-items: center;
  width: 42px;
  height: 42px;
  transform: translate(-50%, -50%);
  border-radius: 50%;
  background: #fff;
  color: #18202b;
  box-shadow: 0 2px 10px #0005;
  font-size: 24px;
}

.photo-comparison__range {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 13px;
}

.photo-comparison__range input {
  flex: 1;
  min-width: 0;
  min-height: 32px;
  accent-color: #e87532;
}

.photo-comparison__error {
  padding: 12px;
  border: 1px solid var(--crm-danger-border, #f3b9b3);
  border-radius: 6px;
  color: var(--crm-danger-text, #a12719);
  background: var(--crm-danger-surface, #fff4f2);
}

.photo-comparison button:focus-visible, .photo-comparison input:focus-visible {
  outline: 2px solid #e87532;
  outline-offset: 2px;
}

@media (max-width: 600px) {
  .photo-comparison__body, .photo-comparison__header {
    padding: 12px;
  }

  .photo-comparison__stage--side {
    grid-template-columns: minmax(0, 1fr);
  }

  .photo-comparison__choices {
    gap: 10px;
  }

}
</style>
