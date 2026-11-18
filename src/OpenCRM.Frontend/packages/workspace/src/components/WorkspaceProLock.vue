<script setup lang="ts">
import { translate as t } from '@opencrm/ui/i18n'
import '../workspaceChromeMessages'
import '../interactionMessages'
import { computed, inject, ref, watch } from 'vue'
import { commercialModuleEnabledKey, proFeatureBillingAccessKey } from '../proFeatureAccess'
import type { ProFeatureKey } from '../proFeatureDetails'
import WorkspaceProFeatureDialog from './WorkspaceProFeatureDialog.vue'

const props = defineProps<{
  locked: boolean
  message: string
  feature?: ProFeatureKey
  compact?: boolean
  centered?: boolean
}>()
const detailsOpen = ref(false)
const commercialEnabled = inject(commercialModuleEnabledKey, computed(() => false))
const canManageBilling = inject(proFeatureBillingAccessKey, computed(() => false))
watch(() => [props.locked, props.feature], () => { detailsOpen.value = false })
</script>

<template>
  <div class="workspace-pro-lock" :class="{ 'workspace-pro-lock--locked': locked, 'workspace-pro-lock--compact': compact, 'workspace-pro-lock--centered': centered }">
    <div class="workspace-pro-lock__content" :inert="locked" :aria-hidden="locked ? true : undefined">
      <slot />
    </div>
    <div v-if="locked" class="workspace-pro-lock__overlay">
      <div class="workspace-pro-lock__notice">
        <div class="workspace-pro-lock__heading">
          <svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><rect x="5" y="10" width="14" height="11" rx="3" /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v3" /></svg>
          <span>{{ commercialEnabled ? message : t('workspaceChrome.featureDisabled') }}</span>
        </div>
        <button v-if="commercialEnabled && feature" class="workspace-pro-lock__details" type="button" aria-haspopup="dialog" :aria-expanded="detailsOpen" @click="detailsOpen = true">{{ t('interaction.viewFeatures') }} <span aria-hidden="true">→</span></button>
      </div>
    </div>
    <WorkspaceProFeatureDialog v-if="commercialEnabled && locked && feature && detailsOpen" :open="detailsOpen" :feature="feature" :can-manage-billing="canManageBilling" @close="detailsOpen = false" />
  </div>
</template>

<style scoped>
.workspace-pro-lock { min-width: 0; position: relative; }
.workspace-pro-lock__content { min-width: 0; }
.workspace-pro-lock--locked > .workspace-pro-lock__content {
  filter: grayscale(1) blur(1px);
  opacity: .6;
  pointer-events: none;
  user-select: none;
}
.workspace-pro-lock__overlay {
  position: absolute;
  inset: 0;
  z-index: 2;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: clamp(76px, 12vw, 150px) 16px 24px;
  background: linear-gradient(180deg, transparent, color-mix(in srgb, var(--crm-surface, #fff) 18%, transparent));
}
.workspace-pro-lock__notice {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 14px;
  max-width: 100%;
  min-height: 76px;
  padding: 16px 20px;
  border: 1px solid color-mix(in srgb, var(--crm-accent, #f47735) 28%, var(--crm-surface, #fff));
  border-radius: 18px;
  background: var(--crm-surface, #fff);
  box-shadow: 0 14px 40px rgb(15 23 42 / .14), 0 0 0 4px color-mix(in srgb, var(--crm-surface, #fff) 60%, transparent);
  color: var(--crm-text, #172536);
  font-size: 16px;
  font-weight: 600;
  text-align: left;
  text-decoration: none;
  transition: border-color .15s ease, box-shadow .15s ease;
}
.workspace-pro-lock__heading { display: flex; align-items: center; gap: 14px; }
.workspace-pro-lock__details { padding: 0; border: 0; background: none; color: var(--crm-action-accent, #bd4b12); font: inherit; font-size: 13px; cursor: pointer; text-decoration: underline; text-underline-offset: 3px; }
.workspace-pro-lock__details:focus-visible { outline: 2px solid var(--crm-accent, #d75b18); outline-offset: 4px; }
.workspace-pro-lock__notice svg { width: 18px; height: 18px; flex-shrink: 0; stroke: currentColor; stroke-width: 1.6; stroke-linecap: round; stroke-linejoin: round; }
.workspace-pro-lock__notice svg:first-child { box-sizing: content-box; width: 20px; height: 20px; padding: 12px; border-radius: 12px; color: var(--crm-accent, #f47735); background: color-mix(in srgb, var(--crm-accent, #f47735) 11%, transparent); }
.workspace-pro-lock--centered > .workspace-pro-lock__overlay { align-items: center; padding: 16px; }
.workspace-pro-lock--compact > .workspace-pro-lock__overlay { align-items: center; padding: 6px; background: color-mix(in srgb, var(--crm-surface, #fff) 28%, transparent); }
.workspace-pro-lock--locked.workspace-pro-lock--compact { min-height: 52px; }
.workspace-pro-lock--compact .workspace-pro-lock__notice { min-height: 34px; padding: 6px 10px; gap: 7px; border-radius: 8px; font-size: 12px; text-align: center; box-shadow: 0 3px 10px rgb(15 23 42 / .12); }
.workspace-pro-lock--compact .workspace-pro-lock__notice { flex-direction: row; flex-wrap: wrap; justify-content: center; }
.workspace-pro-lock--compact .workspace-pro-lock__notice svg:first-child { box-sizing: border-box; width: 14px; height: 14px; padding: 0; background: none; }
.workspace-pro-lock--compact .workspace-pro-lock__heading { gap: 7px; }
.workspace-pro-lock--compact .workspace-pro-lock__details { font-size: 11px; }
@media (max-width: 600px) {
  .workspace-pro-lock__overlay { padding: 90px 12px 20px; }
  .workspace-pro-lock__notice { font-size: 15px; padding: 14px; gap: 12px; border-radius: 16px; }
  .workspace-pro-lock__notice svg:first-child { padding: 10px; }
  .workspace-pro-lock--locked.workspace-pro-lock--compact { min-height: 76px; }
}
</style>
