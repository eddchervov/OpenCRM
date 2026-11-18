<script setup lang="ts">
import { useLocalization, formattingCulture } from '@opencrm/ui/i18n'
import '../proOverviewMessages'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton } from '@opencrm/ui'
import type { WorkspaceFeature, WorkspacePlan } from '../api/workspaceAdmin'
import teamImage from '../assets/pro/team.png'
import intakeImage from '../assets/pro/intake.png'
import statisticsImage from '../assets/pro/statistics.png'
import WorkspaceProFeaturePreview from './WorkspaceProFeaturePreview.vue'
import WorkspaceProFeatureDialog from './WorkspaceProFeatureDialog.vue'
import type { ProFeatureKey } from '../proFeatureDetails'

const { t } = useLocalization()

const props = defineProps<{
  open: boolean
  plan: WorkspacePlan | null
  canPurchase: boolean
  isSaving: boolean
}>()

const emit = defineEmits<{
  close: []
  purchasePlan: [plan: WorkspacePlan]
}>()

type ProFeatureCode = 'workspace.maintenance.intake' | 'vehicle.photo_albums' | 'workspace.module.members' | 'workspace.module.analytics' | 'workspace.assistant' | 'workspace.client_messaging' | 'workspace.documents.styling' | 'workspace.data.import' | 'workspace.finance.export_1c'
type OverviewCard = { code: ProFeatureCode, title: string, description: string }
type ScreenshotSection = { code: 'intake' | 'albums' | 'messages' | 'assistant' | 'analytics' | 'team', title: string, description: string, points: string[], outcome: string, preview?: 'albums' | 'messages' | 'assistant', image?: string, alt?: string, viewBox?: string, imageWidth?: number, imageHeight?: number }

const dialog = ref<HTMLDialogElement | null>(null)
const closeButton = ref<HTMLButtonElement | null>(null)
const opener = ref<HTMLElement | null>(null)
const expandedFeature = ref<ProFeatureKey | null>(null)
let closingFromProp = false

const featureCodes = computed(() => new Set((props.plan?.features ?? [])
  .filter((feature: WorkspaceFeature) => feature.boolValue === true)
  .map(feature => feature.code)))
const hasIntake = computed(() => featureCodes.value.has('workspace.maintenance.intake'))
const hasPhotoAlbums = computed(() => featureCodes.value.has('vehicle.photo_albums'))
const screenshotSections = computed<ScreenshotSection[]>(() => {
  const sections: ScreenshotSection[] = []
  if (hasIntake.value) {
    sections.push({
      code: 'intake',
      title: t('proOverview.intakeTitle'),
      description: t('proOverview.intakeDescription'),
      points: [t('proOverview.intakePoint1'), t('proOverview.intakePoint2'), t('proOverview.intakePoint3')],
      outcome: t('proOverview.intakeOutcome'),
      image: intakeImage,
      viewBox: '279 323 619 361', imageWidth: 1265, imageHeight: 712,
      alt: t('proOverview.intakeAlt')
    })
  }
  if (hasPhotoAlbums.value) {
    sections.push({
      code: 'albums',
      title: t('proOverview.albumsTitle'),
      description: t('proOverview.albumsDescription'),
      points: [t('proOverview.albumsPoint1'), t('proOverview.albumsPoint2'), t('proOverview.albumsPoint3')],
      outcome: t('proOverview.albumsOutcome'),
      preview: 'albums'
    })
  }
  if (featureCodes.value.has('workspace.client_messaging')) {
    sections.push({
      code: 'messages',
      title: t('proOverview.messagesTitle'),
      description: t('proOverview.messagesDescription'),
      points: [t('proOverview.messagesPoint1'), t('proOverview.messagesPoint2'), t('proOverview.messagesPoint3'), t('proOverview.messagesPoint4')],
      outcome: t('proOverview.messagesOutcome'),
      preview: 'messages'
    })
  }
  if (featureCodes.value.has('workspace.assistant')) {
    sections.push({
      code: 'assistant',
      title: t('proOverview.assistantTitle'),
      description: t('proOverview.assistantDescription'),
      points: [t('proOverview.assistantPoint1'), t('proOverview.assistantPoint2'), t('proOverview.assistantPoint3'), t('proOverview.assistantPoint4')],
      outcome: t('proOverview.assistantOutcome'),
      preview: 'assistant'
    })
  }
  if (featureCodes.value.has('workspace.module.analytics')) {
    sections.push({
      code: 'analytics',
      title: t('proOverview.analyticsTitle'),
      description: t('proOverview.analyticsDescription'),
      points: [t('proOverview.analyticsPoint1'), t('proOverview.analyticsPoint2'), t('proOverview.analyticsPoint3')],
      outcome: t('proOverview.analyticsOutcome'),
      image: statisticsImage,
      viewBox: '232 66 1033 646', imageWidth: 1265, imageHeight: 712,
      alt: t('proOverview.analyticsAlt')
    })
  }
  if (featureCodes.value.has('workspace.module.members')) {
    sections.push({
      code: 'team',
      title: t('proOverview.teamTitle'),
      description: t('proOverview.teamDescription'),
      points: [t('proOverview.teamPoint1'), t('proOverview.teamPoint2'), t('proOverview.teamPoint3')],
      outcome: t('proOverview.teamOutcome'),
      image: teamImage,
      viewBox: '232 66 1048 590', imageWidth: 1280, imageHeight: 720,
      alt: t('proOverview.teamAlt')
    })
  }
  return sections
})
const compactCards = computed<OverviewCard[]>(() => [
  ...(featureCodes.value.has('workspace.finance.export_1c') ? [{ code: 'workspace.finance.export_1c' as const, title: t('proOverview.accountingTitle'), description: t('proOverview.accountingDescription') }] : []),
  ...(featureCodes.value.has('workspace.documents.styling') ? [{ code: 'workspace.documents.styling' as const, title: t('proOverview.documentsTitle'), description: t('proOverview.documentsDescription') }] : []),
  ...(featureCodes.value.has('workspace.data.import') ? [{ code: 'workspace.data.import' as const, title: t('proOverview.importTitle'), description: t('proOverview.importDescription') }] : [])
])
const quotaItems = computed(() => {
  if (!props.plan) return []
  const items: Array<{ label: string, value: string }> = []
  const clientsLimit = props.plan.features?.find(feature => feature.code === 'workspace.clients.limit')?.intValue
  const membersLimit = props.plan.features?.find(feature => feature.code === 'workspace.members.limit')?.intValue
  if (clientsLimit != null) items.push({ label: t('proOverview.clients'), value: formatLimit(clientsLimit) })
  if (props.plan.vehicleLimit != null) items.push({ label: t('proOverview.vehicles'), value: formatLimit(props.plan.vehicleLimit) })
  if (membersLimit != null) items.push({ label: t('proOverview.members'), value: formatLimit(membersLimit) })
  return items
})
const price = computed(() => props.plan?.effectivePrice ?? props.plan?.pricePerMonth ?? props.plan?.price ?? null)
const formattedPrice = computed(() => price.value == null ? t('proOverview.pricePending') : price.value === 0 ? t('proOverview.free') : new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: 'RUB', currencyDisplay: 'code', minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(price.value))
const formattedOriginalPrice = computed(() => props.plan?.originalEffectivePrice == null ? '' : new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: 'RUB', currencyDisplay: 'code', minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(props.plan.originalEffectivePrice))
const durationText = computed(() => {
  const duration = props.plan?.durationDays
  if (!duration) return ''
  if (duration >= 365) return t('proOverview.yearly')
  if (duration >= 28 && duration <= 31) return t('proOverview.monthly')
  return t('proOverview.duration', { count: duration })
})

function formatLimit(value: number): string {
  return value >= 2147483647 ? t('proOverview.unlimited') : new Intl.NumberFormat(formattingCulture.value).format(value)
}

function syncDialog(isOpen: boolean) {
  const element = dialog.value
  if (!element) return
  if (isOpen) {
    if (element.open) return
    opener.value = document.activeElement instanceof HTMLElement ? document.activeElement : null
    element.showModal()
    void nextTick(() => closeButton.value?.focus())
    return
  }
  if (element.open) {
    closingFromProp = true
    element.close()
  }
}

function closeDialog() {
  expandedFeature.value = null
  emit('close')
}

function openVisual(code: ScreenshotSection['code']) {
  expandedFeature.value = code === 'analytics' ? 'statistics' : code
}

function showFeature(code: string) {
  dialog.value?.querySelector<HTMLElement>(`[data-pro-feature="${code}"]`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

function onDialogClose() {
  if (!closingFromProp) emit('close')
  closingFromProp = false
  if (opener.value?.isConnected) opener.value.focus()
}

function purchasePlan() {
  if (!props.plan || !props.canPurchase || props.isSaving) return
  emit('purchasePlan', props.plan)
}

function closeFromBackdrop(event: MouseEvent) {
  if (event.target === dialog.value) closeDialog()
}

watch(() => props.open, isOpen => {
  if (!isOpen) expandedFeature.value = null
  void nextTick(() => syncDialog(isOpen))
})
onMounted(() => syncDialog(props.open))
onBeforeUnmount(() => {
  closingFromProp = true
  dialog.value?.close()
  if (opener.value?.isConnected) opener.value.focus()
})
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="workspace-pro-overview" aria-labelledby="pro-overview-title" @cancel.prevent="closeDialog" @click="closeFromBackdrop" @close="onDialogClose">
      <header class="workspace-pro-overview__header">
        <div class="workspace-pro-overview__brand"><span class="workspace-pro-overview__spark" aria-hidden="true">✦</span><span>{{ t('proOverview.plan') }}</span></div>
        <button ref="closeButton" class="workspace-pro-overview__close" type="button" :aria-label="t('proOverview.closeLabel')" @click="closeDialog">×</button>
      </header>
      <div class="workspace-pro-overview__body">
        <section class="workspace-pro-overview__hero">
          <span class="workspace-pro-overview__eyebrow">{{ plan?.name ?? 'PRO' }}</span>
          <h2 id="pro-overview-title">{{ t('proOverview.heading') }}</h2>
          <p>{{ t('proOverview.intro', { plan: plan?.name ?? 'PRO' }) }}</p>
          <div v-if="quotaItems.length" class="workspace-pro-overview__quotas" :aria-label="t('proOverview.limits')">
            <div v-for="quota in quotaItems" :key="quota.label"><strong>{{ quota.value }}</strong><span>{{ quota.label }}</span></div>
          </div>
        </section>

        <nav v-if="screenshotSections.length" class="workspace-pro-overview__navigation" :aria-label="t('proOverview.proFeatures')">
          <button v-for="section in screenshotSections" :key="section.code" type="button" @click="showFeature(section.code)">{{ ({ intake: t('proOverview.intake'), albums: t('proOverview.albums'), messages: t('proOverview.messages'), assistant: t('proOverview.assistant'), analytics: t('proOverview.analytics'), team: t('proOverview.team') })[section.code] }}</button>
        </nav>
        <section v-if="open && screenshotSections.length" class="workspace-pro-overview__features" :aria-label="t('proOverview.planFeatures')">
          <article v-for="section in screenshotSections" :key="section.code" class="workspace-pro-overview__feature" :data-pro-feature="section.code">
            <div class="workspace-pro-overview__visual">
              <button class="workspace-pro-overview__enlarge" type="button" :aria-label="t('proOverview.enlargeLabel', { title: section.title })" @click="openVisual(section.code)">
                <span class="workspace-pro-overview__enlarge-label">{{ t('proOverview.enlarge') }}</span>
                <WorkspaceProFeaturePreview v-if="section.preview" :kind="section.preview" />
                <svg v-else :viewBox="section.viewBox" role="img" :aria-label="section.alt" class="workspace-pro-overview__screenshot">
                  <image :href="section.image" :width="section.imageWidth" :height="section.imageHeight" />
                </svg>
                <span v-if="section.preview" class="workspace-pro-overview__visual-note">{{ t('proOverview.demo') }}</span>
              </button>
            </div>
            <div class="workspace-pro-overview__description"><span class="workspace-pro-overview__feature-label">{{ t('proOverview.feature') }}</span><h3>{{ section.title }}</h3><p>{{ section.description }}</p><ul><li v-for="point in section.points" :key="point">{{ point }}</li></ul><p class="workspace-pro-overview__outcome">{{ section.outcome }}</p></div>
          </article>
        </section>
        <p v-if="screenshotSections.length" class="workspace-pro-overview__example-note">{{ t('proOverview.exampleNote') }}</p>

        <section v-if="compactCards.length" class="workspace-pro-overview__compact" :aria-label="t('proOverview.otherFeatures')">
          <article v-for="card in compactCards" :key="card.code"><span aria-hidden="true">✦</span><div><h3>{{ card.title }}</h3><p>{{ card.description }}</p></div></article>
        </section>
        <p v-if="!screenshotSections.length && !compactCards.length" class="workspace-pro-overview__empty">{{ t('proOverview.noFeatures') }}</p>
      </div>

      <footer class="workspace-pro-overview__footer">
        <div class="workspace-pro-overview__price"><strong>{{ formattedPrice }}</strong><span v-if="durationText">{{ durationText }}</span><s v-if="plan?.hasPromoDiscount && formattedOriginalPrice">{{ formattedOriginalPrice }}</s></div>
        <div class="workspace-pro-overview__actions"><UiButton variant="secondary" @click="closeDialog">{{ t('proOverview.close') }}</UiButton><UiButton :disabled="!canPurchase || isSaving || !plan" @click="purchasePlan">{{ isSaving ? t('proOverview.waiting') : t('proOverview.activate') }}</UiButton></div>
      </footer>
    </dialog>
  </Teleport>
  <WorkspaceProFeatureDialog v-if="open && expandedFeature" :open="true" :feature="expandedFeature" :can-manage-billing="false" visual-only @close="expandedFeature = null" />
</template>

<style scoped>
.workspace-pro-overview { width: min(980px, calc(100vw - 28px)); max-height: min(92dvh, 900px); margin: auto; padding: 0; overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 18px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); box-shadow: 0 28px 100px #10182855; }
.workspace-pro-overview[open] { display: flex; flex-direction: column; }
.workspace-pro-overview::backdrop { background: #101828a8; backdrop-filter: blur(5px); }
.workspace-pro-overview__header { display: flex; align-items: center; justify-content: space-between; padding: 16px 24px; border-bottom: 1px solid var(--crm-line, #dce2e9); }
.workspace-pro-overview__brand { display: flex; align-items: center; gap: 9px; font-size: 13px; font-weight: 700; letter-spacing: .02em; }
.workspace-pro-overview__spark { color: #e87532; font-size: 20px; }
.workspace-pro-overview__close { width: 36px; height: 36px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); font-size: 24px; line-height: 1; cursor: pointer; }
.workspace-pro-overview__body { min-height: 0; overflow: auto; overscroll-behavior: contain; padding: 26px 30px 32px; }
.workspace-pro-overview__example-note { margin: 12px 0 0; color: var(--crm-text-muted, #687386); font-size: 11px; }
.workspace-pro-overview__hero { position: relative; overflow: hidden; padding: 24px 28px; border: 1px solid color-mix(in srgb, #e87532 25%, var(--crm-line, #dce2e9)); border-radius: 16px; background: radial-gradient(circle at 90% 20%, color-mix(in srgb, #e87532 20%, transparent), transparent 36%), linear-gradient(135deg, var(--crm-surface-soft, #fff8f2), var(--crm-surface, #fff)); }
.workspace-pro-overview__eyebrow, .workspace-pro-overview__feature-label { color: #c8561b; font-size: 11px; font-weight: 800; letter-spacing: .09em; text-transform: uppercase; }
.workspace-pro-overview__hero h2 { margin: 10px 0 8px; font-size: clamp(26px, 3vw, 32px); line-height: 1.12; letter-spacing: -.035em; }
.workspace-pro-overview__hero > p { margin: 0; color: var(--crm-text-muted, #687386); font-size: 14px; line-height: 1.55; }
.workspace-pro-overview__quotas { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 18px; }
.workspace-pro-overview__quotas > div { display: grid; gap: 3px; min-width: 120px; padding: 8px 12px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; background: color-mix(in srgb, var(--crm-surface, #fff) 85%, transparent); }
.workspace-pro-overview__quotas strong { font-size: 16px; }
.workspace-pro-overview__quotas span { color: var(--crm-text-muted, #687386); font-size: 11px; }
.workspace-pro-overview__features { display: grid; gap: 16px; margin-top: 22px; }
.workspace-pro-overview__navigation { display: flex; flex-wrap: wrap; gap: 7px; margin-top: 18px; }
.workspace-pro-overview__navigation button { padding: 7px 11px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); font: inherit; font-size: 12px; cursor: pointer; }
.workspace-pro-overview__navigation button:hover { border-color: var(--crm-accent, #e87532); color: var(--crm-accent, #c8561b); }
.workspace-pro-overview__navigation button:focus-visible { outline: 2px solid var(--crm-accent, #e87532); outline-offset: 2px; }
.workspace-pro-overview__feature { display: grid; grid-template-columns: minmax(0, 1.3fr) minmax(220px, .7fr); gap: 24px; align-items: center; padding: 14px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 14px; background: var(--crm-surface, #fff); }
.workspace-pro-overview__feature:nth-child(even) { grid-template-columns: minmax(220px, .7fr) minmax(0, 1.3fr); }
.workspace-pro-overview__feature { scroll-margin-top: 12px; }
.workspace-pro-overview__visual { min-width: 0; }
.workspace-pro-overview__enlarge { display: block; width: 100%; padding: 0; border: 0; background: transparent; color: inherit; font: inherit; text-align: start; cursor: zoom-in; }
.workspace-pro-overview__enlarge:focus-visible { outline: 2px solid var(--crm-accent, #e87532); outline-offset: 3px; }
.workspace-pro-overview__enlarge-label { display: block; margin-bottom: 7px; color: var(--crm-action-accent, #c8561b); font-size: 12px; text-align: right; }
.workspace-pro-overview__visual-note { display: block; margin-top: 7px; color: var(--crm-text-muted, #687386); font-size: 10px; text-align: center; }
.workspace-pro-overview__feature:nth-child(even) .workspace-pro-overview__visual { order: 2; }
.workspace-pro-overview__description { padding: 8px 6px; }
.workspace-pro-overview__description ul { display: grid; gap: 9px; margin: 16px 0; padding: 0; list-style: none; font-size: 12px; line-height: 1.5; }
.workspace-pro-overview__description li { position: relative; padding-left: 17px; }
.workspace-pro-overview__description li::before { position: absolute; left: 0; top: .47em; width: 6px; height: 6px; border-radius: 50%; background: var(--crm-accent, #e87532); content: ''; }
.workspace-pro-overview__description .workspace-pro-overview__outcome { padding-top: 12px; border-top: 1px solid var(--crm-line, #dce2e9); color: var(--crm-text, #202631); font-size: 12px; font-weight: 600; }
.workspace-pro-overview__screenshot { display: block; width: 100%; height: auto; aspect-ratio: 16 / 9; overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 9px; background: var(--crm-surface-soft, #f6f7f9); }
.workspace-pro-overview__feature h3, .workspace-pro-overview__compact h3 { margin: 7px 0 6px; font-size: 18px; }
.workspace-pro-overview__feature p, .workspace-pro-overview__compact p { margin: 0; color: var(--crm-text-muted, #687386); font-size: 13px; line-height: 1.55; }
.workspace-pro-overview__compact { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; margin-top: 18px; }
.workspace-pro-overview__compact article { display: flex; align-items: start; gap: 12px; padding: 16px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 12px; background: var(--crm-surface, #fff); }
.workspace-pro-overview__compact article > span { color: #e87532; font-size: 18px; }
.workspace-pro-overview__compact h3 { margin-top: 0; font-size: 14px; }
.workspace-pro-overview__empty { padding: 20px; color: var(--crm-text-muted, #687386); text-align: center; }
.workspace-pro-overview__footer { position: sticky; bottom: 0; display: flex; align-items: center; justify-content: space-between; gap: 20px; padding: 14px 24px; border-top: 1px solid var(--crm-line, #dce2e9); background: var(--crm-surface, #fff); box-shadow: 0 -10px 24px #1018280b; }
.workspace-pro-overview__price { display: flex; align-items: baseline; flex-wrap: wrap; gap: 8px; }
.workspace-pro-overview__price strong { font-size: 22px; }
.workspace-pro-overview__price span, .workspace-pro-overview__price s { color: var(--crm-text-muted, #687386); font-size: 12px; }
.workspace-pro-overview__actions { display: flex; justify-content: flex-end; gap: 10px; }
.workspace-pro-overview__actions :deep(.ui-button--primary) { background: #e87532; border-color: #e87532; color: #fff; font-weight: 700; }
@media (max-width: 680px) {
  .workspace-pro-overview { width: calc(100vw - 16px); max-height: 95dvh; border-radius: 14px; }
  .workspace-pro-overview__header { padding: 12px 16px; }
  .workspace-pro-overview__body { padding: 16px 14px 20px; }
  .workspace-pro-overview__hero { padding: 20px; }
  .workspace-pro-overview__quotas { gap: 6px; }
  .workspace-pro-overview__quotas > div { min-width: 0; flex: 1 1 110px; padding: 8px; }
  .workspace-pro-overview__quotas strong { font-size: 14px; }
  .workspace-pro-overview__quotas span { font-size: 10px; }
  .workspace-pro-overview__feature, .workspace-pro-overview__feature:nth-child(even) { grid-template-columns: 1fr; gap: 12px; padding: 10px; }
  .workspace-pro-overview__feature:nth-child(even) .workspace-pro-overview__visual { order: 0; }
  .workspace-pro-overview__feature h3 { font-size: 16px; }
  .workspace-pro-overview__footer { align-items: stretch; flex-direction: column; gap: 10px; padding: 12px 14px max(12px, env(safe-area-inset-bottom)); }
  .workspace-pro-overview__actions { display: grid; grid-template-columns: 1fr 1.5fr; }
  .workspace-pro-overview__actions :deep(button) { width: 100%; }
}
</style>
