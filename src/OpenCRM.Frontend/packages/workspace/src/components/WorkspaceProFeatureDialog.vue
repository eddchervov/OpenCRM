<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import { UiButton } from '@opencrm/ui'
import type { ProFeatureKey } from '../proFeatureDetails'
import { proFeatureDetails } from '../proFeatureDetails'
import WorkspaceProTeamLanding from './WorkspaceProTeamLanding.vue'
import WorkspaceProFeatureLanding from './WorkspaceProFeatureLanding.vue'
import WorkspaceProFeatureVisual from './WorkspaceProFeatureVisual.vue'

const props = defineProps<{
  open: boolean
  feature: ProFeatureKey
  canManageBilling: boolean
  visualOnly?: boolean
}>()

const emit = defineEmits<{
  close: []
}>()

const dialog = ref<HTMLDialogElement | null>(null)
const closeButton = ref<HTMLButtonElement | null>(null)
const backButton = ref<HTMLButtonElement | null>(null)
const opener = ref<HTMLElement | null>(null)
const isExpanded = ref(props.visualOnly === true)
const zoomScale = ref(1)
const landingPreview = ref<{ src: string; alt: string } | null>(null)
const landingPreviewOpener = ref<HTMLElement | null>(null)
const isTeamLanding = computed(() => props.feature === 'team' || props.feature === 'roles')
const titleId = `pro-feature-${useId()}`
const detail = computed(() => proFeatureDetails[props.feature])
const otherFeature = computed(() => props.feature === 'team' || props.feature === 'roles' ? null : props.feature)
let closingFromProp = false
let previousBodyOverflow = ''

function openDialog() {
  const element = dialog.value
  if (!element || element.open) return

  opener.value = document.activeElement instanceof HTMLElement ? document.activeElement : null
  previousBodyOverflow = document.body.style.overflow
  document.body.style.overflow = 'hidden'
  element.showModal()
  void nextTick(() => closeButton.value?.focus())
}

function closeDialog() {
  if (isExpanded.value) {
    returnToDescription()
    return
  }

  emit('close')
}

function expandVisual() {
  if (typeof document !== 'undefined') {
    landingPreviewOpener.value = document.activeElement instanceof HTMLElement ? document.activeElement : null
  }
  isExpanded.value = true
  void nextTick(() => backButton.value?.focus())
}

function expandLandingImage(image: { src: string; alt: string }) {
  landingPreview.value = image
  expandVisual()
}

function returnToDescription() {
  if (!isExpanded.value) return
  if (props.visualOnly) {
    emit('close')
    return
  }

  isExpanded.value = false
  zoomScale.value = 1
  landingPreview.value = null
  void nextTick(() => landingPreviewOpener.value?.focus())
}

function onDialogCancel(event: Event) {
  event.preventDefault()
  if (isExpanded.value) {
    returnToDescription()
    return
  }

  emit('close')
}

function increaseZoom() {
  zoomScale.value = Math.min(3, Math.round((zoomScale.value + 0.25) * 100) / 100)
}

function decreaseZoom() {
  zoomScale.value = Math.max(1, Math.round((zoomScale.value - 0.25) * 100) / 100)
}

function resetZoom() {
  zoomScale.value = 1
}

function trapFocus(event: KeyboardEvent) {
  if (event.key !== 'Tab' || !dialog.value) return
  const controls = dialog.value.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], [tabindex="0"]')
  const visibleControls = Array.from(controls).filter(control => control.getClientRects().length > 0)
  const first = visibleControls[0]
  const last = visibleControls[visibleControls.length - 1]
  const active = dialog.value.ownerDocument.activeElement
  if (event.shiftKey && active === first && last) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && active === last && first) {
    event.preventDefault()
    first.focus()
  }
}

function closeFromProp() {
  if (!dialog.value?.open) return

  closingFromProp = true
  dialog.value.close()
}

function onDialogClose() {
  document.body.style.overflow = previousBodyOverflow
  if (!closingFromProp) emit('close')
  closingFromProp = false
  if (opener.value?.isConnected) opener.value.focus()
}

function closeFromBackdrop(event: MouseEvent) {
  if (event.target === dialog.value) closeDialog()
}

function onBillingClick() {
  closeDialog()
}

watch(() => props.open, isOpen => {
  void nextTick(() => isOpen ? openDialog() : closeFromProp())
})

onMounted(() => {
  if (props.open) openDialog()
})

onBeforeUnmount(() => {
  closingFromProp = true
  if (dialog.value?.open) dialog.value.close()
  document.body.style.overflow = previousBodyOverflow
  if (opener.value?.isConnected) opener.value.focus()
})

</script>

<template>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="workspace-pro-feature-dialog"
      :class="{ 'workspace-pro-feature-dialog--expanded': isExpanded, 'workspace-pro-feature-dialog--landing': !isExpanded }"
      :aria-labelledby="!isExpanded ? titleId : undefined"
      :aria-label="isExpanded ? `Увеличенный пример: ${detail.title}` : undefined"
      @cancel="onDialogCancel"
      @keydown="trapFocus"
      @click="closeFromBackdrop"
      @close="onDialogClose"
    >
      <header class="workspace-pro-feature-dialog__header">
        <span class="workspace-pro-feature-dialog__eyebrow">Возможность PRO</span>
        <button ref="closeButton" class="workspace-pro-feature-dialog__close" type="button" :aria-label="isExpanded ? 'Вернуться к описанию' : 'Закрыть'" @click="closeDialog">×</button>
      </header>

      <div class="workspace-pro-feature-dialog__content">
        <WorkspaceProTeamLanding
          v-if="!visualOnly && isTeamLanding"
          v-show="!isExpanded"
          :title-id="titleId"
          :title="detail.title"
          :can-manage-billing="canManageBilling"
          @preview="expandLandingImage"
          @billing="onBillingClick"
        />
        <WorkspaceProFeatureLanding
          v-if="!visualOnly && otherFeature"
          v-show="!isExpanded"
          :feature="otherFeature"
          :title-id="titleId"
          :can-manage-billing="canManageBilling"
          @preview="expandVisual"
          @billing="onBillingClick"
        />

        <div v-if="isExpanded" class="workspace-pro-feature-dialog__zoom-toolbar" aria-label="Масштаб примера">
          <button ref="backButton" type="button" class="workspace-pro-feature-dialog__zoom-back" @click="returnToDescription">← К описанию</button>
          <div class="workspace-pro-feature-dialog__zoom-controls">
            <button type="button" aria-label="Уменьшить пример" :disabled="zoomScale <= 1" @click="decreaseZoom">−</button>
            <span aria-live="polite">{{ Math.round(zoomScale * 100) }}%</span>
            <button type="button" aria-label="Увеличить пример" :disabled="zoomScale >= 3" @click="increaseZoom">+</button>
            <button type="button" class="workspace-pro-feature-dialog__zoom-fit" :disabled="zoomScale === 1" @click="resetZoom">По размеру окна</button>
          </div>
        </div>

        <div v-if="isExpanded" class="workspace-pro-feature-dialog__layout" :class="{ 'workspace-pro-feature-dialog__layout--expanded': isExpanded }">
          <section class="workspace-pro-feature-dialog__visual" :class="{ 'workspace-pro-feature-dialog__visual--expanded': isExpanded }" aria-label="Пример возможности">
            <div class="workspace-pro-feature-dialog__zoom-viewport" tabindex="0" aria-label="Увеличенный пример; прокручивайте, чтобы посмотреть детали">
              <div class="workspace-pro-feature-dialog__zoom-canvas" :style="{ width: `${zoomScale * 100}%`, zoom: zoomScale }">
                <img v-if="landingPreview" :src="landingPreview.src" :alt="landingPreview.alt" class="workspace-pro-feature-dialog__landing-image">
                <WorkspaceProFeatureVisual v-else :feature="feature" />
              </div>
            </div>
            <span class="workspace-pro-feature-dialog__visual-note">Демонстрационный пример</span>
          </section>
        </div>

        <p v-if="!isExpanded && !canManageBilling" class="workspace-pro-feature-dialog__notice">
          Подключение PRO доступно администратору тарифов организации.
        </p>
      </div>

      <footer v-if="!isExpanded" class="workspace-pro-feature-dialog__footer">
        <UiButton variant="secondary" @click="closeDialog">Закрыть</UiButton>
        <UiButton v-if="canManageBilling" href="#billing" @click="onBillingClick">Перейти к тарифам</UiButton>
      </footer>
    </dialog>
  </Teleport>
</template>

<style scoped>
.workspace-pro-feature-dialog { width: min(960px, calc(100vw - 32px)); max-width: none; max-height: min(90vh, 900px); padding: 0; overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 16px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); box-shadow: 0 24px 80px rgb(15 23 42 / 28%); }
.workspace-pro-feature-dialog--landing { width: min(1120px, calc(100vw - 32px)); }
.workspace-pro-feature-dialog__landing-image { display: block; width: 100%; height: auto; }
.workspace-pro-feature-dialog--expanded { width: min(1400px, calc(100vw - 20px)); max-height: 95vh; }
.workspace-pro-feature-dialog[open] { display: flex; flex-direction: column; }
.workspace-pro-feature-dialog::backdrop { background: rgb(12 17 25 / 62%); backdrop-filter: blur(3px); }
.workspace-pro-feature-dialog__header { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 14px 20px; border-bottom: 1px solid var(--crm-line, #dce2e9); background: var(--crm-surface-soft, #f6f7f9); }
.workspace-pro-feature-dialog__eyebrow { color: var(--crm-action-accent, #c8561b); font-size: 12px; font-weight: 700; }
.workspace-pro-feature-dialog__close { display: grid; width: 36px; height: 36px; place-items: center; border: 1px solid var(--crm-line, #dce2e9); border-radius: 9px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); cursor: pointer; font-size: 24px; line-height: 1; }
.workspace-pro-feature-dialog__close:focus-visible { outline: 2px solid var(--crm-accent, #e87532); outline-offset: 2px; }
.workspace-pro-feature-dialog__content { flex: 1 1 auto; min-height: 0; padding: 24px; overflow: auto; }
.workspace-pro-feature-dialog__header, .workspace-pro-feature-dialog__footer { flex-shrink: 0; }
.workspace-pro-feature-dialog__layout { display: grid; grid-template-columns: minmax(0, 1.15fr) minmax(240px, .85fr); align-items: start; gap: 24px; }
.workspace-pro-feature-dialog__visual { min-width: 0; }
.workspace-pro-feature-dialog__visual--expanded { display: flex; min-height: 0; flex: 1 1 auto; flex-direction: column; }
.workspace-pro-feature-dialog__zoom-toolbar { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px; margin-bottom: 12px; }
.workspace-pro-feature-dialog__zoom-toolbar button { min-height: 38px; padding: 7px 11px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); cursor: pointer; font: inherit; }
.workspace-pro-feature-dialog__zoom-toolbar button:disabled { opacity: .5; cursor: default; }
.workspace-pro-feature-dialog__zoom-toolbar button:focus-visible, .workspace-pro-feature-dialog__zoom-viewport:focus-visible { outline: 2px solid var(--crm-action-accent, #c8561b); outline-offset: 2px; }
.workspace-pro-feature-dialog__zoom-back { font-weight: 700 !important; }
.workspace-pro-feature-dialog__zoom-controls { display: flex; align-items: center; gap: 7px; }
.workspace-pro-feature-dialog__zoom-controls > span { min-width: 50px; color: var(--crm-text-muted, #687386); font-size: 12px; text-align: center; }
.workspace-pro-feature-dialog__zoom-controls .workspace-pro-feature-dialog__zoom-fit { margin-left: 5px; }
.workspace-pro-feature-dialog__layout--expanded { display: flex; width: 100%; min-height: 0; flex: 1 1 auto; flex-direction: column; align-items: stretch; gap: 0; }
.workspace-pro-feature-dialog__visual--expanded { width: 100%; }
.workspace-pro-feature-dialog__zoom-viewport { box-sizing: border-box; width: 100%; min-height: 0; flex: 1 1 auto; overflow: auto; border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; background: var(--crm-surface-soft, #f6f7f9); overscroll-behavior: contain; }
.workspace-pro-feature-dialog__zoom-canvas { min-width: 100%; transform-origin: top left; }
.workspace-pro-feature-dialog--expanded .workspace-pro-feature-dialog__content { display: flex; flex-direction: column; overflow: hidden; padding: 14px; }
.workspace-pro-feature-dialog__visual-note { display: block; margin-top: 7px; color: var(--crm-text-muted, #687386); font-size: 11px; text-align: center; }
.workspace-pro-feature-dialog__notice { margin: 18px 0 0; padding: 12px 14px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 9px; color: var(--crm-text-muted, #687386); font-size: 13px; }
.workspace-pro-feature-dialog__footer { display: flex; justify-content: flex-end; gap: 10px; padding: 14px 20px; border-top: 1px solid var(--crm-line, #dce2e9); background: var(--crm-surface-soft, #f6f7f9); }
.workspace-pro-feature-dialog__footer :deep(.ui-button--primary) { background: var(--crm-action-accent, #b94a20); border-color: var(--crm-action-accent, #b94a20); color: #fff; }
.workspace-pro-feature-dialog__footer :deep(.ui-button--primary:hover), .workspace-pro-feature-dialog__footer :deep(.ui-button--primary:focus) { background: var(--crm-action-accent, #b94a20); border-color: var(--crm-action-accent, #b94a20); }
:global(:root[data-crm-theme="dark"] .workspace-pro-feature-dialog__footer .ui-button--primary) { color: var(--crm-canvas, #12161d); }
@media (max-width: 600px) {
  .workspace-pro-feature-dialog { width: calc(100vw - 20px); max-height: 94vh; border-radius: 13px; }
  .workspace-pro-feature-dialog__content { padding: 18px 14px; }
  .workspace-pro-feature-dialog__layout { grid-template-columns: minmax(0, 1fr); gap: 14px; }
  .workspace-pro-feature-dialog__footer { padding: 12px 14px; }
  .workspace-pro-feature-dialog--expanded .workspace-pro-feature-dialog__content { padding: 10px; }
  .workspace-pro-feature-dialog__zoom-toolbar { align-items: stretch; }
  .workspace-pro-feature-dialog__zoom-controls { justify-content: space-between; }
  .workspace-pro-feature-dialog__zoom-controls .workspace-pro-feature-dialog__zoom-fit { margin-left: auto; }
  .workspace-pro-feature-dialog__zoom-viewport { min-height: 0; }
}
</style>
