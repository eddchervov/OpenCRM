<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiConfirmDialog, UiModal } from '@opencrm/ui'
import { useLocalization } from '@opencrm/ui'
import '../maintenanceMediaMessages'
import { getIntakeDiagram } from './intakeDiagramRegistry'
import {
  deleteIntakeDamage,
  IntakeDamageApiError,
  loadIntakeDamages,
  saveIntakeDamage,
  type IntakeDamage,
  type IntakeDamagePayload,
  type IntakeDamagesResponse,
  type IntakeDamageType,
  type IntakeDamageView
} from '../api/intakeDamages'
import {
  loadMaintenancePhotos,
  uploadMaintenancePhotos,
  type WorkspaceMaintenancePhoto
} from '../api/maintenance'

const props = defineProps<{
  organizationId: string
  maintenanceId: string
  maintenanceVersion?: number
  photos: WorkspaceMaintenancePhoto[]
  readOnly: boolean
  previewOnly?: boolean
}>()
const { t } = useLocalization()

const emit = defineEmits<{
  changed: []
  linkedPhotoIdsChanged: [organizationId: string, maintenanceId: string, photoIds: string[]]
  photosChanged: [organizationId: string, maintenanceId: string, photos: WorkspaceMaintenancePhoto[]]
}>()

const views = computed<{ code: IntakeDamageView; label: string }[]>(() => [
  { code: 'front', label: t('intakeDamage.front') },
  { code: 'rear', label: t('intakeDamage.rear') },
  { code: 'left', label: t('intakeDamage.left') },
  { code: 'right', label: t('intakeDamage.right') }
])
const damageTypes = computed<{ code: IntakeDamageType; label: string }[]>(() => [
  { code: 'scratch', label: t('intakeDamage.scratch') },
  { code: 'chip', label: t('intakeDamage.chip') },
  { code: 'dent', label: t('intakeDamage.dent') },
  { code: 'other', label: t('intakeDamage.other') }
])
const activeView = ref<IntakeDamageView>('front')
const projectionView = ref<IntakeDamageView | null>(null)
const projectionTitle = computed(() => views.value.find(view => view.code === projectionView.value)?.label ?? '')
const damages = ref<IntakeDamage[]>([])
const displayedDamages = computed(() => numberDamages(damages.value))
const editingDamageNumber = computed(() => displayedDamages.value.find(damage => damage.id === editingId.value)?.displayNumber ?? null)
const editorTitle = computed(() => editingId.value
  ? editingDamageNumber.value
    ? t('intakeDamage.damageTitle', { number: editingDamageNumber.value })
    : t('intakeDamage.existingDamage')
  : t('intakeDamage.newDamage'))
const version = ref(0)
const loadError = ref('')
const diagramVersion = ref<string | null>(null)
const diagramDefinition = computed(() => getIntakeDiagram(diagramVersion.value))
const diagramImageStates = ref<Record<string, { status: 'loading' | 'loaded' | 'error'; attempt: number }>>({})
const diagramError = computed(() => {
  if (!diagramDefinition.value) return t('intakeDamage.unavailableDiagram')

  return ''
})
const canEdit = computed(() => !props.previewOnly && !props.readOnly && Boolean(diagramDefinition.value) && !loadError.value)
const isReadOnly = computed(() => !canEdit.value)
const editorError = ref('')
const isLoading = ref(false)
const isSaving = ref(false)
const isUploading = ref(false)
const draft = ref<IntakeDamagePayload | null>(null)
const isEditorOpen = ref(false)
const isRepositioning = ref(false)
const keyboardProjection = ref<IntakeDamageView | null>(null)
const keyboardPoint = ref({ x: 0.5, y: 0.5 })
const editingId = ref<string | null>(null)
const draftId = ref<string | null>(null)
const conflictSnapshot = ref<IntakeDamagesResponse | null>(null)
const conflictMessage = ref('')
const showConflictConfirmation = ref<'discard' | 'overwrite' | false>(false)
const saveNeedsReview = ref(false)
const deleteCandidate = ref<IntakeDamage | null>(null)
const deleteError = ref('')
const localPhotos = ref<WorkspaceMaintenancePhoto[]>(props.photos)
const linkedPhotoIds = computed(() => [...new Set(damages.value.flatMap(damage => damage.photoIds))])
const selectablePhotos = computed(() => localPhotos.value.filter(photo => photo.stage === 1))
const conflictDamage = computed(() => {
  const id = editingId.value ?? draftId.value
  return numberDamages(conflictSnapshot.value?.damages ?? []).find(damage => damage.id === id) ?? null
})
const canOverwriteConflict = computed(() => Boolean(conflictSnapshot.value
  && (!editingId.value || conflictDamage.value)
  && diagramVersion.value
  && conflictSnapshot.value.diagramVersion === diagramVersion.value
  && getIntakeDiagram(conflictSnapshot.value.diagramVersion)))
const displayedPhotos = computed(() => isReadOnly.value
  ? localPhotos.value.filter(photo => draft.value?.photoIds.includes(photo.photoId))
  : selectablePhotos.value)
let requestSequence = 0
let uploadSequence = 0
let abortController: AbortController | null = null

watch(() => props.photos, photos => { localPhotos.value = photos }, { deep: true })

async function reloadPhotos() {
  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  const photos = await loadMaintenancePhotos(organizationId, maintenanceId)
  if (organizationId !== props.organizationId || maintenanceId !== props.maintenanceId) return
  localPhotos.value = photos
  emit('photosChanged', organizationId, maintenanceId, photos)
}

async function loadDamages() {
  abortController?.abort()
  if (props.previewOnly) {
    requestSequence++
    damages.value = []
    localPhotos.value = []
    diagramVersion.value = 'sedan-v1'
    isLoading.value = false
    loadError.value = ''
    return
  }
  abortController = new AbortController()
  const controller = abortController
  const sequence = ++requestSequence
  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  isLoading.value = true
  loadError.value = ''

  try {
    const result = await loadIntakeDamages(organizationId, maintenanceId, controller.signal)
    if (sequence !== requestSequence || controller.signal.aborted) return
    damages.value = result.damages
    version.value = result.version
    diagramVersion.value = typeof result.diagramVersion === 'string' && result.diagramVersion.trim()
      ? result.diagramVersion
      : null
    emit('linkedPhotoIdsChanged', organizationId, maintenanceId, linkedPhotoIds.value)
  } catch (error) {
    if (sequence !== requestSequence || controller.signal.aborted) return
    loadError.value = error instanceof Error ? error.message : t('intakeDamage.loadError')
  } finally {
    if (sequence === requestSequence) isLoading.value = false
  }
}

watch(() => [props.organizationId, props.maintenanceId, props.previewOnly] as const, () => {
  uploadSequence++
  isUploading.value = false
  localPhotos.value = props.photos
  damages.value = []
  projectionView.value = null
  version.value = 0
  diagramVersion.value = null
  draft.value = null
  isEditorOpen.value = false
  isRepositioning.value = false
  editingId.value = null
  draftId.value = null
  conflictSnapshot.value = null
  conflictMessage.value = ''
  showConflictConfirmation.value = false
  saveNeedsReview.value = false
  deleteCandidate.value = null
  deleteError.value = ''
  editorError.value = ''
  loadDamages()
}, { immediate: true })
watch(() => props.readOnly, readOnly => {
  if (!readOnly) return
  resetEditor()
  projectionView.value = null
})
watch(() => [props.maintenanceVersion, Boolean(draft.value), Boolean(deleteCandidate.value), isSaving.value] as const, () => {
  if (props.previewOnly || draft.value || deleteCandidate.value || isSaving.value || props.maintenanceVersion === undefined
    || props.maintenanceVersion === version.value) return
  void loadDamages()
})
onBeforeUnmount(() => {
  abortController?.abort()
  requestSequence++
})

function pointFromEvent(event: MouseEvent) {
  const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect()
  return {
    x: Math.max(0.03, Math.min(0.97, (event.clientX - bounds.left) / bounds.width)),
    y: Math.max(0.08, Math.min(0.92, (event.clientY - bounds.top) / bounds.height))
  }
}

function diagramImageStateKey(viewCode: IntakeDamageView, imageVersion = diagramVersion.value) {
  return `${imageVersion ?? ''}:${viewCode}`
}

function diagramImageStatus(viewCode: IntakeDamageView) {
  const diagram = diagramDefinition.value
  if (!diagram) return 'error'

  return diagramImageStates.value[diagramImageStateKey(viewCode)]?.status ?? 'loading'
}

function diagramImageAttempt(viewCode: IntakeDamageView) {
  return diagramImageStates.value[diagramImageStateKey(viewCode)]?.attempt ?? 0
}

function diagramImageKey(viewCode: IntakeDamageView) {
  return `${diagramImageStateKey(viewCode)}:${diagramImageAttempt(viewCode)}`
}

function diagramImageUrl(viewCode: IntakeDamageView) {
  const diagram = diagramDefinition.value
  return diagram?.views[viewCode].imageUrl ?? null
}

function isDiagramViewReady(viewCode: IntakeDamageView) {
  const diagram = diagramDefinition.value
  return Boolean(diagram && diagramImageStatus(viewCode) === 'loaded')
}

function setDiagramImageStatus(
  viewCode: IntakeDamageView,
  imageVersion: string | null,
  attempt: number,
  status: 'loaded' | 'error'
) {
  if (imageVersion !== diagramVersion.value || attempt !== diagramImageAttempt(viewCode)) return
  const key = diagramImageStateKey(viewCode, imageVersion)
  diagramImageStates.value = { ...diagramImageStates.value, [key]: { status, attempt } }
}

function handleDiagramImageLoad(viewCode: IntakeDamageView, imageVersion: string | null, attempt: number) {
  setDiagramImageStatus(viewCode, imageVersion, attempt, 'loaded')
}

function handleDiagramImageError(viewCode: IntakeDamageView, imageVersion: string | null, attempt: number) {
  setDiagramImageStatus(viewCode, imageVersion, attempt, 'error')
}

function retryDiagramImage(viewCode: IntakeDamageView) {
  const key = diagramImageStateKey(viewCode)
  const attempt = diagramImageStates.value[key]?.attempt ?? 0
  diagramImageStates.value = { ...diagramImageStates.value, [key]: { status: 'loading', attempt: attempt + 1 } }
}

function openNewAt(point: { x: number; y: number }, viewCode = activeView.value) {
  if (!canEdit.value || !isDiagramViewReady(viewCode) || isSaving.value || isLoading.value || loadError.value) return
  activeView.value = viewCode
  editingId.value = null
  draftId.value = crypto.randomUUID()
  isEditorOpen.value = true
  isRepositioning.value = false
  editorError.value = ''
  clearConflict()
  draft.value = {
    viewCode: activeView.value,
    damageType: 'scratch',
    ...point,
    description: '',
    photoIds: []
  }
}

function numberDamages(items: IntakeDamage[]) {
  return [...items].sort((first, second) => first.number - second.number)
    .map((damage, index) => ({ ...damage, displayNumber: index + 1 }))
}

function damagesForView(viewCode: IntakeDamageView) {
  return displayedDamages.value.filter(damage => damage.viewCode === viewCode)
}

function openProjection(viewCode: IntakeDamageView) {
  if (!diagramDefinition.value || !isDiagramViewReady(viewCode) || isSaving.value || isUploading.value || isLoading.value || loadError.value) return
  activeView.value = viewCode
  projectionView.value = viewCode
  keyboardProjection.value = null
  keyboardPoint.value = isRepositioning.value && draft.value
    ? { x: draft.value.x, y: draft.value.y }
    : { x: 0.5, y: 0.5 }
}

function closeProjection() {
  if (isSaving.value || isUploading.value) return
  projectionView.value = null
  if (isRepositioning.value) cancelRepositioning()
}

function editDamage(damage: IntakeDamage) {
  if (isSaving.value || isLoading.value || isRepositioning.value) return
  editingId.value = damage.id
  activeView.value = damage.viewCode
  projectionView.value = isDiagramViewReady(damage.viewCode) ? damage.viewCode : null
  isEditorOpen.value = true
  isRepositioning.value = false
  editorError.value = ''
  clearConflict()
  draft.value = {
    viewCode: damage.viewCode,
    damageType: damage.damageType,
    x: damage.x,
    y: damage.y,
    description: damage.description ?? '',
    photoIds: [...damage.photoIds]
  }
}

function closeEditor() {
  if (isSaving.value || isUploading.value || deleteCandidate.value) return
  resetEditor()
}

function resetEditor() {
  draft.value = null
  isEditorOpen.value = false
  isRepositioning.value = false
  editingId.value = null
  draftId.value = null
  editorError.value = ''
  clearConflict()
}

function clearConflict() {
  conflictSnapshot.value = null
  conflictMessage.value = ''
  showConflictConfirmation.value = false
  saveNeedsReview.value = false
}

function startRepositioning() {
  if (!draft.value || !canEdit.value || !isDiagramViewReady(draft.value.viewCode) || isSaving.value || isUploading.value) return
  isRepositioning.value = true
  projectionView.value = draft.value.viewCode
  isEditorOpen.value = false
  keyboardProjection.value = draft.value.viewCode
  keyboardPoint.value = { x: draft.value.x, y: draft.value.y }
}

function cancelRepositioning() {
  isRepositioning.value = false
  isEditorOpen.value = Boolean(draft.value)
}

function handleProjectionClick(event: MouseEvent, viewCode: IntakeDamageView) {
  if (isReadOnly.value || isSaving.value || isUploading.value || !projectionView.value || !isDiagramViewReady(viewCode)) return
  const point = pointFromEvent(event)
  if (isRepositioning.value && draft.value) {
    draft.value.viewCode = viewCode
    draft.value.x = point.x
    draft.value.y = point.y
    activeView.value = viewCode
    isRepositioning.value = false
    isEditorOpen.value = true
    return
  }
  openNewAt(point, viewCode)
}

function handleProjectionFocus(viewCode: IntakeDamageView) {
  keyboardProjection.value = viewCode
  if (isRepositioning.value && draft.value) {
    keyboardPoint.value = { x: draft.value.x, y: draft.value.y }
    return
  }
  keyboardPoint.value = { x: 0.5, y: 0.5 }
}

function handleProjectionKeydown(event: KeyboardEvent, viewCode: IntakeDamageView) {
  if (isReadOnly.value || isSaving.value || isUploading.value || projectionView.value !== viewCode || !isDiagramViewReady(viewCode)) return
  const step = event.shiftKey ? 0.1 : 0.03
  if (event.key === 'ArrowLeft' || event.key === 'ArrowRight' || event.key === 'ArrowUp' || event.key === 'ArrowDown') {
    event.preventDefault()
    keyboardProjection.value = viewCode
    keyboardPoint.value = {
      x: Math.max(0.03, Math.min(0.97, keyboardPoint.value.x + (event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0))),
      y: Math.max(0.08, Math.min(0.92, keyboardPoint.value.y + (event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0)))
    }
    return
  }
  if (event.key !== 'Enter' && event.key !== ' ') return
  event.preventDefault()
  handleKeyboardPosition(viewCode)
}

function handleKeyboardPosition(viewCode: IntakeDamageView) {
  if (isRepositioning.value && draft.value) {
    draft.value.viewCode = viewCode
    draft.value.x = keyboardPoint.value.x
    draft.value.y = keyboardPoint.value.y
    activeView.value = viewCode
    isRepositioning.value = false
    isEditorOpen.value = true
    return
  }
  openNewAt(keyboardPoint.value, viewCode)
}

function togglePhoto(photoId: string) {
  if (!draft.value) return
  draft.value.photoIds = draft.value.photoIds.includes(photoId)
    ? draft.value.photoIds.filter(id => id !== photoId)
    : [...draft.value.photoIds, photoId]
}

async function uploadDraftPhotos(event: Event) {
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  input.value = ''
  if (!files.length || isUploading.value || !draft.value) return
  if (files.length > 10) {
    editorError.value = t('intakeDamage.uploadLimit', { count: 10 })
    return
  }

  isUploading.value = true
  editorError.value = ''
  const sequence = ++uploadSequence
  const currentDraft = draft.value
  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  try {
    await uploadMaintenancePhotos(organizationId, maintenanceId, files, 1)
    if (sequence !== uploadSequence || organizationId !== props.organizationId || maintenanceId !== props.maintenanceId) return
    await reloadPhotos()
  } catch (error) {
    if (sequence === uploadSequence && organizationId === props.organizationId && maintenanceId === props.maintenanceId && draft.value === currentDraft) {
      editorError.value = error instanceof Error ? error.message : t('intakeDamage.uploadError')
    }
  } finally {
    if (sequence === uploadSequence) isUploading.value = false
  }
}

async function saveDamage() {
  const currentDraft = draft.value
  if (!currentDraft || !isEditorOpen.value || deleteCandidate.value || isRepositioning.value
    || isSaving.value || isUploading.value || !canEdit.value || !diagramVersion.value || saveNeedsReview.value) return
  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  const payload: IntakeDamagePayload = {
    ...currentDraft,
    description: normalizeDescription(currentDraft.description),
    photoIds: currentDraft.photoIds.filter(photoId => selectablePhotos.value.some(photo => photo.photoId === photoId))
  }
  const damageId = editingId.value ?? draftId.value ?? crypto.randomUUID()
  const sentVersion = version.value
  const previousDamage = editingId.value
    ? damages.value.find(damage => damage.id === editingId.value) ?? null
    : null
  if (!editingId.value) draftId.value = damageId
  isSaving.value = true
  editorError.value = ''

  try {
    await saveIntakeDamage(
      organizationId,
      maintenanceId,
      damageId,
      sentVersion,
      diagramVersion.value,
      payload
    )
    if (organizationId !== props.organizationId || maintenanceId !== props.maintenanceId) return
    resetEditor()
    projectionView.value = null
    emit('changed')
    await loadDamages()
    if (loadError.value) loadError.value = t('intakeDamage.savedRefreshError')
  } catch (error) {
    if (organizationId !== props.organizationId || maintenanceId !== props.maintenanceId) return
    editorError.value = error instanceof Error ? error.message : t('intakeDamage.saveError')
    if (error instanceof IntakeDamageApiError && error.status === 409) {
      editorError.value = ''
      saveNeedsReview.value = true
      conflictMessage.value = t('intakeDamage.conflictChanged')
      await fetchConflictSnapshot(organizationId, maintenanceId)
    } else if (error instanceof TypeError || (error instanceof IntakeDamageApiError && error.status >= 500)) {
      conflictMessage.value = t('intakeDamage.conflictUnknown')
      const snapshot = await fetchConflictSnapshot(organizationId, maintenanceId)
      const savedDamage = snapshot?.damages.find(damage => damage.id === damageId)
      if (snapshot?.diagramVersion === diagramVersion.value && savedDamage && payloadMatchesDamage(payload, savedDamage)) {
        damages.value = snapshot!.damages
        version.value = snapshot!.version
        emit('linkedPhotoIdsChanged', organizationId, maintenanceId, linkedPhotoIds.value)
        emit('changed')
        resetEditor()
        projectionView.value = null
      } else if (snapshot && snapshot.diagramVersion === diagramVersion.value && snapshot.version === sentVersion && (
        editingId.value
          ? Boolean(previousDamage && snapshot.damages.some(damage => damage.id === damageId && damageMatchesRecord(previousDamage, damage)))
          : !snapshot.damages.some(damage => damage.id === damageId)
      )) {
        conflictSnapshot.value = null
        conflictMessage.value = ''
        saveNeedsReview.value = false
        editorError.value = t('intakeDamage.conflictUnchanged')
      } else {
        saveNeedsReview.value = true
        if (snapshot && snapshot.diagramVersion !== diagramVersion.value) {
          conflictMessage.value = t('intakeDamage.diagramChanged')
        }
      }
    }
  } finally {
    isSaving.value = false
  }
}

function normalizeDescription(description: string | null) {
  return description?.replace(/\s+/g, ' ').trim() || null
}

function payloadMatchesDamage(payload: IntakeDamagePayload, damage: IntakeDamage) {
  return damage.viewCode === payload.viewCode
    && damage.damageType === payload.damageType
    && damage.x === payload.x
    && damage.y === payload.y
    && normalizeDescription(damage.description) === normalizeDescription(payload.description)
    && damage.photoIds.length === payload.photoIds.length
    && damage.photoIds.every(photoId => payload.photoIds.includes(photoId))
}

function damageMatchesRecord(first: IntakeDamage, second: IntakeDamage) {
  return first.viewCode === second.viewCode
    && first.damageType === second.damageType
    && first.x === second.x
    && first.y === second.y
    && normalizeDescription(first.description) === normalizeDescription(second.description)
    && first.photoIds.length === second.photoIds.length
    && first.photoIds.every(photoId => second.photoIds.includes(photoId))
}

async function fetchConflictSnapshot(organizationId = props.organizationId, maintenanceId = props.maintenanceId) {
  try {
    const snapshot = await loadIntakeDamages(organizationId, maintenanceId)
    if (organizationId !== props.organizationId || maintenanceId !== props.maintenanceId) return null
    conflictSnapshot.value = snapshot
    if (snapshot.diagramVersion !== diagramVersion.value) {
      conflictMessage.value = t('intakeDamage.diagramChanged')
    }
    return snapshot
  } catch {
    if (organizationId !== props.organizationId || maintenanceId !== props.maintenanceId) return null
    conflictMessage.value = t('intakeDamage.savedLoadError')
    return null
  }
}

async function confirmConflictOverwrite() {
  const snapshot = conflictSnapshot.value
  if (!snapshot || !saveNeedsReview.value || !canOverwriteConflict.value) return
  damages.value = snapshot.damages
  version.value = snapshot.version
  emit('linkedPhotoIdsChanged', props.organizationId, props.maintenanceId, linkedPhotoIds.value)
  editorError.value = ''
  clearConflict()
  await saveDamage()
}

function confirmConflictRefresh() {
  const snapshot = conflictSnapshot.value
  if (!snapshot || !saveNeedsReview.value) return
  damages.value = snapshot.damages
  version.value = snapshot.version
  diagramVersion.value = typeof snapshot.diagramVersion === 'string' && snapshot.diagramVersion.trim()
    ? snapshot.diagramVersion
    : null
  emit('linkedPhotoIdsChanged', props.organizationId, props.maintenanceId, linkedPhotoIds.value)
  resetEditor()
  projectionView.value = null
}

function askDelete(damage: IntakeDamage) {
  if (isLoading.value || isSaving.value) return
  deleteCandidate.value = damage
  deleteError.value = ''
}

async function confirmDelete() {
  const damage = deleteCandidate.value
  if (!damage || props.readOnly || isSaving.value) return
  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  isSaving.value = true
  deleteError.value = ''
  try {
    await deleteIntakeDamage(organizationId, maintenanceId, damage.id, version.value)
    if (organizationId !== props.organizationId || maintenanceId !== props.maintenanceId) return
    emit('changed')
    damages.value = damages.value.filter(item => item.id !== damage.id)
    emit('linkedPhotoIdsChanged', organizationId, maintenanceId, linkedPhotoIds.value)
    deleteCandidate.value = null
    resetEditor()
    projectionView.value = null
    await loadDamages()
  } catch (error) {
    if (organizationId !== props.organizationId || maintenanceId !== props.maintenanceId) return
    if (error instanceof IntakeDamageApiError && error.status === 409) {
      deleteCandidate.value = null
      if (editingId.value === damage.id && draft.value) {
        saveNeedsReview.value = true
        editorError.value = t('intakeDamage.deleteConflict')
        conflictMessage.value = t('intakeDamage.deleteConflictAdvice')
        await fetchConflictSnapshot(organizationId, maintenanceId)
        return
      }
      await loadDamages()
      if (organizationId === props.organizationId && maintenanceId === props.maintenanceId) {
        deleteError.value = t('intakeDamage.deleteChanged')
      }
      return
    }
    deleteError.value = error instanceof Error ? error.message : t('intakeDamage.deleteError')
  } finally {
    isSaving.value = false
  }
}

function damageTypeLabel(type: IntakeDamageType) {
  return damageTypes.value.find(item => item.code === type)?.label ?? t('intakeDamage.other')
}

function damageSummary(damage: IntakeDamage | IntakeDamagePayload) {
  const viewLabel = views.value.find(view => view.code === damage.viewCode)?.label ?? ''
  const description = normalizeDescription(damage.description) ?? t('intakeDamage.noDescription')
  const number = 'displayNumber' in damage && typeof damage.displayNumber === 'number'
    ? damage.displayNumber
    : 'id' in damage
      ? displayedDamages.value.find(item => item.id === damage.id)?.displayNumber
      : editingDamageNumber.value
  const numberLabel = number ? t('intakeDamage.numberPrefix', { number }) : ''
  return t('intakeDamage.summary', { number: numberLabel, type: damageTypeLabel(damage.damageType), view: viewLabel, description, count: damage.photoIds.length })
}
</script>

<template>
  <section class="intake-damage" aria-labelledby="intake-damage-title">
    <div class="intake-damage__heading ui-section-header">
      <h3 id="intake-damage-title">{{ t('intakeDamage.mapTitle') }} <span class="intake-damage__count">{{ damages.length }}</span></h3>
      <UiButton v-if="loadError || diagramError" variant="secondary" :disabled="isLoading" @click="loadDamages">
        {{ isLoading ? t('intakeDamage.loading') : t('intakeDamage.refresh') }}
      </UiButton>
    </div>

    <p v-if="loadError" class="intake-damage__error" role="alert">{{ loadError }}</p>
    <p v-else-if="diagramError" class="intake-damage__error" role="alert">{{ diagramError }}</p>
    <p v-if="isLoading" class="intake-damage__muted" role="status">{{ t('intakeDamage.loadingDamages') }}</p>

    <div class="intake-damage__layout">
      <div class="intake-damage__map-area">
        <p v-if="isRepositioning" class="intake-damage__hint" role="status">
          {{ t('intakeDamage.repositionHint') }}
          <UiButton type="button" variant="secondary" @click="cancelRepositioning">{{ t('intakeDamage.cancel') }}</UiButton>
        </p>
        <div v-if="diagramDefinition" class="intake-damage__map-grid" :aria-label="t('intakeDamage.projections')">
        <section v-for="view in views" :key="view.code" class="intake-damage__map-panel">
          <h4>{{ view.label }}</h4>
          <div class="intake-damage__map"
            :style="{ aspectRatio: diagramDefinition.aspectRatio }" :aria-label="t('intakeDamage.vehicleDiagram', { view: view.label })">
            <button type="button" class="intake-damage__projection" :class="`intake-damage__projection--${view.code}`"
              :disabled="isLoading || isSaving || isUploading || Boolean(loadError) || !isDiagramViewReady(view.code)"
              :aria-label="t('intakeDamage.openView', { view: view.label })" @click="openProjection(view.code)">
              <img v-if="diagramImageUrl(view.code)" :key="diagramImageKey(view.code)" class="intake-damage__diagram-image"
                :src="diagramImageUrl(view.code)!" :alt="t('intakeDamage.vehicleProjection', { view: view.label })" draggable="false"
                @load="handleDiagramImageLoad(view.code, diagramVersion, diagramImageAttempt(view.code))"
                @error="handleDiagramImageError(view.code, diagramVersion, diagramImageAttempt(view.code))">
              <span v-if="diagramImageUrl(view.code) && diagramImageStatus(view.code) !== 'loaded'"
                class="intake-damage__diagram-status" role="status">
                {{ diagramImageStatus(view.code) === 'error' ? t('intakeDamage.diagramLoadError') : t('intakeDamage.loadingDiagram') }}
              </span>
            </button>
            <button v-if="diagramImageStatus(view.code) === 'error'" type="button" class="intake-damage__diagram-retry"
              @click.stop="retryDiagramImage(view.code)">{{ t('intakeDamage.retryDiagram') }}</button>
            <button v-for="damage in (isDiagramViewReady(view.code) ? damagesForView(view.code) : [])" :key="damage.id" type="button"
              class="intake-damage__marker" :class="[`intake-damage__type--${damage.damageType}`, { 'intake-damage__marker--repositioning': isRepositioning }]"
              :style="{ left: `${damage.x * 100}%`, top: `${damage.y * 100}%` }"
              :aria-label="t('intakeDamage.damageMarker', { number: damage.displayNumber, type: damageTypeLabel(damage.damageType) })"
              @click.stop="openProjection(view.code)">{{ damage.displayNumber }}</button>
          </div>
        </section>
        </div>
        <p v-if="canEdit && !isRepositioning" class="intake-damage__hint">{{ t('intakeDamage.mapHint') }}</p>
      </div>

      <ol v-if="damages.length" class="intake-damage__list" :aria-label="t('intakeDamage.damageList')">
        <li v-for="damage in displayedDamages" :key="damage.id">
          <button type="button" :disabled="isRepositioning || isLoading || isSaving" @click="editDamage(damage)">
            <span class="intake-damage__number" :class="`intake-damage__type--${damage.damageType}`">{{ damage.displayNumber }}</span>
            <span><strong>{{ damageTypeLabel(damage.damageType) }}</strong><small>{{ views.find(view => view.code === damage.viewCode)?.label }}<template v-if="damage.description"> · {{ damage.description }}</template></small></span>
            <span v-if="damage.photoIds.length" class="intake-damage__photo-count">{{ t('intakeDamage.photoCount', { count: damage.photoIds.length }) }}</span>
          </button>
        </li>
      </ol>
      <p v-else class="intake-damage__empty">{{ t('intakeDamage.empty') }}</p>
    </div>
  </section>

  <UiModal :open="Boolean(projectionView) && !isEditorOpen" :title="projectionTitle" @close="closeProjection">
    <div v-if="projectionView && diagramDefinition" class="intake-damage__projection-dialog" @keydown.esc.prevent="closeProjection">
      <p class="intake-damage__hint">{{ isReadOnly ? t('intakeDamage.selectDamage') : t('intakeDamage.clickDamage') }}</p>
      <span id="intake-damage-keyboard-help" class="intake-damage__sr-only">
        {{ t('intakeDamage.keyboardHint') }}
      </span>
      <div class="intake-damage__map intake-damage__map--enlarged"
        :style="{ aspectRatio: diagramDefinition.aspectRatio }" :aria-label="t('intakeDamage.enlargedDiagram', { view: projectionTitle })">
        <div class="intake-damage__projection intake-damage__projection--picker"
          :class="`intake-damage__projection--${projectionView}`"
          :role="isReadOnly ? 'img' : 'button'" :tabindex="isReadOnly ? undefined : 0"
          :aria-label="t('intakeDamage.damageLocation', { view: projectionTitle })"
          :aria-describedby="isReadOnly ? undefined : 'intake-damage-keyboard-help'"
          aria-keyshortcuts="ArrowUp ArrowDown ArrowLeft ArrowRight Enter"
          @focus="handleProjectionFocus(projectionView)" @keydown="handleProjectionKeydown($event, projectionView)"
          @click="handleProjectionClick($event, projectionView)">
          <img v-if="diagramImageUrl(projectionView)" :key="diagramImageKey(projectionView)" class="intake-damage__diagram-image"
            :src="diagramImageUrl(projectionView)!" :alt="t('intakeDamage.vehicleProjection', { view: projectionTitle })" draggable="false"
            @load="handleDiagramImageLoad(projectionView, diagramVersion, diagramImageAttempt(projectionView))"
            @error="handleDiagramImageError(projectionView, diagramVersion, diagramImageAttempt(projectionView))">
          <span v-if="diagramImageUrl(projectionView) && diagramImageStatus(projectionView) !== 'loaded'"
            class="intake-damage__diagram-status" role="status">
            {{ diagramImageStatus(projectionView) === 'error' ? t('intakeDamage.diagramLoadError') : t('intakeDamage.loadingDiagram') }}
          </span>
          <span v-if="!isReadOnly && keyboardProjection === projectionView" class="intake-damage__keyboard-marker"
            :style="{ left: `${keyboardPoint.x * 100}%`, top: `${keyboardPoint.y * 100}%` }" aria-hidden="true" />
        </div>
        <button v-for="damage in (isDiagramViewReady(projectionView) ? damagesForView(projectionView) : [])" :key="damage.id" type="button"
          class="intake-damage__marker" :class="[`intake-damage__type--${damage.damageType}`, { 'intake-damage__marker--repositioning': isRepositioning }]"
          :style="{ left: `${damage.x * 100}%`, top: `${damage.y * 100}%` }"
          :aria-label="t('intakeDamage.damageMarker', { number: damage.displayNumber, type: damageTypeLabel(damage.damageType) })"
          @click.stop="editDamage(damage)">{{ damage.displayNumber }}</button>
      </div>
      <button v-if="diagramImageStatus(projectionView) === 'error'" type="button" class="intake-damage__diagram-retry"
        @click.stop="retryDiagramImage(projectionView)">{{ t('intakeDamage.retryDiagram') }}</button>
      <div class="intake-damage__actions">
        <UiButton v-if="isRepositioning" variant="secondary" @click="projectionView = null">{{ t('intakeDamage.anotherView') }}</UiButton>
        <UiButton variant="secondary" @click="closeProjection">{{ isRepositioning ? t('intakeDamage.cancel') : t('intakeDamage.closeMap') }}</UiButton>
      </div>
    </div>
  </UiModal>

  <UiModal :open="Boolean(draft) && isEditorOpen && !deleteCandidate && !showConflictConfirmation" :title="editorTitle" @close="closeEditor">
    <form v-if="draft" class="intake-damage__form" @submit.prevent="saveDamage" @keydown.esc.prevent="closeEditor">
      <label for="intake-damage-type">{{ t('intakeDamage.damageType') }}</label>
      <select id="intake-damage-type" v-model="draft.damageType" :disabled="isReadOnly || isSaving || isUploading">
        <option v-for="type in damageTypes" :key="type.code" :value="type.code">{{ type.label }}</option>
      </select>

      <p><strong>{{ t('intakeDamage.viewLabel') }}</strong> {{ views.find(view => view.code === draft?.viewCode)?.label }}</p>
      <UiButton v-if="canEdit" type="button" variant="secondary" :disabled="isSaving || isUploading"
        @click="startRepositioning">{{ t('intakeDamage.changeLocation') }}</UiButton>

      <label for="intake-damage-description">{{ t('intakeDamage.description') }}</label>
      <textarea id="intake-damage-description" v-model="draft.description" rows="3" maxlength="1000"
        :disabled="isReadOnly || isSaving || isUploading" :placeholder="t('intakeDamage.descriptionPlaceholder')" />

      <fieldset class="intake-damage__photos">
        <legend>{{ t('intakeDamage.beforeWork') }}</legend>
        <template v-if="isReadOnly">
          <a v-for="photo in displayedPhotos" :key="photo.photoId" class="intake-damage__photo-preview"
            :href="photo.url" target="_blank" rel="noopener noreferrer" :aria-label="t('intakeDamage.openDamagePhoto', { id: photo.photoId })">
            <img :src="photo.url" :alt="t('intakeDamage.damagePhotoAlt')">
            <span>{{ t('intakeDamage.openPhoto') }}</span>
          </a>
        </template>
        <template v-else>
          <label v-for="photo in displayedPhotos" :key="photo.photoId">
            <input type="checkbox" :checked="draft.photoIds.includes(photo.photoId)" :disabled="isSaving || isUploading"
              @change="togglePhoto(photo.photoId)">
            <img :src="photo.url" alt="">
          </label>
        </template>
        <label v-if="canEdit" class="intake-damage__upload">
          {{ isUploading ? t('intakeDamage.uploading') : t('intakeDamage.uploadBeforeWork') }}
          <input type="file" accept="image/*" multiple :disabled="isSaving || isUploading" @change="uploadDraftPhotos">
        </label>
        <p v-if="!displayedPhotos.length" class="intake-damage__muted">
          {{ isReadOnly ? t('intakeDamage.attachedPhotos') : t('intakeDamage.noBeforePhotos') }}
        </p>
      </fieldset>

      <p v-if="editorError" class="intake-damage__error" role="alert">{{ editorError }}</p>
      <div v-if="saveNeedsReview" class="intake-damage__conflict" role="alert">
        <p>{{ conflictMessage }}</p>
        <p v-if="conflictSnapshot">{{ t('intakeDamage.saved', { summary: conflictDamage ? damageSummary(conflictDamage) : editingId ? t('intakeDamage.deletedMark') : t('intakeDamage.notAddedMark') }) }}</p>
        <p v-if="draft">{{ t('intakeDamage.yourChanges', { summary: damageSummary(draft) }) }}</p>
        <UiButton type="button" variant="secondary" :disabled="isSaving" @click="fetchConflictSnapshot()">
          {{ t('intakeDamage.refreshSaved') }}
        </UiButton>
        <UiButton v-if="conflictSnapshot" type="button" variant="secondary" :disabled="isSaving"
          @click="showConflictConfirmation = 'discard'">{{ t('intakeDamage.openSavedInspection') }}</UiButton>
        <UiButton v-if="conflictSnapshot && canOverwriteConflict" type="button" variant="secondary" :disabled="isSaving"
          @click="showConflictConfirmation = 'overwrite'">{{ t('intakeDamage.saveMyVersion') }}</UiButton>
      </div>
      <div class="intake-damage__actions">
        <UiButton v-if="canEdit" type="submit" :disabled="isSaving || isUploading || Boolean(loadError) || saveNeedsReview">{{ isSaving ? t('intakeDamage.saving') : t('intakeDamage.save') }}</UiButton>
        <UiButton type="button" variant="secondary" :disabled="isSaving || isUploading" @click="closeEditor">{{ isReadOnly ? t('intakeDamage.closeEditor') : t('intakeDamage.cancel') }}</UiButton>
        <button v-if="editingId && canEdit" type="button" class="intake-damage__delete" :disabled="isSaving || isUploading || saveNeedsReview"
          @click="askDelete(damages.find(damage => damage.id === editingId)!)">{{ t('intakeDamage.delete') }}</button>
      </div>
    </form>
  </UiModal>

  <UiConfirmDialog :open="Boolean(deleteCandidate)" :title="t('intakeDamage.deleteTitle')"
    :message="deleteError || t('intakeDamage.deleteMessage')"
    :confirm-text="t('intakeDamage.delete')" :busy="isSaving" tone="danger"
    @close="!isSaving && (deleteCandidate = null)" @confirm="confirmDelete" />
  <UiConfirmDialog :open="Boolean(showConflictConfirmation)"
    :title="showConflictConfirmation === 'overwrite' ? t('intakeDamage.overwriteTitle') : t('intakeDamage.discardTitle')"
    :message="showConflictConfirmation === 'overwrite' ? t('intakeDamage.overwriteMessage') : t('intakeDamage.discardMessage')"
    :confirm-text="showConflictConfirmation === 'overwrite' ? t('intakeDamage.overwriteConfirm') : t('intakeDamage.discardConfirm')"
    :busy="isSaving" @close="showConflictConfirmation = false"
    @confirm="showConflictConfirmation === 'overwrite' ? confirmConflictOverwrite() : confirmConflictRefresh()" />
  <p v-if="deleteError && !deleteCandidate" class="intake-damage__error" role="alert">{{ deleteError }}</p>
</template>

<style scoped>
.intake-damage { padding: 14px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; background: var(--crm-surface, #fff); }
.intake-damage__heading { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.intake-damage__heading h3 { display: flex; align-items: center; gap: 8px; margin-right: auto; }
.intake-damage__count { display: inline-flex; align-items: center; justify-content: center; flex-shrink: 0; min-width: 22px; height: 22px; padding: 0 6px; box-sizing: border-box; border-radius: 999px; background: var(--crm-accent, #e87532); color: #fff; font-size: 12px; font-weight: 600; line-height: 1; }
.intake-damage__sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0; }
.intake-damage__layout { display: grid; grid-template-columns: minmax(0, 1.8fr) minmax(240px, .8fr); gap: 16px; align-items: start; }
.intake-damage__map-area { min-width: 0; }
.intake-damage__map-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; min-width: 0; }
.intake-damage__map-panel { min-width: 0; }
.intake-damage__map-panel h4 { margin: 0 0 6px; color: var(--crm-text, #202833); font-size: 13px; }
.intake-damage__map { position: relative; width: 100%; box-sizing: border-box; border: 0; box-shadow: inset 0 0 0 1px var(--crm-line, #dce2e9); border-radius: 8px; background: var(--intake-diagram-surface, var(--crm-surface-soft, #f7f8fa)); overflow: hidden; }
.intake-damage__projection { position: absolute; inset: 0; cursor: zoom-in; touch-action: manipulation; border: 0; padding: 0; background-color: transparent; }
.intake-damage__diagram-image { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: fill; pointer-events: none; filter: var(--intake-diagram-filter, none); }
.intake-damage__diagram-status { position: absolute; inset: 0; display: grid; place-items: center; padding: 12px; background: var(--intake-diagram-surface, var(--crm-surface-soft, #f7f8fa)); color: var(--crm-text-muted, #667085); font-size: 13px; text-align: center; pointer-events: none; }
.intake-damage__diagram-retry { position: absolute; z-index: 4; top: 8px; right: 8px; padding: 6px 9px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 5px; background: var(--crm-surface, #fff); color: var(--crm-text, #202833); font: inherit; font-size: 12px; cursor: pointer; }
.intake-damage__projection--picker { cursor: crosshair; }
.intake-damage__projection-dialog { min-width: 0; }
.intake-damage__projection-dialog > .intake-damage__hint { margin: 0 0 12px; }
:global(.ui-modal__panel:has(.intake-damage__projection-dialog)) { width: min(960px, calc(100vw - 48px)); max-height: calc(100dvh - 48px); display: flex; flex-direction: column; }
:global(.ui-modal__panel:has(.intake-damage__projection-dialog) > .ui-modal__body) { overflow-y: auto; min-height: 0; }
:global(.ui-modal__panel:has(.intake-damage__form)) { max-height: calc(100dvh - 48px); display: flex; flex-direction: column; }
:global(.ui-modal__panel:has(.intake-damage__form) > .ui-modal__header) { flex-shrink: 0; }
:global(.ui-modal__panel:has(.intake-damage__form) > .ui-modal__body) { overflow-y: auto; min-height: 0; }
:global(:root[data-crm-theme="dark"]) { --intake-diagram-filter: brightness(0) invert(0.85); --intake-diagram-surface: var(--crm-surface, #232831); }
.intake-damage__projection:focus-visible { outline: 3px solid var(--crm-accent, #e87532); outline-offset: -3px; }
.intake-damage__projection:not(:focus-visible) .intake-damage__keyboard-marker { display: none; }
.intake-damage__keyboard-marker { position: absolute; width: 20px; height: 20px; transform: translate(-50%, -50%); border: 3px solid #fff; border-radius: 50%; background: var(--crm-accent, #e87532); box-shadow: 0 0 0 2px var(--crm-accent, #e87532), 0 1px 5px #0006; pointer-events: none; }
.intake-damage__marker { position: absolute; display: grid; place-items: center; width: 36px; height: 36px; padding: 0; transform: translate(-50%, -50%); border: 2px solid #fff; border-radius: 50%; background: var(--intake-damage-color, var(--crm-accent, #e87532)); box-shadow: 0 1px 5px #0006; color: #fff; font: inherit; font-weight: 700; cursor: pointer; touch-action: manipulation; }
.intake-damage__type--chip { --intake-damage-color: #3977bd; }
.intake-damage__type--dent { --intake-damage-color: #8b5da8; }
.intake-damage__type--other { --intake-damage-color: #596773; }
.intake-damage__marker--repositioning { pointer-events: none; }
.intake-damage__hint, .intake-damage__muted { margin: 8px 0 0; color: var(--crm-text-muted, #667085); font-size: 12px; }
.intake-damage__list { display: grid; gap: 6px; max-height: 310px; padding: 0; margin: 0; overflow-y: auto; list-style: none; }
.intake-damage__list button { display: flex; align-items: center; gap: 9px; width: 100%; min-height: 48px; padding: 7px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 6px; background: var(--crm-surface, #fff); color: var(--crm-text, #202833); font: inherit; text-align: left; cursor: pointer; }
.intake-damage__list button:disabled { cursor: default; }
.intake-damage__number { display: grid; place-items: center; flex: 0 0 28px; width: 28px; height: 28px; border-radius: 50%; background: var(--intake-damage-color, var(--crm-accent, #e87532)); color: white; font-weight: 700; }
.intake-damage__list strong, .intake-damage__list small { display: block; }
.intake-damage__list small { margin-top: 3px; color: var(--crm-text-muted, #667085); line-height: 1.4; }
.intake-damage__photo-count { margin-left: auto; color: var(--crm-text-muted, #667085); font-size: 11px; white-space: nowrap; }
.intake-damage__empty { padding: 20px 8px; border: 1px dashed var(--crm-line, #dce2e9); border-radius: 6px; color: var(--crm-text-muted, #667085); text-align: center; font-size: 13px; }
.intake-damage__form { display: grid; gap: 8px; padding: 16px; color: var(--crm-text, #202833); }
.intake-damage__form > select, .intake-damage__form textarea { width: 100%; box-sizing: border-box; border: 1px solid var(--crm-line, #dce2e9); border-radius: 6px; padding: 9px 10px; background: var(--crm-surface, #fff); color: var(--crm-text, #202833); font: inherit; }
.intake-damage__form textarea { resize: vertical; }
.intake-damage__photos { min-width: 0; padding: 10px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 6px; }
.intake-damage__photos { display: flex; flex-wrap: wrap; gap: 8px; }
.intake-damage__photos label:not(.intake-damage__upload) { position: relative; display: block; width: 84px; height: 64px; }
.intake-damage__photos label > input[type="checkbox"] { position: absolute; z-index: 1; top: 4px; left: 4px; width: 18px; height: 18px; accent-color: var(--crm-accent, #e87532); }
.intake-damage__photos img { width: 100%; height: 100%; border-radius: 5px; object-fit: cover; }
.intake-damage__photo-preview { display: grid; gap: 4px; width: 180px; color: var(--crm-accent, #e87532); font-size: 12px; text-align: center; }
.intake-damage__photo-preview img { width: 100%; height: 128px; }
.intake-damage__conflict { padding: 10px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 6px; background: var(--crm-surface-soft, #f7f8fa); }
.intake-damage__conflict p { margin: 0 0 8px; }
.intake-damage__upload { position: relative; display: grid; place-items: center; min-height: 40px; padding: 5px 10px; border: 1px dashed var(--crm-line, #dce2e9); border-radius: 5px; color: var(--crm-text-muted, #667085); font-size: 12px; cursor: pointer; }
.intake-damage__upload input { position: absolute; inset: 0; width: 100%; height: 100%; opacity: 0; cursor: pointer; }
.intake-damage__photos .intake-damage__muted { flex: 1 0 100%; }
.intake-damage__actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-top: 5px; }
.intake-damage__delete { margin-left: auto; border: 0; background: transparent; color: var(--crm-danger-text, #a12622); font: inherit; cursor: pointer; }
.intake-damage__error { padding: 10px; border: 1px solid #cd764f; border-radius: 6px; color: var(--crm-danger-text, #9d3619); font-size: 13px; }
.intake-damage button:focus-visible, .intake-damage__form button:focus-visible, .intake-damage__upload:focus-within { outline: 2px solid var(--crm-accent, #e87532); outline-offset: 2px; }
@media (max-width: 999px) {
  .intake-damage__layout { grid-template-columns: minmax(0, 1fr); }
}
@media (max-width: 760px) {
  .intake-damage { padding: 12px; }
  .intake-damage__map-grid { grid-template-columns: minmax(0, 1fr); }
  .intake-damage__marker { width: 42px; height: 42px; }
  .intake-damage__list { max-height: none; }
}
</style>
