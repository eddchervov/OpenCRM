<script setup lang="ts">
import { getVehicleDisplayName } from '../api/vehicles'
import { computed, inject, onMounted, provide, ref, watch } from 'vue'
import { pageBreadcrumbsKey, UiButton, UiCard, UiConfirmDialog, UiInput, UiImagePreview, UiLoader } from '@opencrm/ui'
import { translate as t } from '@opencrm/ui/i18n'
import '../vehicleMediaMessages'
import WorkspaceProLock from './WorkspaceProLock.vue'
import {
  createVehicleAlbum,
  deleteVehicleAlbum,
  deleteVehicleAlbumPhoto,
  deleteVehiclePhoto,
  loadVehicle,
  loadVehicleAlbum,
  loadVehicleAlbums,
  loadVehiclePhotoList,
  setPrimaryVehicleAlbumPhoto,
  setPrimaryVehiclePhoto,
  uploadVehicleAlbumPhotos,
  uploadVehiclePhotos,
  type WorkspaceVehicle,
  type WorkspaceVehicleAlbum,
  type WorkspaceVehiclePhoto
} from '../api/vehicles'
import type { WorkspaceContext } from '../types'

type VehicleMediaMode = 'photos' | 'albums' | 'album'

const props = withDefaults(defineProps<{
  context: WorkspaceContext
  vehicleId: string
  mode?: VehicleMediaMode
  albumId?: string
}>(), {
  mode: 'photos',
  albumId: undefined
})

const emit = defineEmits<{
  changed: []
}>()

const maxVehiclePhotos = 10
const maxBatchBytes = 100 * 1024 * 1024

const vehicle = ref<WorkspaceVehicle | null>(null)
const parentBreadcrumbs = inject(pageBreadcrumbsKey, undefined)
const photos = ref<WorkspaceVehiclePhoto[]>([])
const albums = ref<WorkspaceVehicleAlbum[]>([])
const album = ref<WorkspaceVehicleAlbum | null>(null)
const albumTitle = ref('')
const previewPhotoUrl = ref('')
const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly !== true)
const canChangePhotos = computed(() => canWrite.value && !isPhotosFeatureLocked.value)
const isPhotosFeatureLocked = computed(() => !isPhotosMode.value && props.context.shell.scenario?.modules?.photos === false)
const showSavedMediaHistory = ref(false)
const isPhotoPreview = computed(() => isPhotosFeatureLocked.value && !showSavedMediaHistory.value)
const isLoading = ref(false)
const isSaving = ref(false)
const isUploading = ref(false)
const errorMessage = ref('')
const confirmDialog = ref({
  open: false,
  title: '',
  message: '',
  confirmText: t('vehicleMedia.delete'),
  onConfirm: null as null | (() => Promise<void>)
})

const organizationId = computed(() => props.context.organization.id)
const isPhotosMode = computed(() => props.mode === 'photos')
const isAlbumsMode = computed(() => props.mode === 'albums')
const isAlbumMode = computed(() => props.mode === 'album')
const title = computed(() => {
  if (isAlbumMode.value) {
    return album.value?.title || t('vehicleMedia.album')
  }

  return isPhotosMode.value ? t('vehicleMedia.photosTitle') : t('vehicleMedia.albumsTitle')
})
provide(pageBreadcrumbsKey, computed(() => {
  const vehicleTitle = vehicle.value
    ? [getVehicleDisplayName(vehicle.value), vehicle.value.yearIssue].filter(Boolean).join(' ')
    : t('vehicleMedia.vehicle')
  const items = [
    ...(parentBreadcrumbs?.value ?? []),
    { title: vehicleTitle, href: `#vehicles/${props.vehicleId}` }
  ]

  if (isAlbumMode.value) {
    items.push({ title: t('vehicleMedia.albumsTitle'), href: `#vehicles/${props.vehicleId}/albums` })
  }

  return items
}))

const sortedPhotos = computed(() => [...photos.value].sort((left, right) => Number(right.isPrimary) - Number(left.isPrimary)))
const albumPhotos = computed(() => album.value?.photos ?? [])
const canUploadVehiclePhotos = computed(() => photos.value.length < maxVehiclePhotos)

function getAlbumCover(item: WorkspaceVehicleAlbum) {
  const primary = item.photos.find(photo => photo.photoId === item.primaryPhotoId || photo.isPrimary)
  return primary?.url ?? item.photos[0]?.url ?? ''
}

function getAlbumUrl(item: WorkspaceVehicleAlbum) {
  return `#vehicles/${props.vehicleId}/albums/${item.albumId}`
}

function getBackUrl() {
  if (isAlbumMode.value) {
    return `#vehicles/${props.vehicleId}/albums`
  }

  return `#vehicles/${props.vehicleId}`
}

function askConfirmation(options: {
  title: string
  message: string
  confirmText?: string
  onConfirm: () => Promise<void>
}) {
  confirmDialog.value = {
    open: true,
    title: options.title,
    message: options.message,
    confirmText: options.confirmText ?? t('vehicleMedia.delete'),
    onConfirm: options.onConfirm
  }
}

function closeConfirmation() {
  if (isSaving.value) {
    return
  }

  confirmDialog.value.open = false
}

async function confirmAction() {
  const action = confirmDialog.value.onConfirm
  if (!action || isSaving.value || !canChangePhotos.value) {
    return
  }

  await action()
  confirmDialog.value.open = false
}

async function loadMedia() {
  isLoading.value = true
  errorMessage.value = ''

  try {
    if (vehicle.value?.id !== props.vehicleId) {
      vehicle.value = null
      vehicle.value = await loadVehicle(organizationId.value, props.vehicleId)
    }

    if (isPhotoPreview.value) {
      photos.value = []
      albums.value = []
      album.value = null
      return
    }

    if (isPhotosMode.value) {
      photos.value = await loadVehiclePhotoList(organizationId.value, props.vehicleId)
      albums.value = []
      album.value = null
      return
    }

    if (isAlbumMode.value && props.albumId) {
      album.value = await loadVehicleAlbum(organizationId.value, props.vehicleId, props.albumId)
      photos.value = []
      albums.value = []
      return
    }

    const albumData = await loadVehicleAlbums(organizationId.value, props.vehicleId)
    albums.value = albumData.albums ?? []
    photos.value = []
    album.value = null
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('vehicleMedia.errors.load')
  } finally {
    isLoading.value = false
  }
}

async function toggleSavedMediaHistory() {
  if (!isPhotosFeatureLocked.value) return
  showSavedMediaHistory.value = !showSavedMediaHistory.value
  if (!showSavedMediaHistory.value) return

  await loadMedia()
}

function collectFiles(event: Event) {
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  input.value = ''
  return files
}

function validateBatch(files: File[], currentCount?: number) {
  const batchSize = files.reduce((sum, file) => sum + file.size, 0)

  if (batchSize > maxBatchBytes) {
    return t('vehicleMedia.validation.batchSize', { maxMb: maxBatchBytes / 1024 / 1024 })
  }

  if (currentCount !== undefined && currentCount + files.length > maxVehiclePhotos) {
    return t('vehicleMedia.validation.photoCount', { maxPhotos: maxVehiclePhotos })
  }

  return ''
}

async function uploadPhotos(event: Event) {
  const files = collectFiles(event)
  if (!files.length || isSaving.value || !canChangePhotos.value) {
    return
  }

  const validationError = validateBatch(files, photos.value.length)
  if (validationError) {
    errorMessage.value = validationError
    return
  }

  isSaving.value = true
  isUploading.value = true
  errorMessage.value = ''

  try {
    await uploadVehiclePhotos(organizationId.value, props.vehicleId, files)
    await loadMedia()
    emit('changed')
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('vehicleMedia.errors.upload')
  } finally {
    isUploading.value = false
    isSaving.value = false
  }
}

async function uploadAlbumPhotos(event: Event) {
  const files = collectFiles(event)
  if (!files.length || isSaving.value || !canChangePhotos.value || !props.albumId) {
    return
  }

  const validationError = validateBatch(files)
  if (validationError) {
    errorMessage.value = validationError
    return
  }

  isSaving.value = true
  isUploading.value = true
  errorMessage.value = ''

  try {
    await uploadVehicleAlbumPhotos(organizationId.value, props.vehicleId, props.albumId, files)
    await loadMedia()
    emit('changed')
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('vehicleMedia.errors.uploadAlbum')
  } finally {
    isUploading.value = false
    isSaving.value = false
  }
}

async function makePrimary(photo: WorkspaceVehiclePhoto) {
  if (isSaving.value || !canChangePhotos.value) {
    return
  }

  isSaving.value = true
  errorMessage.value = ''

  try {
    await setPrimaryVehiclePhoto(organizationId.value, props.vehicleId, photo.photoId)
    await loadMedia()
    emit('changed')
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('vehicleMedia.errors.setMainPhoto')
  } finally {
    isSaving.value = false
  }
}

async function removePhoto(photo: WorkspaceVehiclePhoto) {
  if (isSaving.value || !canChangePhotos.value) {
    return
  }

  askConfirmation({
    title: t('vehicleMedia.deletePhotoTitle'),
    message: t('vehicleMedia.deletePhotoMessage'),
    onConfirm: async () => {
  isSaving.value = true
  errorMessage.value = ''

  try {
    await deleteVehiclePhoto(organizationId.value, props.vehicleId, photo.photoId)
    await loadMedia()
    emit('changed')
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('vehicleMedia.errors.deletePhoto')
  } finally {
    isSaving.value = false
  }
    }
  })
}

async function createAlbum() {
  if (isSaving.value || !canChangePhotos.value) {
    return
  }

  const trimmedTitle = albumTitle.value.trim()
  if (!trimmedTitle) {
    errorMessage.value = t('vehicleMedia.albumNameRequired')
    return
  }

  isSaving.value = true
  errorMessage.value = ''

  try {
    const createdAlbum = await createVehicleAlbum(organizationId.value, props.vehicleId, trimmedTitle)
    albumTitle.value = ''
    window.location.hash = `#vehicles/${props.vehicleId}/albums/${createdAlbum.albumId}`
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('vehicleMedia.errors.createAlbum')
  } finally {
    isSaving.value = false
  }
}

async function removeAlbum(item: WorkspaceVehicleAlbum) {
  if (isSaving.value || !canChangePhotos.value) {
    return
  }

  askConfirmation({
    title: t('vehicleMedia.deleteAlbumTitle'),
    message: t('vehicleMedia.deleteAlbumMessage', { title: item.title }),
    onConfirm: async () => {
  isSaving.value = true
  errorMessage.value = ''

  try {
    await deleteVehicleAlbum(organizationId.value, props.vehicleId, item.albumId)
    await loadMedia()
    emit('changed')
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('vehicleMedia.errors.deleteAlbum')
  } finally {
    isSaving.value = false
  }
    }
  })
}

async function makeAlbumPrimary(photo: WorkspaceVehiclePhoto) {
  if (isSaving.value || !canChangePhotos.value || !props.albumId) {
    return
  }

  isSaving.value = true
  errorMessage.value = ''

  try {
    await setPrimaryVehicleAlbumPhoto(organizationId.value, props.vehicleId, props.albumId, photo.photoId)
    await loadMedia()
    emit('changed')
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('vehicleMedia.errors.setAlbumCover')
  } finally {
    isSaving.value = false
  }
}

async function removeAlbumPhoto(photo: WorkspaceVehiclePhoto) {
  if (isSaving.value || !canChangePhotos.value || !props.albumId) {
    return
  }

  const albumId = props.albumId
  askConfirmation({
    title: t('vehicleMedia.deleteAlbumPhotoTitle'),
    message: t('vehicleMedia.deleteAlbumPhotoMessage'),
    onConfirm: async () => {
  isSaving.value = true
  errorMessage.value = ''

  try {
    await deleteVehicleAlbumPhoto(organizationId.value, props.vehicleId, albumId, photo.photoId)
    await loadMedia()
    emit('changed')
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('vehicleMedia.errors.deleteAlbumPhoto')
  } finally {
    isSaving.value = false
  }
    }
  })
}

onMounted(() => { void loadMedia() })
watch(() => [props.vehicleId, props.mode, props.albumId], () => {
  showSavedMediaHistory.value = false
  void loadMedia()
})
</script>

<template>
  <section class="entity-card-section workspace-media">
    <UiCard page :title="title">
      <template #actions>
        <UiButton variant="secondary" :href="getBackUrl()">{{ t('vehicleMedia.back') }}</UiButton>
        <label v-if="canWrite && isPhotosMode && canUploadVehiclePhotos" class="ui-button btn ui-button--secondary btn-outline-dark entity-upload-button" :aria-disabled="!canChangePhotos">
          {{ t('vehicleMedia.uploadPhoto') }}
          <input type="file" accept="image/*" multiple :disabled="isSaving || !canChangePhotos" @change="uploadPhotos">
        </label>
        <label v-if="canWrite && isAlbumMode" class="ui-button btn ui-button--secondary btn-outline-dark entity-upload-button" :aria-disabled="!canChangePhotos">
          {{ t('vehicleMedia.uploadPhoto') }}
          <input type="file" accept="image/*" multiple :disabled="isSaving || !canChangePhotos" @change="uploadAlbumPhotos">
        </label>
      </template>

      <UiLoader v-if="isUploading" :text="t('vehicleMedia.uploadingPhotos')" />
      <UiLoader v-else-if="isLoading" :text="t('vehicleMedia.loadingPhotos')" />
      <div v-else-if="errorMessage" class="detailing-state detailing-state-error" role="alert">{{ errorMessage }}</div>

      <template v-else>
        <WorkspaceProLock :locked="isPhotoPreview" :message="t('vehicleMedia.proAlbums')" feature="albums">
          <div v-if="isPhotoPreview" class="workspace-media__locked-preview" aria-hidden="true">
            <template v-if="isPhotosMode">
              <h3>{{ t('vehicleMedia.carPhotos') }}</h3>
              <div class="entity-photo-manage-grid">
                <span v-for="index in 4" :key="index" class="workspace-media__preview-tile"></span>
              </div>
            </template>
            <template v-else-if="isAlbumsMode">
              <div class="entity-album-create entity-album-create--page">
                <span class="workspace-media__preview-input"></span>
                <span class="workspace-media__preview-button"></span>
              </div>
              <div class="entity-album-grid entity-album-grid--page">
                <span v-for="index in 3" :key="index" class="workspace-media__preview-album"></span>
              </div>
            </template>
            <div v-else class="entity-photo-manage-grid">
              <span v-for="index in 3" :key="index" class="workspace-media__preview-tile"></span>
            </div>
          </div>
          <div v-else>
        <div v-if="isPhotosMode">
          <div v-if="sortedPhotos.length === 0" class="detailing-empty">{{ t('vehicleMedia.noVehiclePhotos') }}</div>
          <div v-else class="entity-photo-manage-grid">
            <article v-for="photo in sortedPhotos" :key="photo.photoId" class="entity-photo-manage-item">
              <button type="button" class="workspace-media__preview" :aria-label="t('vehicleMedia.openPhoto', { fileName: photo.originalFileName || t('vehicleMedia.photo') })" @click="previewPhotoUrl = photo.url">
                <img :src="photo.url" :alt="photo.originalFileName || t('vehicleMedia.photo')">
              </button>
              <span v-if="photo.isPrimary" class="entity-media-badge">{{ t('vehicleMedia.mainPhoto') }}</span>
              <div v-if="canWrite" class="entity-card-actions entity-photo-manage-actions">
                <UiButton v-if="!photo.isPrimary" variant="secondary" :disabled="isSaving || !canChangePhotos" @click="makePrimary(photo)">{{ t('vehicleMedia.setMainPhoto') }}</UiButton>
                <UiButton variant="secondary" :disabled="isSaving || !canChangePhotos" @click="removePhoto(photo)">{{ t('vehicleMedia.delete') }}</UiButton>
              </div>
            </article>
          </div>
        </div>

        <div v-else-if="isAlbumsMode">
          <form v-if="canWrite" class="entity-album-create entity-album-create--page" @submit.prevent="createAlbum">
            <UiInput v-model="albumTitle" :label="t('vehicleMedia.albumTitle')" :placeholder="t('vehicleMedia.albumTitleExample')" :disabled="isSaving || !canChangePhotos" />
            <UiButton type="submit" :disabled="isSaving || !canChangePhotos">{{ t('vehicleMedia.create') }}</UiButton>
          </form>

          <div v-if="albums.length === 0" class="detailing-empty">{{ t('vehicleMedia.noAlbums') }}</div>
          <div v-else class="entity-album-grid entity-album-grid--page">
            <article v-for="item in albums" :key="item.albumId" class="entity-album-item entity-album-item--manage">
              <a :href="getAlbumUrl(item)">
                <img v-if="getAlbumCover(item)" :src="getAlbumCover(item)" :alt="item.title">
                <span v-else class="entity-album-placeholder">{{ t('vehicleMedia.albumPlaceholder') }}</span>
                <strong>{{ item.title }}</strong>
                <small>{{ t(item.photoCount === 1 ? 'vehicleMedia.photoCountOne' : 'vehicleMedia.photoCountMany', { count: item.photoCount }) }}</small>
              </a>
              <UiButton v-if="canWrite" variant="secondary" :disabled="isSaving || !canChangePhotos" @click="removeAlbum(item)">{{ t('vehicleMedia.delete') }}</UiButton>
            </article>
          </div>
        </div>

        <div v-else>
          <div v-if="albumPhotos.length === 0" class="detailing-empty">{{ t('vehicleMedia.noAlbumPhotos') }}</div>
          <div v-else class="entity-photo-manage-grid">
            <article v-for="photo in albumPhotos" :key="photo.photoId" class="entity-photo-manage-item">
              <button type="button" class="workspace-media__preview" :aria-label="t('vehicleMedia.openPhoto', { fileName: photo.originalFileName || album?.title || t('vehicleMedia.photo') })" @click="previewPhotoUrl = photo.url">
                <img :src="photo.url" :alt="photo.originalFileName || album?.title || t('vehicleMedia.photo')">
              </button>
              <span v-if="photo.photoId === album?.primaryPhotoId || photo.isPrimary" class="entity-media-badge">{{ t('vehicleMedia.albumCover') }}</span>
              <div v-if="canWrite" class="entity-card-actions entity-photo-manage-actions">
                <UiButton v-if="photo.photoId !== album?.primaryPhotoId && !photo.isPrimary" variant="secondary" :disabled="isSaving || !canChangePhotos" @click="makeAlbumPrimary(photo)">{{ t('vehicleMedia.setAlbumCover') }}</UiButton>
                <UiButton variant="secondary" :disabled="isSaving || !canChangePhotos" @click="removeAlbumPhoto(photo)">{{ t('vehicleMedia.delete') }}</UiButton>
              </div>
            </article>
          </div>
        </div>
          </div>
        </WorkspaceProLock>
        <div v-if="isPhotosFeatureLocked" class="workspace-media__history-action">
          <UiButton variant="secondary" :disabled="isLoading" @click="toggleSavedMediaHistory">
            {{ t(showSavedMediaHistory ? 'vehicleMedia.hideSavedAlbums' : 'vehicleMedia.savedAlbums') }}
          </UiButton>
        </div>
      </template>
    </UiCard>
    <UiImagePreview :src="previewPhotoUrl" :title="title" @close="previewPhotoUrl = ''" />
    <UiConfirmDialog
      :open="confirmDialog.open"
      :title="confirmDialog.title"
      :message="confirmDialog.message"
      :confirm-text="confirmDialog.confirmText"
      :busy="isSaving"
      tone="danger"
      @close="closeConfirmation"
      @confirm="confirmAction"
    />
  </section>
</template>

<style scoped>
.workspace-media__locked-preview { display: grid; gap: 16px; }
.workspace-media__locked-preview h3 { margin: 0; font-size: 15px; }
.workspace-media__preview-tile { display: block; aspect-ratio: 4 / 3; border: 1px solid var(--crm-border, #dce2e9); border-radius: 8px; background: linear-gradient(145deg, var(--crm-surface-soft, #f4f6f8), var(--crm-border, #dce2e9)); }
.workspace-media__preview-input { display: block; width: 100%; height: 38px; border: 1px solid var(--crm-border, #dce2e9); border-radius: 6px; background: var(--crm-surface-soft, #f4f6f8); }
.workspace-media__preview-button { display: block; width: 92px; height: 38px; border-radius: 6px; background: var(--crm-border, #dce2e9); }
.workspace-media__preview-album { display: block; aspect-ratio: 1 / 1; border: 1px solid var(--crm-border, #dce2e9); border-radius: 8px; background: linear-gradient(180deg, var(--crm-surface-soft, #f4f6f8) 68%, var(--crm-border, #dce2e9) 68%); }
.workspace-media__history-action { display: flex; justify-content: flex-end; margin-top: 12px; }
.workspace-media .workspace-pro-lock__content { min-width: 0; }
</style>
