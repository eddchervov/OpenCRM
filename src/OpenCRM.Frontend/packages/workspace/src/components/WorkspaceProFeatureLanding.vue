<script setup lang="ts">
import { getProjectBrand } from '@opencrm/ui'
import { computed } from 'vue'
import { UiButton } from '@opencrm/ui'
import type { ProFeatureKey } from '../proFeatureDetails'
import { proFeatureDetails } from '../proFeatureDetails'
import { proFeatureLandingContent } from '../proFeatureLandingContent'
import WorkspaceProFeatureVisual from './WorkspaceProFeatureVisual.vue'

const projectBrand = getProjectBrand()








const props = defineProps<{
  feature: Exclude<ProFeatureKey, 'team' | 'roles'>
  titleId: string
  canManageBilling: boolean
}>()
const emit = defineEmits<{ preview: []; billing: [] }>()
const detail = computed(() => proFeatureDetails[props.feature])
const content = computed(() => proFeatureLandingContent[props.feature])
</script>

<template>
  <div class="pro-feature-landing" :class="`pro-feature-landing--${feature}`">
    <section class="pro-feature-landing__hero">
      <div class="pro-feature-landing__pitch">
        <div class="pro-feature-landing__category"><span>{{ detail.title }} в PRO</span><span v-if="detail.beta" class="pro-feature-landing__beta">БЕТА</span></div>
        <h2 :id="titleId"><span v-for="line in content.headline" :key="line">{{ line }}</span></h2>
        <p>{{ detail.description }}</p>
        <UiButton v-if="canManageBilling" href="#billing" @click="emit('billing')">Выбрать PRO →</UiButton>
        <p v-if="detail.beta" class="pro-feature-landing__beta-note">Бета-версия: возможности дорабатываются.</p>
      </div>
      <div class="pro-feature-landing__stage">
        <span class="pro-feature-landing__tags">{{ content.tags }}</span>
        <button type="button" class="pro-feature-landing__visual" :aria-label="`Увеличить пример: ${detail.title}`" @click="emit('preview')">
          <span class="pro-feature-landing__window" aria-hidden="true"><i></i><i></i><i></i><span>{{ projectBrand }}</span><b>↗</b></span>
          <span class="pro-feature-landing__example"><WorkspaceProFeatureVisual :feature="feature" /></span>
          <span class="pro-feature-landing__enlarge">Увеличить пример ↗</span>
        </button>
        <span class="pro-feature-landing__caption">Демонстрационный пример</span>
      </div>
    </section>
    <section class="pro-feature-landing__benefits" :class="{ 'pro-feature-landing__benefits--four': detail.points.length === 4 }" aria-label="Возможности раздела">
      <article v-for="(point, index) in detail.points" :key="point">
        <span class="pro-feature-landing__number">0{{ index + 1 }}</span>
        <h3>{{ content.benefitTitles[index] }}</h3><p>{{ point }}</p>
      </article>
    </section>
    <section class="pro-feature-landing__workflow" aria-label="Как это работает">
      <div class="pro-feature-landing__workflow-heading"><span>В ВАШЕЙ РАБОТЕ</span><h3>От первого шага<br>до результата</h3></div>
      <ol>
        <li v-for="(step, index) in content.steps" :key="step.title"><span class="pro-feature-landing__step">{{ index + 1 }}</span><div><h4>{{ step.title }}</h4><p>{{ step.text }}</p></div></li>
      </ol>
    </section>
    <p v-if="detail.note" class="pro-feature-landing__note">{{ detail.note }}</p>
  </div>
</template>

<style scoped>
.pro-feature-landing { container-type: inline-size; }
.pro-feature-landing__hero { display: grid; grid-template-columns: minmax(0, .9fr) minmax(0, 1.1fr); gap: 30px; align-items: center; }
.pro-feature-landing__pitch { padding: 12px 0; }
.pro-feature-landing__category { display: flex; align-items: center; flex-wrap: wrap; gap: 9px; color: var(--crm-action-accent, #b94a20); font-size: 11px; font-weight: 800; letter-spacing: .06em; }
.pro-feature-landing__beta { padding: 3px 6px; border: 1px solid color-mix(in srgb, var(--crm-action-accent, #b94a20) 40%, var(--crm-line, #dce2e9)); border-radius: 5px; font-size: 9px; }
.pro-feature-landing h2 { margin: 16px 0; font-size: clamp(28px, 3.7cqi, 40px); line-height: 1.1; letter-spacing: -.03em; overflow-wrap: anywhere; }
.pro-feature-landing h2 > span { display: block; }
.pro-feature-landing p { color: var(--crm-text-muted, #687386); font-size: 14px; line-height: 1.65; }
.pro-feature-landing__pitch > p { margin: 0 0 22px; }
.pro-feature-landing__pitch :deep(.ui-button--primary) { background: var(--crm-action-accent, #b94a20); border-color: var(--crm-action-accent, #b94a20); color: #fff; }
:global(:root[data-crm-theme="dark"] .pro-feature-landing__pitch .ui-button--primary) { color: var(--crm-canvas, #12161d); }
.pro-feature-landing__pitch > .pro-feature-landing__beta-note { margin: 12px 0 0; font-size: 11px; }
.pro-feature-landing__stage { min-width: 0; padding: 18px; border: 1px solid color-mix(in srgb, var(--crm-action-accent, #b94a20) 15%, var(--crm-line, #dce2e9)); border-radius: 16px; background: radial-gradient(ellipse at top right, color-mix(in srgb, var(--crm-action-accent, #b94a20) 14%, transparent), transparent 70%), var(--crm-surface-soft, #f6f7f9); }
.pro-feature-landing__tags { display: block; margin: 0 0 12px; color: var(--crm-text-muted, #687386); font-size: 10px; letter-spacing: .03em; }
.pro-feature-landing__visual { display: block; width: 100%; padding: 0; overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 11px; background: var(--crm-surface, #fff); color: inherit; cursor: zoom-in; text-align: left; box-shadow: 0 12px 24px var(--crm-shadow, rgb(15 23 42 / 8%)); }
.pro-feature-landing__window { display: flex; align-items: center; gap: 4px; padding: 10px 12px; border-bottom: 1px solid var(--crm-line, #dce2e9); background: var(--crm-surface-soft, #f6f7f9); }
.pro-feature-landing__window i { width: 5px; height: 5px; border-radius: 50%; background: var(--crm-line, #dce2e9); }
.pro-feature-landing__window > span { margin-left: 8px; color: var(--crm-text-muted, #687386); font-size: 9px; letter-spacing: .08em; }
.pro-feature-landing__window b { margin-left: auto; color: var(--crm-action-accent, #b94a20); font-size: 14px; }
.pro-feature-landing__example { display: block; padding: 10px; }
.pro-feature-landing__example :deep(.pro-feature-preview) { border-radius: 7px; }
.pro-feature-landing__enlarge { display: block; padding: 4px 12px 10px; color: var(--crm-text-muted, #687386); font-size: 11px; text-align: right; }
.pro-feature-landing__caption { display: block; margin-top: 10px; color: var(--crm-text-muted, #687386); font-size: 10px; text-align: center; }
.pro-feature-landing__benefits { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 20px; margin: 30px 0; }
.pro-feature-landing__benefits--four { grid-template-columns: repeat(4, minmax(0, 1fr)); }
.pro-feature-landing__benefits article { padding-top: 16px; border-top: 2px solid var(--crm-line, #dce2e9); }
.pro-feature-landing__number { color: var(--crm-action-accent, #b94a20); font-size: 11px; font-weight: 800; }
.pro-feature-landing__benefits h3 { margin: 10px 0 6px; font-size: 16px; line-height: 1.3; }
.pro-feature-landing__benefits p { margin: 0; font-size: 13px; }
.pro-feature-landing__workflow { display: grid; grid-template-columns: minmax(0, .8fr) minmax(0, 1.2fr); gap: 30px; padding: 26px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 14px; background: var(--crm-surface-soft, #f6f7f9); }
.pro-feature-landing__workflow-heading > span { color: var(--crm-action-accent, #b94a20); font-size: 10px; font-weight: 800; letter-spacing: .08em; }
.pro-feature-landing__workflow-heading h3 { margin: 12px 0; font-size: 28px; line-height: 1.15; letter-spacing: -.02em; }
.pro-feature-landing__workflow ol { display: grid; gap: 20px; margin: 0; padding: 0; list-style: none; }
.pro-feature-landing__workflow li { display: flex; align-items: flex-start; gap: 14px; }
.pro-feature-landing__step { display: grid; width: 30px; height: 30px; flex: none; place-items: center; border: 1px solid color-mix(in srgb, var(--crm-action-accent, #b94a20) 30%, var(--crm-line, #dce2e9)); border-radius: 9px; background: var(--crm-surface, #fff); color: var(--crm-action-accent, #b94a20); font-size: 12px; font-weight: 700; }
.pro-feature-landing__workflow h4 { margin: 3px 0 3px; font-size: 14px; }
.pro-feature-landing__workflow p { margin: 0; font-size: 13px; }
.pro-feature-landing__note { margin: 18px 0 0; font-size: 12px !important; }
.pro-feature-landing__visual:focus-visible { outline: 2px solid var(--crm-action-accent, #b94a20); outline-offset: 3px; }
@container (max-width: 700px) {
  .pro-feature-landing__hero, .pro-feature-landing__workflow { grid-template-columns: minmax(0, 1fr); gap: 20px; }
  .pro-feature-landing__pitch { padding: 0; }
  .pro-feature-landing__benefits, .pro-feature-landing__benefits--four { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
@container (max-width: 400px) {
  .pro-feature-landing__stage { padding: 10px; }
  .pro-feature-landing__benefits, .pro-feature-landing__benefits--four { grid-template-columns: minmax(0, 1fr); gap: 14px; }
  .pro-feature-landing__workflow { padding: 18px; }
}
</style>
